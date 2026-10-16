using System.Globalization;
using System.Text;
using ExcelDataReader;
using ExcelDataReader.Exceptions;

namespace ChatGPTClone.Api.Services.Files;

/// <summary>
/// Excel (.xlsx and .xls) via ExcelDataReader (MIT), streamed row by row.
/// - Each sheet's first non-empty row is treated as the header row.
/// - Rows become "R{excelRow}: Header=value; ..." lines, grouped into row ranges for citations.
/// - Per column, exact statistics (count, blanks, sum, average, min, max; date ranges) are computed here
///   in .NET and stored as a "summary" so the AI quotes calculated numbers instead of doing arithmetic.
/// - Formulas: the value Excel saved with the file (cached result) is used; formulas are never evaluated.
///   A formula saved without a cached value (some generated files) reads as blank.
/// </summary>
public sealed class SpreadsheetExtractor : IDocumentExtractor
{
    private const int RowsPerSection = 40;

    static SpreadsheetExtractor()
    {
        // .xls (BIFF) files use legacy code pages.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public bool CanHandle(string extension) =>
        extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".xls", StringComparison.OrdinalIgnoreCase);

    public ExtractionResult Extract(Stream content, string extension, ExtractionLimits limits)
    {
        IExcelDataReader reader;
        try
        {
            reader = extension.Equals(".xls", StringComparison.OrdinalIgnoreCase)
                ? ExcelReaderFactory.CreateBinaryReader(content)
                : ExcelReaderFactory.CreateOpenXmlReader(content);
        }
        catch (InvalidPasswordException ex)
        {
            throw new DocumentExtractionException("This workbook is password-protected. Remove the password and upload it again.", "encrypted", ex);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new DocumentExtractionException("This Excel file is damaged or not a valid workbook.", "corrupted", ex);
        }

        using (reader)
        {
            var budget = new TextBudget(limits.MaxCharacters);
            var sections = new List<ExtractedSection>();
            var warnings = new List<string>();
            var sheetCount = 0;

            try
            {
                do
                {
                    sheetCount++;
                    ReadSheet(reader, budget, limits, sections, warnings);
                }
                while (reader.NextResult());
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not DocumentExtractionException)
            {
                throw new DocumentExtractionException("This Excel file could not be read completely; it may be damaged.", "corrupted", ex);
            }

            if (sections.All(s => s.Kind == Models.DocumentChunkKind.Summary && s.Text.Contains("is empty")))
            {
                throw new DocumentExtractionException("This workbook has no data in any sheet.", "empty");
            }

            if (budget.Exhausted)
            {
                warnings.Add($"Only the first part of the workbook text was read (limit {limits.MaxCharacters:N0} characters); statistics cover the rows read.");
            }
            return new ExtractionResult(sections, sheetCount, warnings.Count > 0 ? string.Join(' ', warnings) : null);
        }
    }

    private static void ReadSheet(IExcelDataReader reader, TextBudget budget, ExtractionLimits limits,
        List<ExtractedSection> sections, List<string> warnings)
    {
        var sheet = string.IsNullOrWhiteSpace(reader.Name) ? "Sheet" : reader.Name.Trim();
        string[]? headers = null;
        ColumnStats[] stats = [];
        var excelRow = 0;
        var dataRows = 0;
        var rowsTruncated = false;

        var block = new StringBuilder();
        int? blockFirstRow = null;
        var blockLastRow = 0;
        var blockRows = 0;

        void FlushBlock()
        {
            if (blockRows > 0 && !budget.Exhausted)
            {
                var text = budget.Take(block.ToString().TrimEnd());
                if (text.Length > 0)
                {
                    sections.Add(new ExtractedSection($"Sheet '{sheet}' rows {blockFirstRow}-{blockLastRow}", text));
                }
            }
            block.Clear();
            blockFirstRow = null;
            blockRows = 0;
        }

        while (reader.Read())
        {
            excelRow++;
            var values = new object?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                values[i] = reader.GetValue(i);
            }
            if (values.All(IsBlank))
            {
                continue;
            }

            if (headers is null)
            {
                headers = BuildHeaders(values);
                stats = headers.Select((h, i) => new ColumnStats(h, ColumnLetter(i))).ToArray();
                continue;
            }

            if (dataRows >= limits.MaxRowsPerSheet)
            {
                rowsTruncated = true;
                break;
            }
            dataRows++;

            var cells = new List<string>();
            for (var i = 0; i < headers.Length; i++)
            {
                var value = i < values.Length ? values[i] : null;
                stats[i].Add(value);
                if (!IsBlank(value))
                {
                    cells.Add($"{headers[i]}={Format(value)}");
                }
            }
            // Values in columns to the right of the header row still matter.
            for (var i = headers.Length; i < values.Length; i++)
            {
                if (!IsBlank(values[i]))
                {
                    cells.Add($"{ColumnLetter(i)}={Format(values[i])}");
                }
            }

            if (!budget.Exhausted)
            {
                blockFirstRow ??= excelRow;
                blockLastRow = excelRow;
                blockRows++;
                block.Append('R').Append(excelRow).Append(": ").AppendLine(string.Join("; ", cells));
                if (blockRows >= RowsPerSection)
                {
                    FlushBlock();
                }
            }
        }
        FlushBlock();

