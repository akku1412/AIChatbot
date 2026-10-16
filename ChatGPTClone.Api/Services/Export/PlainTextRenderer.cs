using System.Text;

namespace ChatGPTClone.Api.Services.Export;

/// <summary>
/// Markdown → readable plain text: no ** or # symbols, headings underlined, lists indented,
/// tables as aligned columns, code indented. Line breaks are CRLF so every editor shows them.
/// </summary>
public static class PlainTextRenderer
{
    private const string NewLine = "\r\n";

    public static string Render(IReadOnlyList<DocBlock> blocks)
    {
        var text = new StringBuilder();
        DocBlock? previous = null;
        foreach (var block in blocks)
        {
            // Consecutive list items stay together; everything else is separated by a blank line.
            if (previous is not null && !(previous is ListItemBlock && block is ListItemBlock))
            {
                text.Append(NewLine);
            }

            switch (block)
            {
                case HeadingBlock heading:
                    var title = Lines(RunText.Plain(heading.Runs));
                    text.Append(title).Append(NewLine);
                    if (heading.Level <= 2)
                    {
                        text.Append(new string(heading.Level == 1 ? '=' : '-', Math.Min(LongestLine(title), 80))).Append(NewLine);
                    }
                    break;

                case ParagraphBlock paragraph:
                    text.Append(Lines(RunText.Plain(paragraph.Runs))).Append(NewLine);
                    break;

                case ListItemBlock item:
                    var indent = new string(' ', item.Depth * 3);
                    var marker = item.Marker.Length == 0 ? "  " : item.Marker + " ";
                    var hanging = indent + new string(' ', marker.Length);
                    var body = RunText.Plain(item.Runs).Replace("\r\n", "\n").Split('\n');
                    text.Append(indent).Append(marker).Append(body[0]).Append(NewLine);
                    foreach (var line in body.Skip(1))
                    {
                        text.Append(hanging).Append(line).Append(NewLine);
                    }
                    break;

                case CodeBlock code:
                    foreach (var line in code.Text.Replace("\r\n", "\n").Split('\n'))
                    {
                        text.Append("    ").Append(line).Append(NewLine);
                    }
                    break;

                case QuoteBlock quote:
                    foreach (var line in RunText.Plain(quote.Runs).Replace("\r\n", "\n").Split('\n'))
                    {
                        text.Append("> ").Append(line).Append(NewLine);
                    }
                    break;

                case TableBlock table:
                    AppendTable(text, table);
                    break;

                case RuleBlock:
                    text.Append(new string('-', 40)).Append(NewLine);
                    break;
            }
            previous = block;
        }
        return text.ToString().TrimEnd() + NewLine;
    }

    private static void AppendTable(StringBuilder text, TableBlock table)
    {
        var widths = table.Header.Select((h, i) =>
            Math.Min(40, Math.Max(h.Length, table.Rows.Count == 0 ? 0 : table.Rows.Max(r => r[i].Length)))).ToList();

        string Row(IReadOnlyList<string> cells) =>
            string.Join(" | ", cells.Select((c, i) => c.Length > widths[i] ? c : c.PadRight(widths[i]))).TrimEnd();

        text.Append(Row(table.Header)).Append(NewLine);
        text.Append(string.Join("-+-", widths.Select(w => new string('-', Math.Max(w, 1))))).Append(NewLine);
        foreach (var row in table.Rows)
        {
            text.Append(Row(row)).Append(NewLine);
        }
    }

    private static string Lines(string value) => value.Replace("\r\n", "\n").Replace("\n", NewLine);

    private static int LongestLine(string value) => value.Split(NewLine).Max(l => l.Length);
}
