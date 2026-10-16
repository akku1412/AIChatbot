using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.Services.Files;

/// <summary>A piece of extracted content and where it came from.</summary>
public sealed record ExtractedSection(string Location, string Text, DocumentChunkKind Kind = DocumentChunkKind.Text);

public sealed record ExtractionResult(IReadOnlyList<ExtractedSection> Sections, int? PageCount, string? Warning);

public sealed record ExtractionLimits(int MaxCharacters, int MaxRowsPerSheet);

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
