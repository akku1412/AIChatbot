using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Data;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Models;
using ChatGPTClone.Api.Services;
using ChatGPTClone.Api.Services.Ai;
using ChatGPTClone.Api.Services.Audio;
using ChatGPTClone.Api.Services.Web;
using ChatGPTClone.Api.Services.Embeddings;
using ChatGPTClone.Api.Services.Files;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChatGPTClone.Api.Tests.Integration;

/// <summary>
/// Runs the real API in memory against a throwaway SQL Server database (created from the EF migrations,
/// dropped afterwards). Only the external services are replaced: Google token validation, email, and AI.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestDatabase = "ChatGPTCloneDB_Tests";

    /// <summary>Listed in Admin:Emails, so signing in with it makes the account an admin.</summary>
    public const string AdminEmail = "admin@example.test";

    /// <summary>SqlServer (default) or PostgreSQL: the whole suite runs against either database.</summary>
    private static readonly string Provider = Environment.GetEnvironmentVariable("CHATGPTCLONE_TEST_PROVIDER") ?? "SqlServer";

    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("CHATGPTCLONE_TEST_DB")
        ?? $@"Server=AKANSHA\SQL2016;Database={TestDatabase};Trusted_Connection=True;TrustServerCertificate=True";

    public FakeEmailSender Email { get; } = new();
    public FakeAiProvider Ai { get; } = new();
    public FakeWebSearch WebSearch { get; } = new();
    public FakeSpeechToText Speech { get; } = new();
    public FakeMalwareScanner Scanner { get; } = new();

    /// <summary>Private upload folder for this test run (deleted afterwards).</summary>
    public string StoragePath { get; } = Path.Combine(Path.GetTempPath(), "chatgptclone-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>Small limit so the "too large" path is cheap to test.</summary>
    public const long MaxFileSize = 1024 * 1024;

    /// <summary>Images have their own (smaller) limit.</summary>
    public const long MaxImageSize = 300 * 1024;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
        builder.UseSetting("Database:Provider", Provider);
        builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));
        builder.UseSetting("Otp:HashKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("Otp:MaxSendsPerHour", "100");
        builder.UseSetting("Smtp:Username", "unused@example.test");
        builder.UseSetting("Smtp:Password", "unused");
        builder.UseSetting("Smtp:FromEmail", "unused@example.test");
        builder.UseSetting("Ai:Provider", "OpenAI");
        // Two models for the picker tests; the fake factory below serves both, recording which one answered.
        builder.UseSetting("Ai:DefaultModel", "smart");
        builder.UseSetting("Ai:Models:0:Id", "fast");
        builder.UseSetting("Ai:Models:0:Label", "Fast");
        builder.UseSetting("Ai:Models:0:Model", "fast-model-name");
        builder.UseSetting("Ai:Models:1:Id", "smart");
        builder.UseSetting("Ai:Models:1:Label", "Smart");
        builder.UseSetting("Ai:Models:1:Model", "smart-model-name");
        builder.UseSetting("Ai:Models:1:SupportsVision", "true");
        builder.UseSetting("Admin:Emails:0", AdminEmail);
        builder.UseSetting("RateLimits:GlobalPerMinute", "100000");
        builder.UseSetting("RateLimits:AuthPerMinute", "100000");
        builder.UseSetting("RateLimits:AiChatPerMinute", "100000");
        builder.UseSetting("RateLimits:UploadsPerMinute", "100000");
        builder.UseSetting("RateLimits:SharedLinksPerMinute", "100000");
        builder.UseSetting("FileUploads:StoragePath", StoragePath);
        builder.UseSetting("FileUploads:MaxFileSizeBytes", MaxFileSize.ToString());
        builder.UseSetting("FileUploads:MaxImageSizeBytes", MaxImageSize.ToString());

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IGoogleTokenValidator, FakeGoogleTokenValidator>();
            services.AddSingleton<IEmailSender>(Email);
            services.AddSingleton<IAiProviderFactory>(new FakeAiProviderFactory(Ai));
            services.AddSingleton<IWebSearchProvider>(WebSearch);
            services.AddSingleton<ISpeechToText>(Speech);
            services.AddSingleton<IMalwareScanner>(Scanner);
            // The real 118 MB model is exercised by RetrievalQualityTests only; everything else uses this fake.
            services.AddSingleton<IEmbeddingProvider>(Embeddings);
        });
    }

    public FakeEmbeddingProvider Embeddings { get; } = new();

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync(); // also proves the migrations apply cleanly to an empty database
    }

    public new async Task DisposeAsync()
    {
        using (var scope = Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        }
        await base.DisposeAsync();
        if (Directory.Exists(StoragePath))
        {
            Directory.Delete(StoragePath, recursive: true);
        }
    }

    /// <summary>A browser-like client: HTTPS (so Secure cookies work) with its own cookie jar.</summary>
    public HttpClient CreateBrowserClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}

