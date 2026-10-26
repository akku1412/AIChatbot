using System.Net;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using ChatGPTClone.Api.Services.Files;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChatGPTClone.Api.Tests.Unit;

/// <summary>The cloud stores, driven by the real Azure/AWS SDKs against fake HTTP endpoints (no cloud account needed).</summary>
public class FileStorageTests
{
    /// <summary>Records requests; answers with whatever the test's responder returns.</summary>
    private sealed class FakeHttp(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Query, HttpRequestMessage Request)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query, request));
            return respond(request, body);
        }
    }

    private static HttpResponseMessage Status(HttpStatusCode status, string? body = null, Dictionary<string, string>? headers = null)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body ?? "")) };
        // A real MD5 ETag: the S3 SDK checks downloads against it (Azure accepts any quoted value).
        response.Headers.TryAddWithoutValidation("ETag", $"\"{Convert.ToHexString(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(body ?? ""))).ToLowerInvariant()}\"");
        response.Content.Headers.TryAddWithoutValidation("Last-Modified", DateTime.UtcNow.ToString("R"));
        foreach (var (name, value) in headers ?? [])
        {
            if (!response.Headers.TryAddWithoutValidation(name, value))
            {
                response.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }
        return response;
    }

    // ---------- Azure Blob ----------

    private static AzureBlobFileStorage Azure(FakeHttp http) => new(
        new BlobContainerClient(new Uri("https://account.blob.core.windows.net/uploads"),
            new BlobClientOptions { Transport = new HttpClientTransport(new HttpClient(http)), Retry = { MaxRetries = 0 } }),
        NullLogger<AzureBlobFileStorage>.Instance);

    [Fact]
    public async Task Azure_creates_a_private_container_once_and_stores_under_a_server_made_key()
    {
        var http = new FakeHttp((_, _) => Status(HttpStatusCode.Created));
        var storage = Azure(http);

        var first = await storage.SaveAsync(new MemoryStream("first"u8.ToArray()), CancellationToken.None);
        await storage.SaveAsync(new MemoryStream("second"u8.ToArray()), CancellationToken.None);

        Assert.Matches("^[a-f0-9]{32}\\.bin$", first);
        var createContainer = Assert.Single(http.Calls, c => c.Query.Contains("restype=container"));
        Assert.False(createContainer.Request.Headers.Contains("x-ms-blob-public-access")); // no public access header = private
        var puts = http.Calls.Where(c => c.Method == HttpMethod.Put && !c.Query.Contains("restype")).ToList();
        Assert.Equal(2, puts.Count);
        Assert.Equal($"/uploads/{first}", puts[0].Path);
        Assert.Equal("*", puts[0].Request.Headers.GetValues("If-None-Match").Single()); // never overwrite
    }

    [Fact]
    public async Task Azure_reads_into_a_seekable_stream_and_reports_missing_blobs_as_FileNotFound()
    {
        const string key = "0123456789abcdef0123456789abcdef.bin";
        var http = new FakeHttp((request, _) => request.RequestUri!.AbsolutePath.EndsWith(key)
            ? Status(HttpStatusCode.OK, "hello", new() { ["x-ms-blob-type"] = "BlockBlob", ["Content-Length"] = "5" })
            : Status(HttpStatusCode.NotFound, null, new() { ["x-ms-error-code"] = "BlobNotFound" }));
        var storage = Azure(http);

        await using var stream = await storage.OpenReadAsync(key, CancellationToken.None);
        Assert.True(stream.CanSeek);
        Assert.Equal("hello", await new StreamReader(stream).ReadToEndAsync());

        await Assert.ThrowsAsync<FileNotFoundException>(() => storage.OpenReadAsync("fedcba9876543210fedcba9876543210.bin", CancellationToken.None));
    }

    // ---------- S3 ----------

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(handler);
        public override bool UseSDKHttpClientCaching(IClientConfig clientConfig) => false;
        public override bool DisposeHttpClientsAfterUse(IClientConfig clientConfig) => false;
    }

    private static S3FileStorage S3(FakeHttp http) => new(
        new AmazonS3Client(new BasicAWSCredentials("test-key", "test-secret"), new AmazonS3Config
        {
            ServiceURL = "http://s3.test",
            ForcePathStyle = true,
            HttpClientFactory = new FakeHttpClientFactory(http),
            MaxErrorRetry = 0,
        }),
        "aichat-uploads");

    [Fact]
    public async Task S3_stores_encrypted_objects_under_a_server_made_key()
    {
        var http = new FakeHttp((_, _) => Status(HttpStatusCode.OK));

        var key = await S3(http).SaveAsync(new MemoryStream("data"u8.ToArray()), CancellationToken.None);

        Assert.Matches("^[a-f0-9]{32}\\.bin$", key);
        var put = Assert.Single(http.Calls, c => c.Method == HttpMethod.Put);
        Assert.Equal($"/aichat-uploads/{key}", put.Path);
        Assert.Equal("AES256", put.Request.Headers.GetValues("x-amz-server-side-encryption").Single());
        Assert.False(put.Request.Headers.Contains("x-amz-acl")); // no public ACL
    }

    [Fact]
    public async Task S3_reads_into_a_seekable_stream_and_reports_missing_objects_as_FileNotFound()
    {
        const string key = "0123456789abcdef0123456789abcdef.bin";
        var http = new FakeHttp((request, _) => request.RequestUri!.AbsolutePath.EndsWith(key)
            ? Status(HttpStatusCode.OK, "hello", new() { ["Content-Length"] = "5" })
            : Status(HttpStatusCode.NotFound, "<Error><Code>NoSuchKey</Code><Message>gone</Message></Error>", new() { ["Content-Type"] = "application/xml" }));
        var storage = S3(http);

        await using var stream = await storage.OpenReadAsync(key, CancellationToken.None);
        Assert.True(stream.CanSeek);
        Assert.Equal("hello", await new StreamReader(stream).ReadToEndAsync());

        await Assert.ThrowsAsync<FileNotFoundException>(() => storage.OpenReadAsync("fedcba9876543210fedcba9876543210.bin", CancellationToken.None));
    }

    // ---------- Keys ----------

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF.bin")]
    [InlineData("0123456789abcdef0123456789abcdef.exe")]
    [InlineData("uploads/0123456789abcdef0123456789abcdef.bin")]
    public async Task Every_store_refuses_keys_it_did_not_make(string key)
    {
        var never = new FakeHttp((_, _) => throw new InvalidOperationException("must not be called"));
        await Assert.ThrowsAsync<ArgumentException>(() => Azure(never).OpenReadAsync(key, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => S3(never).OpenReadAsync(key, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => S3(never).DeleteAsync(key));
        Assert.Empty(never.Calls);
    }
}
