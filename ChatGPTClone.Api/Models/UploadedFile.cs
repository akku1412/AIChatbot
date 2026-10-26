namespace ChatGPTClone.Api.Models;

public enum FileProcessingStatus
{
    Uploaded,
    Processing,
    Completed,
    Failed,
}

/// <summary>
/// Metadata of a user's uploaded document. The file itself lives in private storage under
/// <see cref="StorageKey"/>; its extracted text lives in <see cref="DocumentChunk"/> rows.
/// </summary>
public class UploadedFile : IAuditable
{
    /// <summary>Random GUID: unguessable, and used in URLs.</summary>
    public Guid FileId { get; set; }

    public int UserId { get; set; }

    /// <summary>Display name only (sanitised). Never used to build a path.</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>Server-generated storage name ("{guid}.bin"). Never sent to clients.</summary>
    public string StorageKey { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    /// <summary>".pdf", ".docx", ".xlsx" or ".xls" (verified against the file's signature).</summary>
    public string FileExtension { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public FileProcessingStatus ProcessingStatus { get; set; }

    /// <summary>User-facing reason when processing failed or partially succeeded (e.g. "needs OCR", "truncated").</summary>
    public string? StatusMessage { get; set; }

    /// <summary>Machine-readable reason when processing failed (e.g. "encrypted", "empty", "ocr_required").</summary>
    public string? StatusReason { get; set; }

    /// <summary>Background processing progress, e.g. scanned pages read so far (current) of those needing OCR (total).</summary>
    public int? ProgressCurrent { get; set; }
    public int? ProgressTotal { get; set; }

    /// <summary>PDF pages, spreadsheet sheets or presentation slides.</summary>
    public int? PageCount { get; set; }

    public int ExtractedCharacters { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>PNG, JPEG or WebP: sent to the vision model as an image (its OCR text is also searchable).</summary>
    public bool IsImage => FileTypes.IsImage(FileExtension);

    public User User { get; set; } = null!;
    public ICollection<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();
}

public static class FileTypes
{
    public static readonly IReadOnlySet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp",
    };

    public static bool IsImage(string extension) => ImageExtensions.Contains(extension);
}

/// <summary>Where a chunk's text came from.</summary>
public enum ChunkSource
{
    /// <summary>Text stored in the document itself.</summary>
    Text,

    /// <summary>Recognised from an image or a scanned page by OCR (may contain recognition errors).</summary>
    Ocr,
}

/// <summary>The embedding vector of a chunk (float32, L2-normalised) for semantic search.</summary>
public class ChunkEmbedding
{
    public long ChunkId { get; set; }

    /// <summary>The embedding model that produced the vector; vectors from different models are never compared.</summary>
    public string Model { get; set; } = string.Empty;

    public int Dimensions { get; set; }

    /// <summary>Dimensions × 4 bytes, little-endian float32.</summary>
    public byte[] Vector { get; set; } = [];

    public DocumentChunk Chunk { get; set; } = null!;
}

/// <summary>A document chunk that was given to the AI for a reply (its citations point at these).</summary>
public class MessageSource
{
    public long MessageSourceId { get; set; }

    public long MessageId { get; set; }

    public long ChunkId { get; set; }

    /// <summary>Retrieval score (higher = more relevant).</summary>
    public double Score { get; set; }

    public Message Message { get; set; } = null!;
    public DocumentChunk Chunk { get; set; } = null!;
}

public enum DocumentChunkKind
{
    /// <summary>Extracted document text.</summary>
    Text,

    /// <summary>Computed facts (e.g. spreadsheet column totals); always included in the AI context.</summary>
    Summary,
}

/// <summary>A slice of extracted text with where it came from (page, heading or sheet/rows).</summary>
public class DocumentChunk
{
    public long ChunkId { get; set; }

    public Guid FileId { get; set; }

    public int ChunkIndex { get; set; }

    public DocumentChunkKind Kind { get; set; }

    /// <summary>"Page 3", "Sheet 'Sales' rows 2-120", "Section: Introduction".</summary>
    public string Location { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    /// <summary>Document text, or text recognised by OCR.</summary>
    public ChunkSource Source { get; set; } = ChunkSource.Text;

    public UploadedFile File { get; set; } = null!;
    public ChunkEmbedding? Embedding { get; set; }
}

/// <summary>Links a chat message to the files the user attached to it.</summary>
public class MessageAttachment
{
    public long MessageAttachmentId { get; set; }

    public long MessageId { get; set; }

    public Guid FileId { get; set; }

    public Message Message { get; set; } = null!;
    public UploadedFile File { get; set; } = null!;
}