        if (headers is null)
        {
            sections.Add(new ExtractedSection($"Sheet '{sheet}' summary", $"Sheet '{sheet}' is empty.", Models.DocumentChunkKind.Summary));
            return;
        }

        if (rowsTruncated)
        {
            warnings.Add($"Sheet '{sheet}' has more than {limits.MaxRowsPerSheet:N0} data rows; only the first {limits.MaxRowsPerSheet:N0} were read.");
        }

        var summary = new StringBuilder()
            .Append($"Sheet '{sheet}': {dataRows:N0} data rows")
            .Append(rowsTruncated ? " (truncated)" : string.Empty)
            .AppendLine($", {headers.Length} columns. Header row: {string.Join(", ", headers)}.")
            .AppendLine("Exact statistics computed by the server (formula cells use their saved values):");
        foreach (var column in stats)
        {
            summary.AppendLine(column.Describe(dataRows));
        }
        sections.Add(new ExtractedSection($"Sheet '{sheet}' summary", summary.ToString().TrimEnd(), Models.DocumentChunkKind.Summary));
    }

    private static string[] BuildHeaders(object?[] values)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var headers = new string[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var name = IsBlank(values[i]) ? $"Column {ColumnLetter(i)}" : Format(values[i]).Replace(';', ',').Replace('=', '-').Trim();
            if (name.Length > 60)
            {
                name = name[..60];
            }
            var unique = name;
            for (var n = 2; !used.Add(unique); n++)
            {
                unique = $"{name} ({n})";
            }
            headers[i] = unique;
        }
        return headers;
    }

    internal static bool IsBlank(object? value) =>
        value is null || value is DBNull || (value is string s && string.IsNullOrWhiteSpace(s));

    internal static string Format(object? value) => value switch
    {
        null or DBNull => string.Empty,
        DateTime d when d.TimeOfDay == TimeSpan.Zero => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime d => d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        double d => FormatNumber(d),
        float f => FormatNumber(f),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        int or long or short => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        bool b => b ? "TRUE" : "FALSE",
        TimeSpan t => t.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
        string s => s.Replace("\r", " ").Replace('\n', ' ').Trim(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    internal static string FormatNumber(double value) =>
        Math.Abs(value - Math.Round(value)) < 1e-9 && Math.Abs(value) < 1e15
            ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.####", CultureInfo.InvariantCulture);

    internal static string ColumnLetter(int index)
    {
        var letters = string.Empty;
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            letters = (char)('A' + (n - 1) % 26) + letters;
        }
        return letters;
    }

    /// <summary>Running statistics for one column (no rows are kept in memory).</summary>
    private sealed class ColumnStats(string header, string letter)
    {
        private int _nonBlank;
        private int _numbers;
        private double _sum;
        private double _min = double.MaxValue;
        private double _max = double.MinValue;
        private int _dates;
        private DateTime _minDate = DateTime.MaxValue;
        private DateTime _maxDate = DateTime.MinValue;
        private int _texts;

        public void Add(object? value)
        {
            if (IsBlank(value))
            {
                return;
            }
            _nonBlank++;
            switch (value)
            {
                case DateTime d:
                    _dates++;
                    if (d < _minDate) _minDate = d;
                    if (d > _maxDate) _maxDate = d;
                    break;
                case double or float or decimal or int or long or short:
                    var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    _numbers++;
                    _sum += number;
                    if (number < _min) _min = number;
                    if (number > _max) _max = number;
                    break;
                default:
                    _texts++;
                    break;
            }
        }

        public string Describe(int rows)
        {
            var blanks = rows - _nonBlank;
            var line = new StringBuilder($"- Column {letter} '{header}': {_nonBlank} values, {blanks} blank");
            if (_numbers > 0)
            {
                line.Append($"; numeric {_numbers}: sum={FormatNumber(_sum)}, average={FormatNumber(_sum / _numbers)}, " +
                            $"min={FormatNumber(_min)}, max={FormatNumber(_max)}");
            }
            if (_dates > 0)
            {
                line.Append($"; dates {_dates}: earliest={Format(_minDate)}, latest={Format(_maxDate)}");
            }
            if (_texts > 0)
            {
                line.Append($"; text {_texts}");
            }
            return line.ToString();
        }
    }
}
