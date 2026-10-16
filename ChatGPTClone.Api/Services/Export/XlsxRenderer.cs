using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace ChatGPTClone.Api.Services.Export;

/// <summary>
/// Excel workbook via ClosedXML (MIT). Each Markdown table becomes a worksheet named after the heading above it,
/// with a bold, frozen, filterable header row and typed cells (numbers, percentages, currency amounts, dates).
/// A reply without tables but with a "Label: value" list (a common AI answer shape) becomes a two-column sheet.
/// </summary>
public static partial class XlsxRenderer
{
    public static byte[] Render(IReadOnlyList<DocBlock> blocks, ExportContext context)
    {
        var sheets = FindTables(blocks);
        if (sheets.Count == 0)
        {
            throw new ExportNotPossibleException(
                "This reply has no table or list of values to put in a spreadsheet. Download it as PDF, DOCX or TXT instead.");
        }

        using var workbook = new XLWorkbook();
        workbook.Properties.Title = context.Title;
        workbook.Properties.Author = "AIChat";
        workbook.Properties.Created = context.CreatedAtUtc;

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < sheets.Count; i++)
        {
            var (name, table) = sheets[i];
            var sheet = workbook.Worksheets.Add(UniqueSheetName(name ?? $"Table {i + 1}", usedNames));

            for (var c = 0; c < table.Header.Count; c++)
            {
                sheet.Cell(1, c + 1).Value = table.Header[c];
            }
            for (var r = 0; r < table.Rows.Count; r++)
            {
                for (var c = 0; c < table.Header.Count; c++)
                {
                    SetTyped(sheet.Cell(r + 2, c + 1), table.Rows[r][c]);
                }
            }

            var header = sheet.Range(1, 1, 1, table.Header.Count);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2FF");
            header.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            sheet.SheetView.FreezeRows(1);
            if (table.Rows.Count > 0)
            {
                sheet.Range(1, 1, table.Rows.Count + 1, table.Header.Count).SetAutoFilter();
            }
            sheet.Columns(1, table.Header.Count).AdjustToContents(1, Math.Min(table.Rows.Count + 1, 500));
            foreach (var column in sheet.Columns(1, table.Header.Count))
            {
                column.Width = Math.Clamp(column.Width + 2, 10, 60);
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Tables (named by the nearest heading above) or, if there are none, "Label: value" lists.</summary>
    internal static List<(string? Name, TableBlock Table)> FindTables(IReadOnlyList<DocBlock> blocks)
    {
        var found = new List<(string?, TableBlock)>();
        string? heading = null;
        foreach (var block in blocks)
        {
            if (block is HeadingBlock h)
            {
                heading = RunText.Plain(h.Runs);
            }
            else if (block is TableBlock table)
            {
                found.Add((heading, table));
                heading = null; // a second table under the same heading gets a numbered name
            }
        }
        if (found.Count > 0)
        {
            return found;
        }

        // Fallback: a list where (nearly) every item is "Label: value", e.g. "- **Venue:** Shubh Banquet Hall".
        var pairs = blocks.OfType<ListItemBlock>()
            .Where(i => i.Marker.Length > 0)
            .Select(i => RunText.Plain(i.Runs))
            .Select(text => KeyValue().Match(text))
            .Where(m => m.Success)
            .Select(m => (IReadOnlyList<string>)[m.Groups["key"].Value.Trim(), m.Groups["value"].Value.Trim()])
            .ToList();
        var items = blocks.OfType<ListItemBlock>().Count(i => i.Marker.Length > 0);
        if (pairs.Count >= 2 && pairs.Count * 10 >= items * 8)
        {
            var title = blocks.OfType<HeadingBlock>().Select(b => RunText.Plain(b.Runs)).FirstOrDefault();
            found.Add((title ?? "Details", new TableBlock(["Item", "Value"], pairs)));
        }
        return found;
    }

    /// <summary>Numbers stay numbers in Excel (sortable, summable); anything else is kept as text.</summary>
    private static void SetTyped(IXLCell cell, string raw)
    {
        var text = raw.Trim();
        if (text.Length == 0)
        {
            return;
        }

        var match = NumberLike().Match(text);
        if (match.Success)
        {
            var digits = match.Groups["num"].Value.Replace(",", "");
            if (decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            {
                var negative = match.Groups["neg"].Success;
                if (negative)
                {
                    number = -number;
                }
                if (match.Groups["pct"].Success)
                {
                    cell.Value = (double)(number / 100m);
                    cell.Style.NumberFormat.Format = digits.Contains('.') ? "0.0#%" : "0%";
                }
                else
                {
                    cell.Value = (double)number;
                    var currency = match.Groups["cur"].Value.Trim();
                    if (currency.Length > 0)
                    {
                        cell.Style.NumberFormat.Format = $"\"{currency}\"#,##0.##";
                    }
                    else if (digits.Contains('.') || match.Groups["num"].Value.Contains(','))
                    {
                        cell.Style.NumberFormat.Format = digits.Contains('.') ? "#,##0.0#" : "#,##0";
                    }
                }
                return;
            }
        }

        if (IsoDate().IsMatch(text) && DateTime.TryParseExact(text, ["yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            cell.Value = date;
            cell.Style.DateFormat.Format = text.Length > 10 ? "yyyy-mm-dd hh:mm" : "yyyy-mm-dd";
            return;
        }

        // Always text, never a formula: a value starting with "=" is stored as a literal string.
        cell.Value = text;
    }

    /// <summary>Excel rules: max 31 characters, none of : \ / ? * [ ], unique (case-insensitive), not blank.</summary>
    internal static string UniqueSheetName(string wanted, HashSet<string> used)
    {
        var name = InvalidSheetCharacters().Replace(wanted, " ").Trim().Trim('\'');
        name = Whitespace().Replace(name, " ");
        if (name.Length == 0 || name.Equals("History", StringComparison.OrdinalIgnoreCase))
        {
            name = "Sheet";
        }
        name = name.Length > 31 ? name[..31].TrimEnd() : name;

        var candidate = name;
        for (var n = 2; !used.Add(candidate); n++)
        {
            var suffix = $" ({n})";
            candidate = (name.Length + suffix.Length > 31 ? name[..(31 - suffix.Length)].TrimEnd() : name) + suffix;
        }
        return candidate;
    }

    [GeneratedRegex(@"^(?<neg>-)?(?<cur>[₹$€£¥]|Rs\.?\s?|INR\s?)?(?<num>\d{1,3}(?:,\d{2,3})+(?:\.\d+)?|\d+(?:\.\d+)?)(?<pct>\s?%)?$", RegexOptions.IgnoreCase)]
    private static partial Regex NumberLike();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}([ T]\d{2}:\d{2}(:\d{2})?)?$")]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"^(?<key>[^:\n]{1,60}):\s+(?<value>.+)$", RegexOptions.Singleline)]
    private static partial Regex KeyValue();

    [GeneratedRegex(@"[:\\/?*\[\]\p{C}]")]
    private static partial Regex InvalidSheetCharacters();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
