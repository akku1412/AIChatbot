using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Data;
using ChatGPTClone.Api.DTOs.Files;
using ChatGPTClone.Api.Middleware;
using ChatGPTClone.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Services.Files;

public interface IFileService
{
    /// <summary>Validates, stores and extracts the file; returns it as Completed or throws ApiException.</summary>
    Task<UploadedFileDto> UploadAsync(int userId, IFormFile file, CancellationToken cancellationToken);

    Task<UploadedFileDto> GetAsync(int userId, Guid fileId, CancellationToken cancellationToken);

    Task<IReadOnlyList<UploadedFileDto>> ListAsync(int userId, int take, CancellationToken cancellationToken);

    Task DeleteAsync(int userId, Guid fileId, CancellationToken cancellationToken);

    /// <summary>The user's files with these ids, all Completed; throws 404/409 otherwise.</summary>
    Task<List<UploadedFile>> GetReadyFilesAsync(int userId, IReadOnlyCollection<Guid> fileIds, CancellationToken cancellationToken);

    UploadLimitsDto Limits { get; }
}

public sealed class FileService(
    AppDbContext db,
    IFileStorage storage,
    IEnumerable<IDocumentExtractor> extractors,
    IOptions<FileUploadOptions> options,
    ILogger<FileService> logger) : IFileService
{
    private static readonly TimeSpan ProcessingTimeout = TimeSpan.FromSeconds(60);
    private readonly FileUploadOptions _options = options.Value;

    public UploadLimitsDto Limits => new(_options.MaxFileSizeBytes, _options.MaxFilesPerMessage, FileSignature.Supported.Keys.ToList());

    public async Task<UploadedFileDto> UploadAsync(int userId, IFormFile file, CancellationToken cancellationToken)
    {
        // ---- Cheap checks before touching disk ----
        if (file.Length == 0)
        {
            throw Problem(400, "Empty file", "The file is empty.", "empty");
        }
        if (file.Length > _options.MaxFileSizeBytes)
        {
            throw Problem(413, "File too large",
                $"Files can be at most {_options.MaxFileSizeBytes / (1024 * 1024)} MB.", "too_large");
        }

        var displayName = SanitizeFileName(file.FileName);
        var extension = Path.GetExtension(displayName).ToLowerInvariant();
        if (!FileSignature.Supported.TryGetValue(extension, out var format))
        {
            throw Problem(415, "Unsupported file type",
                "Only PDF (.pdf), Word (.docx) and Excel (.xlsx, .xls) files are supported.", "unsupported");
        }

        // ---- Store under a random name, then verify the real format from the bytes ----
        string storageKey;
        await using (var upload = file.OpenReadStream())
        {
            storageKey = await storage.SaveAsync(upload, cancellationToken);
        }

        var record = new UploadedFile
        {
            FileId = Guid.NewGuid(),
            UserId = userId,
            OriginalFileName = displayName,
            StorageKey = storageKey,
            ContentType = format.ContentType, // from the verified format, never from the client
            FileExtension = extension,
            FileSize = file.Length,
            ProcessingStatus = FileProcessingStatus.Uploaded,
        };

        try
        {
            await using (var stored = storage.OpenRead(storageKey))
            {
                var mismatch = FileSignature.Validate(stored, extension);
                if (mismatch is not null)
                {
                    throw Problem(415, "Invalid file", $"{mismatch} Make sure the file isn't damaged or renamed.", "invalid_format");
                }
            }

            record.ProcessingStatus = FileProcessingStatus.Processing;
            db.UploadedFiles.Add(record);
            await db.SaveChangesAsync(cancellationToken);

            var result = await ExtractAsync(storageKey, extension, cancellationToken);
            var chunks = DocumentChunker.Chunk(record.FileId, result.Sections, _options.ChunkCharacters);

            db.DocumentChunks.AddRange(chunks);
            record.ProcessingStatus = FileProcessingStatus.Completed;
            record.PageCount = result.PageCount;
            record.ExtractedCharacters = result.Sections.Sum(s => s.Text.Length);
            record.StatusMessage = result.Warning;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("File {FileId} ({Extension}, {Size} bytes) processed into {Chunks} chunks for user {UserId}",
                record.FileId, extension, file.Length, chunks.Count, userId);
            return UploadedFileDto.From(record);
        }
        catch (Exception ex)
        {
            // Failed uploads are removed completely (file + metadata): nothing half-processed is left behind.
            await CleanUpFailedAsync(record, storageKey);

            if (ex is DocumentExtractionException extraction)
            {
                logger.LogInformation("File upload rejected for user {UserId}: {Reason}", userId, extraction.Reason);
                throw Problem(422, "Couldn't read this file", extraction.Message, extraction.Reason);
            }
            if (ex is ApiException or OperationCanceledException)
            {
                throw;
            }

            logger.LogError(ex, "Processing upload failed for user {UserId}", userId);
            throw Problem(500, "Upload failed", "We couldn't process this file. Please try again.", "processing_failed");
        }
    }

    public async Task<UploadedFileDto> GetAsync(int userId, Guid fileId, CancellationToken cancellationToken) =>
        UploadedFileDto.From(await GetOwnedAsync(userId, fileId, cancellationToken));

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

        // Attachment links use NO ACTION (SQL Server multiple-cascade-path rule), so remove them first.
        await db.MessageAttachments.Where(a => a.FileId == fileId).ExecuteDeleteAsync(cancellationToken);
        db.UploadedFiles.Remove(file); // chunks cascade
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
        logger.LogInformation("File {FileId} deleted by user {UserId}", fileId, userId);
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
            throw Problem(409, "File not ready", $"\"{notReady.OriginalFileName}\" isn't ready yet. Wait for it to finish processing.", "file_not_ready");
        }
        return files;
    }

    private async Task<ExtractionResult> ExtractAsync(string storageKey, string extension, CancellationToken cancellationToken)
    {
        var extractor = extractors.FirstOrDefault(e => e.CanHandle(extension))
            ?? throw Problem(415, "Unsupported file type", "This file type can't be read.", "unsupported");
        var limits = new ExtractionLimits(_options.MaxExtractedCharacters, _options.MaxRowsPerSheet);

        // Parsers are synchronous CPU work: run off the request thread and bound the time.
        var work = Task.Run(() =>
        {
            using var stream = storage.OpenRead(storageKey);
            return extractor.Extract(stream, extension, limits);
        }, cancellationToken);

        var finished = await Task.WhenAny(work, Task.Delay(ProcessingTimeout, cancellationToken));
        if (finished != work)
        {
            throw Problem(422, "Processing timed out",
                "This file took too long to process. Try a smaller file or split it into parts.", "timeout");
        }
        return await work;
    }

    private async Task CleanUpFailedAsync(UploadedFile record, string storageKey)
    {
        try
        {
            if (db.Entry(record).State != EntityState.Detached)
            {
                db.ChangeTracker.Clear();
                await db.UploadedFiles.Where(f => f.FileId == record.FileId).ExecuteDeleteAsync();
            }
            await storage.DeleteAsync(storageKey);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cleanup after failed upload {FileId} was incomplete", record.FileId);
        }
    }

    private async Task<UploadedFile> GetOwnedAsync(int userId, Guid fileId, CancellationToken cancellationToken) =>
        await db.UploadedFiles.FirstOrDefaultAsync(f => f.FileId == fileId && f.UserId == userId, cancellationToken)
        // 404 for other users' files too: ids reveal nothing.
        ?? throw Problem(404, "File not found", "This file doesn't exist or has been deleted.", "file_not_found");

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
