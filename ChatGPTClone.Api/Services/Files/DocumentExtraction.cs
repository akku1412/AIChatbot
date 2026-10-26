using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.Services.Files;

/// <summary>A piece of extracted content and where it came from.</summary>
public sealed record ExtractedSection(string Location, string Text, DocumentChunkKind Kind = DocumentChunkKind.Text,
    ChunkSource Source = ChunkSource.Text);

public sealed record ExtractionResult(IReadOnlyList<ExtractedSection> Sections, int? PageCount, string? Warning);

/// <param name="ReportProgress">Called with (done, total) during slow steps such as OCR of scanned pages.</param>
public sealed record ExtractionLimits(int MaxCharacters, int MaxRowsPerSheet, Action<int, int>? ReportProgress = null);

/// <summary>Text recognition (OCR) for scanned pages and pictures.</summary>
public interface IOcrEngine
{
    /// <summary>False when OCR isn't set up (no language data); scanned documents are then rejected as before.</summary>
    bool IsAvailable { get; }

    /// <summary>The text in an image (PNG/JPEG/WebP bytes), lines separated by "\n"; empty when none is found.</summary>
    string Recognize(byte[] image);
}

/// <summary>The document can't be used: corrupted, password-protected, scanned without text, empty...</summary>
public sealed class DocumentExtractionException(string userMessage, string reason, Exception? inner = null)
    : Exception(userMessage, inner)
{
    /// <summary>Machine-readable: corrupted, encrypted, ocr_required, empty.</summary>
    public string Reason { get; } = reason;
}

public interface IDocumentExtractor
{
    bool CanHandle(string extension);

    /// <summary>Reads text from a seekable stream. Never executes document content (macros, scripts, links).</summary>
    ExtractionResult Extract(Stream content, string extension, ExtractionLimits limits);
}

/// <summary>Accumulates text and stops cleanly at the character budget.</summary>
internal sealed class TextBudget(int maxCharacters)
{
    public int Used { get; private set; }
    public bool Exhausted => Used >= maxCharacters;

    /// <summary>Returns the part of <paramref name="text"/> that still fits.</summary>
    public string Take(string text)
    {
        var room = maxCharacters - Used;
        if (room <= 0)
        {
            return string.Empty;
        }
        var taken = text.Length <= room ? text : text[..room];
        Used += taken.Length;
        return taken;
    }
}
