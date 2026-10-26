using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Data;
using ChatGPTClone.Api.DTOs.Files;
using ChatGPTClone.Api.Middleware;
using ChatGPTClone.Api.Models;
using ChatGPTClone.Api.Services.Usage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Services.Files;

public interface IFileService
{
    /// <summary>
    /// Validates (size, type, real format from the bytes) and stores the file privately, then queues it for background
    /// processing. Returns status "processing"; poll GET /api/files/{id} until "completed" or "failed".
    /// </summary>
    Task<UploadedFileDto> UploadAsync(int userId, IFormFile file, CancellationToken cancellationToken);

    Task<UploadedFileDto> GetAsync(int userId, Guid fileId, CancellationToken cancellationToken);

    Task<IReadOnlyList<UploadedFileDto>> ListAsync(int userId, int take, CancellationToken cancellationToken);

    Task DeleteAsync(int userId, Guid fileId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> files uploaded before the cutoff that were never attached to a message
    /// or shared with a project (with their chunks and stored bytes). Returns how many were deleted.
    /// </summary>
    Task<int> DeleteAbandonedAsync(DateTime uploadedBeforeUtc, int batchSize, CancellationToken cancellationToken);

    /// <summary>The original bytes of the user's file, with its original name and verified content type. 404 otherwise.</summary>
    Task<FileDownload> OpenDownloadAsync(int userId, Guid fileId, CancellationToken cancellationToken);

    /// <summary>One extracted passage of the user's file (what a citation points at). 404 otherwise.</summary>
    Task<ChunkDto> GetChunkAsync(int userId, Guid fileId, long chunkId, CancellationToken cancellationToken);

    /// <summary>The user's files with these ids, all Completed; throws 404/409 otherwise.</summary>
    Task<List<UploadedFile>> GetReadyFilesAsync(int userId, IReadOnlyCollection<Guid> fileIds, CancellationToken cancellationToken);

    UploadLimitsDto Limits { get; }
}

/// <summary>An open stream (the caller disposes it) plus what the browser needs to save it.</summary>
public sealed record FileDownload(Stream Content, string ContentType, string FileName, long Length);

public sealed class FileService(
    AppDbContext db,
    IFileStorage storage,
    IDocumentProcessingQueue queue,
    IOptions<FileUploadOptions> options,
    IMalwareScanner scanner,
    IOptions<MalwareScanOptions> scanOptions,
    IUsageService usage,
    ILogger<FileService> logger) : IFileService
{
    private readonly FileUploadOptions _options = options.Value;

    public UploadLimitsDto Limits => new(_options.MaxFileSizeBytes, _options.MaxFilesPerMessage,
        FileSignature.Supported.Keys.ToList(), _options.MaxImageSizeBytes, FileTypes.ImageExtensions.ToList());

    public async Task<UploadedFileDto> UploadAsync(int userId, IFormFile file, CancellationToken cancellationToken)
    {
        // ---- Cheap checks before touching disk ----
        if (file.Length == 0)
        {
            throw Problem(400, "Empty file", "The file is empty.", "empty");
        }
        await usage.EnsureUploadQuotaAsync(userId, cancellationToken);

        var displayName = SanitizeFileName(file.FileName);
        var extension = Path.GetExtension(displayName).ToLowerInvariant();
        if (extension == ".doc")
        {
            throw Problem(415, "Old Word format",
                "Word 97–2003 (.doc) files aren't supported. Open the file in Word, choose File → Save As → Word Document (.docx), and upload that.",
                "doc_not_supported");
        }
        if (!FileSignature.Supported.TryGetValue(extension, out var format))
        {
            throw Problem(415, "Unsupported file type",
                "Supported files: PDF, Word (.docx), Excel (.xlsx, .xls), CSV, PowerPoint (.pptx), text (.txt) and images (PNG, JPG, WebP).",
                "unsupported");
        }

        var isImage = FileTypes.IsImage(extension);
        var limit = isImage ? _options.MaxImageSizeBytes : _options.MaxFileSizeBytes;
        if (file.Length > limit)
        {
            throw Problem(413, "File too large",
                $"{(isImage ? "Images" : "Files")} can be at most {limit / (1024 * 1024)} MB.", "too_large");
        }

        // ---- Verify the real format from the bytes; images are cleaned (metadata removed) before they are stored ----
        await using var upload = file.OpenReadStream();
        using var bytes = new MemoryStream();
        await upload.CopyToAsync(bytes, cancellationToken);
        bytes.Position = 0;

        var mismatch = FileSignature.Validate(bytes, extension);
        if (mismatch is not null)
        {
            throw Problem(415, "Invalid file", $"{mismatch} Make sure the file isn't damaged or renamed.", "invalid_format");
        }

        // ---- Virus scan before anything is stored: an infected file never reaches storage ----
        await ScanAsync(bytes, displayName, userId, cancellationToken);
        bytes.Position = 0;

        Stream toStore = bytes;
        if (isImage)
        {
            try
            {
                toStore = new MemoryStream(ImageProcessor.Clean(bytes.ToArray(), extension).Bytes);
            }
            catch (DocumentExtractionException ex)
            {
                throw Problem(422, "Couldn't read this image", ex.Message, ex.Reason);
            }
        }

        var storageKey = await storage.SaveAsync(toStore, cancellationToken);
        var record = new UploadedFile
        {
            FileId = Guid.NewGuid(),
            UserId = userId,
            OriginalFileName = displayName,
            StorageKey = storageKey,
            ContentType = format.ContentType, // from the verified format, never from the client
            FileExtension = extension,
            FileSize = toStore.Length,
            ProcessingStatus = FileProcessingStatus.Processing,
            StatusMessage = "Waiting to be read",
        };

        try
        {
            db.UploadedFiles.Add(record);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await storage.DeleteAsync(storageKey);
            throw;
        }

        await usage.RecordAsync(userId, new UsageDelta(Uploads: 1, UploadBytes: record.FileSize), CancellationToken.None);
        queue.Enqueue(new DocumentJob(record.FileId, DocumentJobKind.Process));
        logger.LogInformation("File {FileId} ({Extension}, {Size} bytes) uploaded by user {UserId}; queued for processing",
            record.FileId, extension, record.FileSize, userId);
        return UploadedFileDto.From(record);
    }

    public async Task<UploadedFileDto> GetAsync(int userId, Guid fileId, CancellationToken cancellationToken) =>
        UploadedFileDto.From(await GetOwnedAsync(userId, fileId, cancellationToken, track: false));

    public async Task<IReadOnlyList<UploadedFileDto>> ListAsync(int userId, int take, CancellationToken cancellationToken) =>
        (await db.UploadedFiles.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken))
        .Select(UploadedFileDto.From)
        .ToList();

