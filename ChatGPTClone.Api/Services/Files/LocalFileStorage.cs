using System.Text.RegularExpressions;
using ChatGPTClone.Api.Configuration;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Services.Files;

/// <summary>
/// Private binary storage for uploads: local disk (development), Azure Blob Storage or Amazon S3, chosen by
/// FileStorage:Provider. Nothing is ever public: downloads stream through the API after the ownership check.
/// Keys are server-made ("{32 hex}.bin") and validated by every implementation.
/// </summary>
public interface IFileStorage
{
    /// <summary>Streams the content to a new, server-named object and returns its storage key.</summary>
    Task<string> SaveAsync(Stream content, CancellationToken cancellationToken);

    /// <summary>A readable, seekable stream (the caller disposes it). Throws FileNotFoundException when it is gone.</summary>
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);

    /// <summary>Deletes the object; missing objects are not an error.</summary>
    Task DeleteAsync(string storageKey);
}

/// <summary>
/// Stores files on local disk in a folder outside wwwroot, under random names ("{guid}.bin").
/// Keys are validated against a strict pattern before any path is built, so path traversal is impossible.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IOptions<FileUploadOptions> options, IWebHostEnvironment environment)
    {
        var configured = options.Value.StoragePath;
        _root = Path.GetFullPath(Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured));

        var webRoot = environment.WebRootPath is null ? null : Path.GetFullPath(environment.WebRootPath);
        if (webRoot is not null && _root.StartsWith(webRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("FileUploads:StoragePath must not be inside wwwroot (uploads would become public).");
        }

        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, CancellationToken cancellationToken)
    {
        var key = StorageKeys.New();
        var path = PathFor(key);
        try
        {
            await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true);
            await content.CopyToAsync(target, cancellationToken);
        }
        catch
        {
            TryDelete(path);
            throw;
        }
        return key;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(new FileStream(PathFor(storageKey), FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920));

    public Task DeleteAsync(string storageKey)
    {
        TryDelete(PathFor(storageKey));
        return Task.CompletedTask;
    }

    private string PathFor(string storageKey)
    {
        if (!KeyPattern().IsMatch(storageKey))
        {
            throw new ArgumentException("Invalid storage key.", nameof(storageKey));
        }

        var full = Path.GetFullPath(Path.Combine(_root, storageKey));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Invalid storage key.", nameof(storageKey));
        }
        return full;
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static Regex KeyPattern() => StorageKeys.Pattern();
}

/// <summary>Storage keys are made by the server: 32 hex characters + ".bin". Every store refuses anything else.</summary>
public static partial class StorageKeys
{
    public static string New() => $"{Guid.NewGuid():N}.bin";

    public static void Validate(string storageKey)
    {
        if (!Pattern().IsMatch(storageKey))
        {
            throw new ArgumentException("Invalid storage key.", nameof(storageKey));
        }
    }

    [GeneratedRegex("^[a-f0-9]{32}\\.bin$")]
    public static partial Regex Pattern();
}
