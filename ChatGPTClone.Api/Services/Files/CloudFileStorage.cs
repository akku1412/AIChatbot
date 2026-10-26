using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace ChatGPTClone.Api.Services.Files;

/// <summary>
/// Azure Blob Storage. The container is private (created as such if missing); objects are only ever read here and
/// streamed to the owner by the API. Reads are buffered in memory because the extractors need seekable streams
/// (uploads are at most FileUploads:MaxFileSizeBytes).
/// </summary>
public sealed class AzureBlobFileStorage(BlobContainerClient container, ILogger<AzureBlobFileStorage> logger) : IFileStorage
{
    private int _containerReady;

    public async Task<string> SaveAsync(Stream content, CancellationToken cancellationToken)
    {
        await EnsureContainerAsync(cancellationToken);
        var key = StorageKeys.New();
        // overwrite: false — keys are random, so a clash means something is wrong; never replace someone's file.
        await container.GetBlobClient(key).UploadAsync(content, overwrite: false, cancellationToken);
        return key;
    }

    public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        StorageKeys.Validate(storageKey);
        try
        {
            var buffer = new MemoryStream();
            await container.GetBlobClient(storageKey).DownloadToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            return buffer;
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException("Stored file not found.", storageKey, ex);
        }
    }

    public async Task DeleteAsync(string storageKey)
    {
        StorageKeys.Validate(storageKey);
        await container.GetBlobClient(storageKey).DeleteIfExistsAsync();
    }

    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _containerReady, 1, 0) == 0)
        {
            try
            {
                // PublicAccessType.None: no anonymous access, ever.
                await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
            }
            catch
            {
                _containerReady = 0;
                throw;
            }
            logger.LogInformation("Using private Azure Blob container {Container}", container.Name);
        }
    }
}

/// <summary>
/// Amazon S3 (or an S3-compatible store). Objects are written with server-side encryption and no public ACL; the
/// bucket itself should have "Block all public access" on. Reads are buffered like the Azure store.
/// </summary>
public sealed class S3FileStorage(IAmazonS3 s3, string bucket) : IFileStorage
{
    public async Task<string> SaveAsync(Stream content, CancellationToken cancellationToken)
    {
        var key = StorageKeys.New();
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = content,
            AutoCloseStream = false,
            ContentType = "application/octet-stream",
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256,
        }, cancellationToken);
        return key;
    }

    public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        StorageKeys.Validate(storageKey);
        try
        {
            using var response = await s3.GetObjectAsync(bucket, storageKey, cancellationToken);
            var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            return buffer;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException("Stored file not found.", storageKey, ex);
        }
    }

    public async Task DeleteAsync(string storageKey)
    {
        StorageKeys.Validate(storageKey);
        await s3.DeleteObjectAsync(bucket, storageKey); // deleting a missing key is not an error in S3
    }
}