    public async Task DeleteAsync(int userId, Guid fileId, CancellationToken cancellationToken)
    {
        var file = await GetOwnedAsync(userId, fileId, cancellationToken);
        await RemoveAsync(file, cancellationToken);
        logger.LogInformation("File {FileId} deleted by user {UserId}", fileId, userId);
    }

    public async Task<int> DeleteAbandonedAsync(DateTime uploadedBeforeUtc, int batchSize, CancellationToken cancellationToken)
    {
        // Never sent with a message and not shared with a project: an upload the user removed from the composer
        // without us hearing about it, or left behind when they closed the tab.
        var abandoned = await db.UploadedFiles
            .Where(f => f.CreatedAt < uploadedBeforeUtc
                && !db.MessageAttachments.Any(a => a.FileId == f.FileId)
                && !db.ProjectFiles.Any(pf => pf.FileId == f.FileId))
            .OrderBy(f => f.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        foreach (var file in abandoned)
        {
            await RemoveAsync(file, cancellationToken);
        }
        return abandoned.Count;
    }

    /// <summary>422 for malware; 503 when the scanner can't answer and MalwareScan:FailClosed is on (the default).</summary>
    private async Task ScanAsync(MemoryStream bytes, string fileName, int userId, CancellationToken cancellationToken)
    {
        if (!scanner.Enabled)
        {
            return;
        }
        try
        {
            bytes.Position = 0;
            var result = await scanner.ScanAsync(bytes, cancellationToken);
            if (result.Infected)
            {
                logger.LogWarning("Upload by user {UserId} rejected: malware {Threat} detected", userId, result.Threat);
                throw Problem(422, "File rejected", $"\"{fileName}\" looks harmful ({result.Threat}), so it wasn't saved.", "malware_detected");
            }
        }
        catch (MalwareScanUnavailableException ex)
        {
            if (scanOptions.Value.FailClosed)
            {
                logger.LogError(ex, "Virus scan unavailable; upload refused (MalwareScan:FailClosed)");
                throw Problem(503, "Couldn't check the file",
                    "We couldn't check this file for viruses right now. Please try again in a moment.", "scan_unavailable");
            }
            logger.LogWarning(ex, "Virus scan unavailable; upload accepted unchecked (MalwareScan:FailClosed = false)");
        }
    }

    /// <summary>Deletes a file's links, record, extracted text and stored bytes.</summary>
    private async Task RemoveAsync(UploadedFile file, CancellationToken cancellationToken)
    {
        var fileId = file.FileId;
        // Attachment, citation and project links use NO ACTION (SQL Server multiple-cascade-path rule), so remove them first.
        await db.MessageAttachments.Where(a => a.FileId == fileId).ExecuteDeleteAsync(cancellationToken);
        await db.MessageSources.Where(s => s.Chunk.FileId == fileId).ExecuteDeleteAsync(cancellationToken);
        await db.ProjectFiles.Where(pf => pf.FileId == fileId).ExecuteDeleteAsync(cancellationToken);
        db.UploadedFiles.Remove(file); // chunks and their embeddings cascade
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await storage.DeleteAsync(file.StorageKey);
        }
        catch (Exception ex)
        {
            // Metadata is gone, so the orphaned blob is unreachable; log for cleanup.
            logger.LogWarning(ex, "Could not delete stored blob for file {FileId}", fileId);
        }
    }

