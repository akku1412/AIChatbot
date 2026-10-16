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

    /// <summary>PDF pages or spreadsheet sheets.</summary>
    public int? PageCount { get; set; }

    public int ExtractedCharacters { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public ICollection<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();
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

    public UploadedFile File { get; set; } = null!;
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
