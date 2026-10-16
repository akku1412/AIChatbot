using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ChatGPTClone.Api.Services.Files;

/// <summary>
/// Word (.docx) via the Open XML SDK (MIT): headings become Markdown headings, tables become pipe tables,
/// and each heading starts a new section so answers can cite "Section: ...". Macros are never loaded.
/// </summary>
public sealed class DocxExtractor : IDocumentExtractor
{
    public bool CanHandle(string extension) => extension.Equals(".docx", StringComparison.OrdinalIgnoreCase);

    public ExtractionResult Extract(Stream content, string extension, ExtractionLimits limits)
    {
        WordprocessingDocument document;
        try
        {
            document = WordprocessingDocument.Open(content, isEditable: false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new DocumentExtractionException("This Word document is damaged or not a valid .docx file.", "corrupted", ex);
        }

        using (document)
        {
            var body = document.MainDocumentPart?.Document?.Body
                ?? throw new DocumentExtractionException("This Word document has no content.", "empty");

            var budget = new TextBudget(limits.MaxCharacters);
            var sections = new List<ExtractedSection>();
            var current = new StringBuilder();
            var location = "Start of document";

            void Flush()
            {
                var text = current.ToString().Trim();
                if (text.Length > 0)
                {
                    sections.Add(new ExtractedSection(location, budget.Take(text)));
                }
                current.Clear();
            }

            foreach (var element in body.ChildElements)
            {
                if (budget.Exhausted)
                {
                    break;
                }

                switch (element)
                {
                    case Paragraph paragraph:
                        var text = paragraph.InnerText.Trim();
                        if (text.Length == 0)
                        {
                            continue;
                        }

                        var level = HeadingLevel(paragraph);
                        if (level > 0)
                        {
                            Flush();
                            location = $"Section: {Truncate(text, 120)}";
                            current.Append(new string('#', Math.Min(level, 6))).Append(' ').AppendLine(text);
                        }
                        else
                        {
                            current.AppendLine(IsListItem(paragraph) ? $"- {text}" : text);
                        }
                        break;

                    case Table table:
                        AppendTable(table, current);
                        break;
                }
            }
            Flush();

            if (sections.Count == 0)
            {
                throw new DocumentExtractionException("This Word document has no readable text.", "empty");
            }

            var warning = budget.Exhausted
                ? $"Only the first part of this document was read (limit {limits.MaxCharacters:N0} characters)."
                : null;
            return new ExtractionResult(sections, null, warning);
        }
    }

    private static int HeadingLevel(Paragraph paragraph)
    {
        var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? string.Empty;
        if (style.Equals("Title", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }
        // Built-in heading styles are "Heading1".."Heading9" (localized Word versions keep these ids).
        return style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
               && int.TryParse(style.AsSpan("Heading".Length), out var level)
            ? level
            : 0;
    }

    private static bool IsListItem(Paragraph paragraph) => paragraph.ParagraphProperties?.NumberingProperties is not null;

    private static void AppendTable(Table table, StringBuilder output)
    {
        var rows = table.Elements<TableRow>().ToList();
        if (rows.Count == 0)
        {
            return;
        }

        output.AppendLine();
        for (var i = 0; i < rows.Count; i++)
        {
            var cells = rows[i].Elements<TableCell>()
                .Select(c => c.InnerText.Replace('|', '/').Trim())
                .ToList();
            output.Append("| ").Append(string.Join(" | ", cells)).AppendLine(" |");
            if (i == 0)
            {
                output.Append('|').Append(string.Concat(cells.Select(_ => " --- |"))).AppendLine();
            }
        }
        output.AppendLine();
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
