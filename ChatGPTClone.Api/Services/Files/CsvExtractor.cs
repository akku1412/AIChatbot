using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace ChatGPTClone.Api.Services.Files;

/// <summary>
/// CSV files via CsvHelper (MS-PL / Apache-2.0): the delimiter (comma, semicolon, tab, pipe) is detected, values are
/// typed (numbers, dates) and the rows go through the same path as Excel sheets, so totals, averages, min/max and
/// blanks are computed exactly by the server. Cited as "Sheet 'data' rows 2-41".
/// </summary>
public sealed class CsvExtractor : IDocumentExtractor
{
    public bool CanHandle(string extension) => extension.Equals(".csv", StringComparison.OrdinalIgnoreCase);

    public ExtractionResult Extract(Stream content, string extension, ExtractionLimits limits)
    {
        // Same encoding detection as .txt files (BOM, UTF-8, Windows-1252), then parse the decoded text.
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        var text = TextFileExtractor.Decode(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new DocumentExtractionException("This CSV file is empty.", "empty");
        }

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            DetectDelimiter = true,
            DetectDelimiterValues = [",", ";", "\t", "|"],
            HasHeaderRecord = false, // the shared table reader treats the first non-blank row as the header
            BadDataFound = null,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
        };

        var budget = new TextBudget(limits.MaxCharacters);
        var sections = new List<ExtractedSection>();
        var warnings = new List<string>();
        try
        {
            using var reader = new CsvReader(new StringReader(text), config);
            SpreadsheetExtractor.ReadRows("data", Rows(reader), budget, limits, sections, warnings);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not DocumentExtractionException)
        {
            throw new DocumentExtractionException("This CSV file could not be read; check that it is a valid comma-separated file.", "corrupted", ex);
        }

        if (sections.All(s => s.Kind == Models.DocumentChunkKind.Summary && s.Text.Contains("is empty")))
        {
            throw new DocumentExtractionException("This CSV file has no data.", "empty");
        }
        if (budget.Exhausted)
        {
            warnings.Add($"Only the first part of the file text was read (limit {limits.MaxCharacters:N0} characters); statistics cover the rows read.");
        }
        return new ExtractionResult(sections, 1, warnings.Count > 0 ? string.Join(' ', warnings) : null);
    }

    private static IEnumerable<object?[]> Rows(CsvReader reader)
    {
        while (reader.Read())
        {
            var record = reader.Parser.Record ?? [];
            yield return record.Select(Typed).ToArray();
        }
    }

    /// <summary>"1,200.50" → 1200.5, "12%" stays text, "2026-03-01" → date; everything else is text.</summary>
    internal static object? Typed(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }
        if (DateTime.TryParseExact(text, ["yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }
        return text;
    }
}