/// <summary>Accepts "google|{sub}|{email}|{name}"; anything else is an invalid token.</summary>
public sealed class FakeGoogleTokenValidator : IGoogleTokenValidator
{
    public Task<GoogleIdentity?> ValidateAsync(string idToken)
    {
        var parts = idToken.Split('|');
        return Task.FromResult(parts is ["google", var sub, var email, var name]
            ? new GoogleIdentity(sub, email.ToLowerInvariant(), name, null)
            : null);
    }
}

/// <summary>Captures the OTP from each email instead of sending it.</summary>
public sealed partial class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentDictionary<string, string> _lastCodeByEmail = new(StringComparer.OrdinalIgnoreCase);

    public Task SendAsync(string toEmail, string toName, string subject, string htmlBody, string textBody,
        CancellationToken cancellationToken)
    {
        _lastCodeByEmail[toEmail] = CodePattern().Match(textBody).Groups[1].Value;
        return Task.CompletedTask;
    }

    public string LastCodeFor(string email) =>
        _lastCodeByEmail.TryGetValue(email, out var code) ? code : throw new InvalidOperationException($"No email sent to {email}");

    [GeneratedRegex(@"code is: (\d{6})")]
    private static partial Regex CodePattern();
}

/// <summary>Every configured model is served by the same fake, which records which model was asked.</summary>
public sealed class FakeAiProviderFactory(FakeAiProvider fake) : IAiProviderFactory
{
    public IAiChatProvider Create(AiModelOption model) => new ForModel(fake, model.Id);

    private sealed class ForModel(FakeAiProvider fake, string modelId) : IAiChatProvider
    {
        public string Name => $"Fake/{modelId}";

        public Task<AiCompletion> CompleteAsync(IReadOnlyList<AiChatMessage> messages, int maxOutputTokens,
            CancellationToken cancellationToken)
        {
            fake.CallingModelId = modelId;
            return fake.CompleteAsync(messages, maxOutputTokens, cancellationToken);
        }

        public IAsyncEnumerable<AiStreamUpdate> StreamAsync(IReadOnlyList<AiChatMessage> messages, int maxOutputTokens,
            CancellationToken cancellationToken)
        {
            fake.CallingModelId = modelId;
            return fake.StreamAsync(messages, maxOutputTokens, cancellationToken);
        }
    }
}

/// <summary>Deterministic AI: echoes the last user message; can be switched to fail.</summary>
public sealed class FakeAiProvider : IAiChatProvider
{
    public string Name => "Fake";

    /// <summary>Set by <see cref="FakeAiProviderFactory"/> before each call.</summary>
    internal string? CallingModelId { get; set; }

    /// <summary>The model that wrote the most recent answer (title prompts excluded).</summary>
    public string? LastModelId { get; private set; }
    public AiFailureKind? FailWith { get; set; }
    public ConcurrentBag<IReadOnlyList<AiChatMessage>> Prompts { get; } = [];

    /// <summary>Prompts that asked for a standalone web search query.</summary>
    public ConcurrentBag<IReadOnlyList<AiChatMessage>> SearchQueryPrompts { get; } = [];

    /// <summary>The most recent prompt (tests in a collection run one at a time).</summary>
    public IReadOnlyList<AiChatMessage>? LastPrompt { get; private set; }

    public Task<AiCompletion> CompleteAsync(IReadOnlyList<AiChatMessage> messages, int maxOutputTokens,
        CancellationToken cancellationToken)
    {
        if (FailWith is { } kind)
        {
            throw new AiProviderException(kind, "simulated failure");
        }

        if (messages[0].Content.StartsWith("Write a short title", StringComparison.Ordinal))
        {
            return Task.FromResult(new AiCompletion("\"Fake Generated Title\"", 5, false));
        }
        if (messages[0].Content == ChatService.SearchQueryInstructions)
        {
            SearchQueryPrompts.Add(messages);
            return Task.FromResult(new AiCompletion("\"fake standalone query\"", 5, false));
        }

        Prompts.Add(messages);
        LastPrompt = messages; // the answer prompt, not the title prompt
        LastModelId = CallingModelId;
        if (Reply is { } reply)
        {
            return Task.FromResult(new AiCompletion(reply(messages), 42, false));
        }
        var lastUser = messages.Last(m => m.Role == MessageRole.User).Content;
        return Task.FromResult(new AiCompletion($"**Echo:** {lastUser}", 42, false));
    }

