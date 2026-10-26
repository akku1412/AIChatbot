using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.DTOs.Files;

/// <summary>
/// An uploaded file as clients see it. Never includes storage keys or server paths.
/// Status: "processing" (being read in the background; poll this), "completed" or "failed" (StatusMessage says why,
/// StatusReason is machine-readable). While processing, ProgressCurrent/ProgressTotal count e.g. scanned pages read.
/// </summary>
public sealed record UploadedFileDto(
    Guid FileId,
    string FileName,
    string FileExtension,
    string ContentType,
    long FileSize,
    string Status,
    string? StatusMessage,
    string? StatusReason,
    int? ProgressCurrent,
    int? ProgressTotal,
    int? PageCount,
    int ExtractedCharacters,
    bool IsImage,
    DateTime CreatedAt)
{
    public static UploadedFileDto From(UploadedFile f) =>
        new(f.FileId, f.OriginalFileName, f.FileExtension, f.ContentType, f.FileSize,
            f.ProcessingStatus.ToString().ToLowerInvariant(), f.StatusMessage, f.StatusReason, f.ProgressCurrent, f.ProgressTotal,
            f.PageCount, f.ExtractedCharacters, f.IsImage, f.CreatedAt);
}

/// <summary>A file shown on a chat message.</summary>
public sealed record AttachmentDto(Guid FileId, string FileName, string FileExtension, long FileSize, bool IsImage);

/// <summary>A passage the AI was given for a reply; the reply cites it as [file:ChunkId].</summary>
public sealed record SourceDto(long ChunkId, Guid FileId, string FileName, string Location, bool IsOcr);

/// <summary>GET /api/files/{fileId}/chunks/{chunkId}: the cited passage, for the source preview panel.</summary>
public sealed record ChunkDto(long ChunkId, Guid FileId, string FileName, string Location, string Content, string Source, bool IsSummary);

/// <summary>Limits the Angular app mirrors for instant validation (the server enforces them regardless).</summary>
public sealed record UploadLimitsDto(long MaxFileSizeBytes, int MaxFilesPerMessage, IReadOnlyList<string> AllowedExtensions,
    long MaxImageSizeBytes, IReadOnlyList<string> ImageExtensions);