    public async Task<ChunkDto> GetChunkAsync(int userId, Guid fileId, long chunkId, CancellationToken cancellationToken)
    {
        var chunk = await db.DocumentChunks.AsNoTracking()
            .Where(c => c.ChunkId == chunkId && c.FileId == fileId && c.File.UserId == userId)
            .Select(c => new ChunkDto(c.ChunkId, c.FileId, c.File.OriginalFileName, c.Location, c.Content,
                c.Source.ToString().ToLower(), c.Kind == DocumentChunkKind.Summary))
            .FirstOrDefaultAsync(cancellationToken);
        // Same 404 for "doesn't exist", "belongs to another file" and "belongs to another user".
        return chunk ?? throw Problem(404, "Source not found", "This source passage doesn't exist or has been deleted.", "chunk_not_found");
    }

    public async Task<List<UploadedFile>> GetReadyFilesAsync(int userId, IReadOnlyCollection<Guid> fileIds, CancellationToken cancellationToken)
    {
        if (fileIds.Count == 0)
        {
            return [];
        }

        var files = await db.UploadedFiles
            .Where(f => f.UserId == userId && fileIds.Contains(f.FileId))
            .ToListAsync(cancellationToken);

        if (files.Count != fileIds.Distinct().Count())
        {
            throw Problem(404, "File not found", "One or more attached files were not found. Remove them and attach again.", "file_not_found");
        }
        var notReady = files.FirstOrDefault(f => f.ProcessingStatus != FileProcessingStatus.Completed);
        if (notReady is not null)
        {
            throw notReady.ProcessingStatus == FileProcessingStatus.Failed
                ? Problem(409, "File can't be used", $"\"{notReady.OriginalFileName}\" couldn't be read: {notReady.StatusMessage}", "file_failed")
                : Problem(409, "File not ready", $"\"{notReady.OriginalFileName}\" isn't ready yet. Wait for it to finish processing.", "file_not_ready");
        }
        return files;
    }

    public async Task<FileDownload> OpenDownloadAsync(int userId, Guid fileId, CancellationToken cancellationToken)
    {
        var file = await GetOwnedAsync(userId, fileId, cancellationToken, track: false);
        if (file.ProcessingStatus == FileProcessingStatus.Failed)
        {
            throw Problem(404, "File not found", "This file couldn't be processed and is no longer stored.", "file_failed");
        }

        try
        {
            // Name and type come from our own records (sanitized at upload / verified from the bytes), never from the request.
            return new FileDownload(await storage.OpenReadAsync(file.StorageKey, cancellationToken), file.ContentType, file.OriginalFileName, file.FileSize);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            logger.LogWarning("Stored content missing for file {FileId}", file.FileId);
            throw Problem(404, "File not found", "This file is no longer available.", "file_missing");
        }
    }

    private async Task<UploadedFile> GetOwnedAsync(int userId, Guid fileId, CancellationToken cancellationToken, bool track = true)
    {
        var query = track ? db.UploadedFiles : db.UploadedFiles.AsNoTracking();
        return await query.FirstOrDefaultAsync(f => f.FileId == fileId && f.UserId == userId, cancellationToken)
            // 404 for other users' files too: ids reveal nothing.
            ?? throw Problem(404, "File not found", "This file doesn't exist or has been deleted.", "file_not_found");
    }

    /// <summary>Keeps only a safe display name (no path parts, no control characters, bounded length).</summary>
    internal static string SanitizeFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c) && c is not ('<' or '>' or '"' or '|' or '?' or '*' or ':')).ToArray()).Trim();
        if (name.Length == 0)
        {
            name = "file";
        }
        if (name.Length > 200)
        {
            var extension = Path.GetExtension(name);
            name = name[..(200 - extension.Length)] + extension;
        }
        return name;
    }

    private static ApiException Problem(int status, string title, string detail, string reason) =>
        new(status, title, detail, new Dictionary<string, object?> { ["reason"] = reason });
}
