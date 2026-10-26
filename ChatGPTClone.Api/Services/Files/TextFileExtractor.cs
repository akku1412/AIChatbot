using System.Globalization;
using System.Text;

namespace ChatGPTClone.Api.Services.Files;

/// <summary>
/// Plain text (.txt). Encoding: a BOM wins (UTF-8 / UTF-16 LE / UTF-16 BE); otherwise strict UTF-8, falling back to
/// Windows-1252 for older ANSI files. Line breaks are kept; sections are blocks of lines so answers can cite "Lines 41-80".
/// </summary>
public sealed class TextFileExtractor : IDocumentExtractor
{
    /// <summary>Lines per section (sections are split further by the chunker when they are long).</summary>
    private const int LinesPerSection = 40;

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static TextFileExtractor()
    {
        // Windows-1252 lives in the code pages provider on .NET Core.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public bool CanHandle(string extension) => extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);

    public ExtractionResult Extract(Stream content, string extension, ExtractionLimits limits)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        var text = Decode(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));

        // Normalise line endings and drop characters that are invisible noise (keep tabs and new lines).
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = new string(text.Where(c => c is '\n' or '\t' || !char.IsControl(c)).ToArray());

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new DocumentExtractionException("This text file is empty.", "empty");
        }

        var budget = new TextBudget(limits.MaxCharacters);
        var sections = new List<ExtractedSection>();
        // Trailing blank lines would make the last range overstate the line count ("Lines 1-7" for 6 lines).
        var lines = text.TrimEnd().Split('\n');
        for (var start = 0; start < lines.Length && !budget.Exhausted; start += LinesPerSection)
        {
            var count = Math.Min(LinesPerSection, lines.Length - start);
            var block = string.Join('\n', lines, start, count).Trim('\n');
            if (block.Trim().Length == 0)
            {
                continue;
            }
            var location = count == 1 ? $"Line {start + 1}" : $"Lines {start + 1}-{start + count}";
            sections.Add(new ExtractedSection(location, budget.Take(block)));
        }

        var warning = budget.Exhausted && sections.Sum(s => s.Text.Length) < text.Length
            ? string.Create(CultureInfo.InvariantCulture, $"Only the first {limits.MaxCharacters:N0} characters of this file were read.")
            : null;
        return new ExtractionResult(sections, PageCount: null, warning);
    }

    internal static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((byte[])[0xEF, 0xBB, 0xBF]))
        {
            return Encoding.UTF8.GetString(bytes[3..]);
        }
        if (bytes.StartsWith((byte[])[0xFF, 0xFE]))
        {
            return Encoding.Unicode.GetString(bytes[2..]);
        }
        if (bytes.StartsWith((byte[])[0xFE, 0xFF]))
        {
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }
}
