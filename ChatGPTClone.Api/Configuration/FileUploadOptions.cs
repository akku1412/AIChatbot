using System.ComponentModel.DataAnnotations;

namespace ChatGPTClone.Api.Configuration;

/// <summary>Bound from "FileUploads". Limits are enforced on the server (Angular mirrors them for early feedback).</summary>
public sealed class FileUploadOptions
{
    public const string SectionName = "FileUploads";

    /// <summary>Private folder for uploaded files. Relative paths are resolved against the app's content root (never wwwroot).</summary>
    [Required]
    public string StoragePath { get; init; } = "App_Data/uploads";

    [Range(1024, 200L * 1024 * 1024)]
    public long MaxFileSizeBytes { get; init; } = 20L * 1024 * 1024;

    /// <summary>Images (PNG/JPEG/WebP) have their own, smaller limit.</summary>
    [Range(1024, 50L * 1024 * 1024)]
    public long MaxImageSizeBytes { get; init; } = 10L * 1024 * 1024;

    [Range(1, 20)]
    public int MaxFilesPerMessage { get; init; } = 5;

    /// <summary>Background processing (text extraction, OCR, indexing) of one file stops after this long.</summary>
    [Range(10, 3600)]
    public int ProcessingTimeoutSeconds { get; init; } = 300;

    /// <summary>Stop extracting after this many characters per file (protects memory, the database and the AI context).</summary>
    [Range(1000, 10_000_000)]
    public int MaxExtractedCharacters { get; init; } = 1_000_000;

    /// <summary>Spreadsheets: rows read per sheet (statistics and text both stop here).</summary>
    [Range(10, 1_000_000)]
    public int MaxRowsPerSheet { get; init; } = 50_000;

    /// <summary>Target size of one stored text chunk.</summary>
    [Range(200, 20_000)]
    public int ChunkCharacters { get; init; } = 2_000;

    /// <summary>How much document text (all attached files together) goes to the AI per question.</summary>
    [Range(1000, 400_000)]
    public int MaxDocumentContextCharacters { get; init; } = 24_000;

    /// <summary>Earlier attachments in the same conversation stay available for follow-up questions, up to this many files.</summary>
    [Range(1, 20)]
    public int MaxFilesInContext { get; init; } = 5;
}
