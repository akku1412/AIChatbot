using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdBlock = Markdig.Syntax.Block;

namespace ChatGPTClone.Api.Services.Export;

/// <summary>A piece of inline text with simple formatting. "\n" inside <see cref="Text"/> is a hard line break.</summary>
public sealed record TextRun(string Text, bool Bold = false, bool Italic = false, bool Code = false);

/// <summary>Format-neutral blocks that every exporter (TXT, PDF, DOCX, XLSX) knows how to render.</summary>
public abstract record DocBlock;

public sealed record HeadingBlock(int Level, IReadOnlyList<TextRun> Runs) : DocBlock;

public sealed record ParagraphBlock(IReadOnlyList<TextRun> Runs) : DocBlock;

/// <summary>One list item. Marker is "•" or "3." (empty for a continuation paragraph inside an item).</summary>
public sealed record ListItemBlock(int Depth, string Marker, IReadOnlyList<TextRun> Runs) : DocBlock;

public sealed record CodeBlock(string Text) : DocBlock;

public sealed record QuoteBlock(IReadOnlyList<TextRun> Runs) : DocBlock;

/// <summary>A table as plain cell text; every row has exactly <c>Header.Count</c> cells.</summary>
public sealed record TableBlock(IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows) : DocBlock;

public sealed record RuleBlock : DocBlock;

public static class RunText
{
    public static string Plain(IEnumerable<TextRun> runs) => string.Concat(runs.Select(r => r.Text));
}

