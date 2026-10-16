using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.DTOs.Files;

/// <summary>An uploaded file as clients see it. Never includes storage keys or server paths.</summary>
public sealed record UploadedFileDto(
    Guid FileId,
    string FileName,
    string FileExtension,
    string ContentType,
    long FileSize,
    string Status,
    string? StatusMessage,
    int? PageCount,
    int ExtractedCharacters,
    DateTime CreatedAt)
{
    public static UploadedFileDto From(UploadedFile f) =>
        new(f.FileId, f.OriginalFileName, f.FileExtension, f.ContentType, f.FileSize,
            f.ProcessingStatus.ToString().ToLowerInvariant(), f.StatusMessage, f.PageCount, f.ExtractedCharacters, f.CreatedAt);
}

/// <summary>A file shown on a chat message.</summary>
public sealed record AttachmentDto(Guid FileId, string FileName, string FileExtension, long FileSize);

/// <summary>Limits the Angular app mirrors for instant validation (the server enforces them regardless).</summary>
public sealed record UploadLimitsDto(long MaxFileSizeBytes, int MaxFilesPerMessage, IReadOnlyList<string> AllowedExtensions);