    /// <summary>When set, the reply text instead of the echo (e.g. a reply that cites passages). Reset after use.</summary>
    public Func<IReadOnlyList<AiChatMessage>, string>? Reply { get; set; }

    // ----- Streaming controls (reset by ResetStreaming) -----

    /// <summary>After this many pieces the stream waits until cancelled (simulates a long reply for Stop tests).</summary>
    public int? HoldAfterPieces { get; set; }

    /// <summary>Completes when the stream has sent HoldAfterPieces pieces and is waiting.</summary>
    public TaskCompletionSource Holding { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Throw this failure after this many pieces (simulates the provider failing mid-reply).</summary>
    public (int AfterPieces, AiFailureKind Kind)? FailMidStream { get; set; }

    public int StreamCalls;

    public void ResetStreaming()
    {
        HoldAfterPieces = null;
        FailMidStream = null;
        FailWith = null;
        Holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Streams the echo reply word by word.</summary>
    public async IAsyncEnumerable<AiStreamUpdate> StreamAsync(IReadOnlyList<AiChatMessage> messages, int maxOutputTokens,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref StreamCalls);
        var full = (await CompleteAsync(messages, maxOutputTokens, cancellationToken)).Content;
        var pieces = System.Text.RegularExpressions.Regex.Split(full, @"(?<= )");
        for (var i = 0; i < pieces.Length; i++)
        {
            if (FailMidStream is { } fail && i == fail.AfterPieces)
            {
                throw new AiProviderException(fail.Kind, "simulated mid-stream failure");
            }
            if (HoldAfterPieces == i)
            {
                Holding.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken); // until Stop / disconnect
            }
            await Task.Yield();
            yield return new AiStreamUpdate(pieces[i]);
        }
        yield return new AiStreamUpdate("", 42, false);
    }
}

/// <summary>
/// Deterministic, dependency-free embeddings: hashed bag of words (lower-cased), plus a tiny synonym table so tests
/// can prove that semantic ranking finds a passage sharing no keyword with the question ("sales" ~ "revenue").
/// </summary>
public sealed partial class FakeEmbeddingProvider : IEmbeddingProvider
{
    private const int Dimensions = 64;
    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sales"] = "revenue", ["income"] = "revenue", ["earnings"] = "revenue", ["grew"] = "increase",
        ["change"] = "increase", ["rose"] = "increase", ["staff"] = "employees", ["workers"] = "employees",
    };

    public bool IsAvailable { get; set; } = true;
    public string Model => "fake-test-model";
    public int Calls;

    /// <summary>When set, EmbedAsync throws (simulates a model failure).</summary>
    public bool Fail { get; set; }

    public Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, EmbeddingKind kind, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        if (Fail)
        {
            throw new InvalidOperationException("simulated embedding failure");
        }
        return Task.FromResult(texts.Select(Embed).ToArray());
    }

    private static float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        foreach (Match word in Word().Matches(text.ToLowerInvariant()))
        {
            var token = Synonyms.GetValueOrDefault(word.Value, word.Value);
            vector[(int)((uint)StringComparer.Ordinal.GetHashCode(token) % Dimensions)] += 1;
        }
        vector[0] += 0.01f; // never all zeros
        return Vectors.Normalize(vector);
    }

    [GeneratedRegex(@"\p{L}{3,}")]
    private static partial Regex Word();
}

