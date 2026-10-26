namespace ChatGPTClone.Api.Configuration;

/// <summary>
/// Bound from "FileStorage": where uploads are kept. "Local" (default; FileUploads:StoragePath on disk), "AzureBlob"
/// or "S3". Containers and buckets must be private: downloads always stream through the API, never via public URLs.
/// </summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    public string Provider { get; init; } = "Local";

    public AzureBlobStorageOptions AzureBlob { get; init; } = new();

    public S3StorageOptions S3 { get; init; } = new();
}

public sealed class AzureBlobStorageOptions
{
    /// <summary>Secret (user secrets / FileStorage__AzureBlob__ConnectionString).</summary>
    public string? ConnectionString { get; init; }

    /// <summary>Created as a private container if it doesn't exist.</summary>
    public string Container { get; init; } = "uploads";
}

public sealed class S3StorageOptions
{
    public string? Bucket { get; init; }

    /// <summary>e.g. "eu-west-1".</summary>
    public string? Region { get; init; }

    /// <summary>Optional: an S3-compatible endpoint (MinIO, Cloudflare R2…). Empty = AWS.</summary>
    public string? ServiceUrl { get; init; }

    /// <summary>Optional keys (secrets). Empty = the AWS default credentials (environment, profile, instance role).</summary>
    public string? AccessKeyId { get; init; }

    public string? SecretAccessKey { get; init; }
}
