using System.IO.Compression;
using System.Text;

namespace ChatGPTClone.Api.Services.Files;

/// <summary>Supported formats and how to recognise them from their bytes (the extension alone is never trusted).</summary>
public static class FileSignature
{
    public static readonly IReadOnlyDictionary<string, (string ContentType, string Description)> Supported =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = ("application/pdf", "PDF document"),
            [".docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "Word document"),
            [".xlsx"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Excel workbook"),
            [".xls"] = ("application/vnd.ms-excel", "Excel 97-2003 workbook"),
            [".txt"] = ("text/plain", "Text file"),
        };

    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] OleMagic = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    /// <summary>Null when the content matches the extension; otherwise a user-facing reason.</summary>
    public static string? Validate(Stream content, string extension)
    {
        var header = new byte[1024];
        var read = content.Read(header, 0, header.Length);
        content.Position = 0;

        switch (extension.ToLowerInvariant())
        {
            case ".pdf":
                // "%PDF-" must appear near the start (some generators put a few bytes before it).
                return Encoding.ASCII.GetString(header, 0, read).Contains("%PDF-", StringComparison.Ordinal)
                    ? null
                    : "This file is not a valid PDF.";

            case ".docx":
                return ValidateOpenXml(content, header, read, "word/document.xml", "word/vbaProject.bin", "Word document");

            case ".xlsx":
                return ValidateOpenXml(content, header, read, "xl/workbook.xml", "xl/vbaProject.bin", "Excel workbook");

            case ".xls":
                return read >= OleMagic.Length && header.AsSpan(0, OleMagic.Length).SequenceEqual(OleMagic)
                    ? null
                    : "This file is not a valid Excel 97-2003 (.xls) workbook.";

            case ".txt":
                return LooksLikeText(header, read) ? null : "This file is not a plain text file.";

            default:
                return "Unsupported file type.";
        }
    }

    /// <summary>
    /// Text has no magic bytes, so reject what is clearly binary: known document signatures (a .zip/.docx, .xls or
    /// .pdf renamed to .txt) and NUL bytes, which never appear in UTF-8 or ANSI text but always appear early in
    /// executables and images. UTF-16 is allowed only with its BOM.
    /// </summary>
    private static bool LooksLikeText(byte[] header, int read)
    {
        var span = header.AsSpan(0, read);
        if (span.StartsWith((byte[])[0xFF, 0xFE]) || span.StartsWith((byte[])[0xFE, 0xFF]))
        {
            return true; // UTF-16 with BOM: NUL bytes are expected
        }
        if (span.StartsWith(ZipMagic) || span.StartsWith(OleMagic) || span.StartsWith("%PDF"u8))
        {
            return false;
        }
        return !span.Contains((byte)0);
    }

    private static string? ValidateOpenXml(Stream content, byte[] header, int read, string requiredPart, string macroPart, string kind)
    {
        if (read < ZipMagic.Length || !header.AsSpan(0, ZipMagic.Length).SequenceEqual(ZipMagic))
        {
            return $"This file is not a valid {kind}.";
        }

        try
        {
            using var zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            var names = zip.Entries.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!names.Contains("[Content_Types].xml") || !names.Contains(requiredPart))
            {
                return $"This file is not a valid {kind}.";
            }
            if (names.Contains(macroPart))
            {
                // Macro-enabled content renamed to .docx/.xlsx: never accept it.
                return $"This {kind.ToLowerInvariant()} contains macros, which are not allowed.";
            }
            return null;
        }
        catch (InvalidDataException)
        {
            return $"This {kind.ToLowerInvariant()} is damaged and cannot be opened.";
        }
        finally
        {
            content.Position = 0;
        }
    }
}