/// <summary>Signs users in through the real endpoints.</summary>
public static class TestAuth
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string NewEmail(string prefix = "user") => $"{prefix}-{Guid.NewGuid():N}@example.test";

    /// <summary>Email OTP: send-otp -> read the code from the captured email -> verify-otp. Returns the access token.</summary>
    public static async Task<string> SignInAsync(ApiFactory factory, HttpClient client, string? email = null)
    {
        var session = await SignInWithEmailAsync(factory, client, email ?? NewEmail());
        return session.GetProperty("accessToken").GetString()!;
    }

    public static async Task<JsonElement> SignInWithEmailAsync(ApiFactory factory, HttpClient client, string email)
    {
        var sent = await client.PostAsJsonAsync("/api/auth/send-otp", new { email });
        sent.EnsureSuccessStatusCode();

        var verify = await client.PostAsJsonAsync("/api/auth/verify-otp", new { email, code = factory.Email.LastCodeFor(email) });
        verify.EnsureSuccessStatusCode();
        return await verify.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    /// <summary>Google: the fake validator accepts "google|sub|email|name". Returns the HTTP response.</summary>
    public static Task<HttpResponseMessage> GoogleAsync(HttpClient client, string sub, string email, string name = "Google User") =>
        client.PostAsJsonAsync("/api/auth/google", new { idToken = $"google|{sub}|{email}|{name}" });

    /// <summary>
    /// POST /api/files/upload, then (when accepted) polls GET /api/files/{id} until background processing has finished.
    /// The returned response keeps the upload's status code (201) with the file's final JSON ("completed" or "failed").
    /// </summary>
    public static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string token, string fileName, byte[] content)
    {
        var response = await UploadRawAsync(client, token, fileName, content);
        if (response.StatusCode != System.Net.HttpStatusCode.Created)
        {
            return response;
        }

        var file = await WaitForProcessingAsync(client, token,
            (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("fileId").GetGuid());
        return new HttpResponseMessage(response.StatusCode) { Content = JsonContent.Create(file, options: Json) };
    }

    /// <summary>Polls the file until it is no longer "processing" (OCR of a few pages takes seconds).</summary>
    public static async Task<JsonElement> WaitForProcessingAsync(HttpClient client, string token, Guid fileId, int timeoutSeconds = 90)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (true)
        {
            var get = await client.SendAsync(Authorized(HttpMethod.Get, $"/api/files/{fileId}", token));
            get.EnsureSuccessStatusCode();
            var file = await get.Content.ReadFromJsonAsync<JsonElement>(Json);
            if (file.GetProperty("status").GetString() != "processing")
            {
                return file;
            }
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"File {fileId} was still processing after {timeoutSeconds} s.");
            }
            await Task.Delay(100);
        }
    }

    /// <summary>POST /api/files/upload with one file in the "file" form field; returns as soon as the server accepts it.</summary>
    public static Task<HttpResponseMessage> UploadRawAsync(HttpClient client, string token, string fileName, byte[] content)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new("application/octet-stream");
        form.Add(file, "file", fileName);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/files/upload") { Content = form };
        request.Headers.Authorization = new("Bearer", token);
        return client.SendAsync(request);
    }

    public static HttpRequestMessage Authorized(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return request;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

/// <summary>Search results on demand; records queries; can be switched to fail or to "not configured".</summary>
public sealed class FakeWebSearch : IWebSearchProvider
{
    public bool IsConfigured { get; set; } = true;
    public List<string> Queries { get; } = [];
    public IReadOnlyList<WebResult> Results { get; set; } = [];
    public bool Fail { get; set; }

    public Task<IReadOnlyList<WebResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        Queries.Add(query);
        return Fail ? throw new WebSearchException("simulated search outage") : Task.FromResult(Results);
    }

    public void Reset()
    {
        IsConfigured = true;
        Queries.Clear();
        Results = [];
        Fail = false;
    }
}

/// <summary>Returns a fixed transcript; records what it was sent; can fail or be "not configured".</summary>
public sealed class FakeSpeechToText : ISpeechToText
{
    public bool IsConfigured { get; set; } = true;
    public string Text { get; set; } = "hello from the microphone";
    public AiFailureKind? FailWith { get; set; }
    public List<(string FileName, int Bytes)> Calls { get; } = [];

    public async Task<string> TranscribeAsync(Stream audio, string fileName, CancellationToken cancellationToken)
    {
        using var copy = new MemoryStream();
        await audio.CopyToAsync(copy, cancellationToken);
        Calls.Add((fileName, (int)copy.Length));
        return FailWith is { } kind ? throw new AiProviderException(kind, "simulated transcription failure") : Text;
    }

    public void Reset()
    {
        IsConfigured = true;
        Text = "hello from the microphone";
        FailWith = null;
        Calls.Clear();
    }
}

/// <summary>"Infected" when the file contains the EICAR test marker; can be switched to "scanner unreachable".</summary>
public sealed class FakeMalwareScanner : IMalwareScanner
{
    public const string Marker = "EICAR-STANDARD-ANTIVIRUS-TEST-FILE";
    public bool Enabled => true;
    public bool Unavailable { get; set; }
    public int Scans { get; private set; }

    public async Task<ScanResult> ScanAsync(Stream content, CancellationToken cancellationToken)
    {
        Scans++;
        if (Unavailable)
        {
            throw new MalwareScanUnavailableException("simulated: clamd down");
        }
        using var reader = new StreamReader(content, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        return text.Contains(Marker, StringComparison.Ordinal) ? new ScanResult(true, "Eicar-Test-Signature") : ScanResult.Clean;
    }

    public void Reset() => Unavailable = false;
}
