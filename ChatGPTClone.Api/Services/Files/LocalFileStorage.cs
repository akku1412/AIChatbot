using System.Text.RegularExpressions;
using ChatGPTClone.Api.Configuration;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Services.Files;

/// <summary>Private binary storage for uploads. Swap for a cloud implementation (e.g. Azure Blob) in production.</summary>
public interface IFileStorage
{
    /// <summary>Streams the content to a new, server-named file and returns its storage key.</summary>
    Task<string> SaveAsync(Stream content, CancellationToken cancellationToken);

    Stream OpenRead(string storageKey);

    Task DeleteAsync(string storageKey);
}

/// <summary>
/// Stores files on local disk in a folder outside wwwroot, under random names ("{guid}.bin").
/// Keys are validated against a strict pattern before any path is built, so path traversal is impossible.
/// </summary>
public sealed partial class LocalFileStorage : IFileStorage
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
        var key = $"{Guid.NewGuid():N}.bin";
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

    public Stream OpenRead(string storageKey) =>
        new FileStream(PathFor(storageKey), FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920);

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

    [GeneratedRegex("^[a-f0-9]{32}\\.bin$")]
    private static partial Regex KeyPattern();
}