/// <summary>
/// Turns the AI's Markdown into <see cref="DocBlock"/>s with Markdig (BSD-2). Raw HTML is never interpreted:
/// it is kept as literal text, so nothing in a reply can inject markup or scripts into a generated file.
/// </summary>
public static class MarkdownReader
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .Build();

    public static IReadOnlyList<DocBlock> Parse(string markdown)
    {
        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);
        var blocks = new List<DocBlock>();
        foreach (var block in document)
        {
            AddBlock(block, blocks, listDepth: 0, markdown ?? string.Empty);
        }
        return blocks;
    }

    private static void AddBlock(MdBlock block, List<DocBlock> output, int listDepth, string source)
    {
        switch (block)
        {
            case Markdig.Syntax.HeadingBlock heading:
                output.Add(new HeadingBlock(Math.Clamp(heading.Level, 1, 6), Runs(heading.Inline)));
                break;

            case Markdig.Syntax.ParagraphBlock paragraph:
                var runs = Runs(paragraph.Inline);
                if (runs.Count > 0)
                {
                    output.Add(listDepth > 0 ? new ListItemBlock(listDepth - 1, "", runs) : new ParagraphBlock(runs));
                }
                break;

            case ListBlock list:
                AddList(list, output, listDepth, source);
                break;

            case Markdig.Syntax.CodeBlock code: // fenced and indented
                output.Add(new CodeBlock(CodeText(code)));
                break;

            case Markdig.Syntax.QuoteBlock quote:
                foreach (var child in quote)
                {
                    if (child is Markdig.Syntax.ParagraphBlock p)
                    {
                        output.Add(new QuoteBlock(Runs(p.Inline)));
                    }
                    else
                    {
                        AddBlock(child, output, listDepth, source);
                    }
                }
                break;

            case Table table:
                output.Add(ReadTable(table));
                break;

            case ThematicBreakBlock:
                output.Add(new RuleBlock());
                break;

            case HtmlBlock html:
                // Shown as literal text, never rendered as HTML.
                var raw = source.Substring(html.Span.Start, Math.Max(0, html.Span.Length)).Trim();
                if (raw.Length > 0)
                {
                    output.Add(new ParagraphBlock([new TextRun(raw)]));
                }
                break;

            case ContainerBlock container:
                foreach (var child in container)
                {
                    AddBlock(child, output, listDepth, source);
                }
                break;
        }
    }

    private static void AddList(ListBlock list, List<DocBlock> output, int depth, string source)
    {
        var number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
        foreach (var item in list.OfType<Markdig.Syntax.ListItemBlock>())
        {
            var marker = list.IsOrdered ? $"{number++}." : "•";
            var first = true;
            foreach (var child in item)
            {
                if (first && child is Markdig.Syntax.ParagraphBlock paragraph)
                {
                    output.Add(new ListItemBlock(depth, marker, Runs(paragraph.Inline)));
                }
                else
                {
                    if (first)
                    {
                        output.Add(new ListItemBlock(depth, marker, [])); // item that starts with a nested block
                    }
                    AddBlock(child, output, depth + 1, source);
                }
                first = false;
            }
        }
    }

    private static TableBlock ReadTable(Table table)
    {
        List<string> header = [];
        var body = new List<List<string>>();
        foreach (var row in table.OfType<TableRow>())
        {
            var cells = row.OfType<TableCell>().Select(CellText).ToList();
            if (row.IsHeader && header.Count == 0)
            {
                header = cells;
            }
            else
            {
                body.Add(cells);
            }
        }

        var width = Math.Max(header.Count, body.Count == 0 ? 0 : body.Max(r => r.Count));
        if (header.Count == 0)
        {
            header = Enumerable.Range(1, width).Select(i => $"Column {i}").ToList();
        }
        List<string> Pad(List<string> row) => row.Concat(Enumerable.Repeat("", Math.Max(0, width - row.Count))).Take(width).ToList();
        return new TableBlock(Pad(header), body.Select(r => (IReadOnlyList<string>)Pad(r)).ToList());
    }

    private static string CellText(TableCell cell) =>
        string.Join(" ", cell.OfType<Markdig.Syntax.ParagraphBlock>().Select(p => RunText.Plain(Runs(p.Inline)).Replace('\n', ' '))).Trim();

    private static string CodeText(Markdig.Syntax.CodeBlock code)
    {
        var lines = code.Lines;
        var text = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                text.Append('\n');
            }
            text.Append(lines.Lines[i].Slice.ToString());
        }
        return text.ToString().TrimEnd();
    }

    private static List<TextRun> Runs(ContainerInline? container)
    {
        var runs = new List<TextRun>();
        if (container is not null)
        {
            Collect(container, runs, bold: false, italic: false);
        }

        // Merge neighbours with the same formatting and trim the edges of the block.
        var merged = new List<TextRun>();
        foreach (var run in runs.Where(r => r.Text.Length > 0))
        {
            if (merged.Count > 0 && merged[^1] is var last && last.Bold == run.Bold && last.Italic == run.Italic && last.Code == run.Code)
            {
                merged[^1] = last with { Text = last.Text + run.Text };
            }
            else
            {
                merged.Add(run);
            }
        }
        if (merged.Count > 0)
        {
            merged[0] = merged[0] with { Text = merged[0].Text.TrimStart() };
            merged[^1] = merged[^1] with { Text = merged[^1].Text.TrimEnd() };
        }
        return merged.Where(r => r.Text.Length > 0).ToList();
    }

    private static void Collect(Inline inline, List<TextRun> runs, bool bold, bool italic)
    {
        switch (inline)
        {
            case LiteralInline literal:
                runs.Add(new TextRun(literal.Content.ToString(), bold, italic));
                break;
            case CodeInline code:
                runs.Add(new TextRun(code.Content, bold, italic, Code: true));
                break;
            case LineBreakInline lineBreak:
                runs.Add(new TextRun(lineBreak.IsHard ? "\n" : " ", bold, italic));
                break;
            case HtmlEntityInline entity:
                runs.Add(new TextRun(entity.Transcoded.ToString(), bold, italic));
                break;
            case AutolinkInline autolink:
                runs.Add(new TextRun(autolink.Url, bold, italic));
                break;
            case HtmlInline html:
                runs.Add(new TextRun(html.Tag.Equals("<br>", StringComparison.OrdinalIgnoreCase) || html.Tag.StartsWith("<br", StringComparison.OrdinalIgnoreCase)
                    ? "\n" : html.Tag, bold, italic));
                break;
            case EmphasisInline emphasis:
                var isBold = bold || (emphasis.DelimiterChar is '*' or '_' && emphasis.DelimiterCount >= 2);
                var isItalic = italic || (emphasis.DelimiterChar is '*' or '_' && emphasis.DelimiterCount is 1 or 3);
                foreach (var child in emphasis)
                {
                    Collect(child, runs, isBold, isItalic);
                }
                break;
            case LinkInline link:
                var start = runs.Count;
                foreach (var child in link)
                {
                    Collect(child, runs, bold, italic);
                }
                var label = string.Concat(runs.Skip(start).Select(r => r.Text));
                if (!link.IsImage && !string.IsNullOrWhiteSpace(link.Url) && !label.Contains(link.Url, StringComparison.Ordinal))
                {
                    runs.Add(new TextRun($" ({link.Url})", bold, italic)); // keep the address readable on paper
                }
                break;
            case ContainerInline container:
                foreach (var child in container)
                {
                    Collect(child, runs, bold, italic);
                }
                break;
        }
    }
}
