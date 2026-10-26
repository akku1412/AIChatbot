using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ChatGPTClone.Api.Services.Export;

/// <summary>
/// Editable Word document via the Open XML SDK (MIT). Uses Word's built-in style ids (Heading1..3, Quote) so headings
/// appear in the navigation pane and can be restyled; tables are real Word tables with a repeating header row.
/// </summary>
public static class DocxRenderer
{
    private const int TextWidthTwips = 9026; // A4 width minus 2.5 cm margins

    public static byte[] Render(IReadOnlyList<DocBlock> blocks, ExportContext context)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            document.PackageProperties.Title = context.Title;
            document.PackageProperties.Creator = "AIChat";
            document.PackageProperties.Created = DateTime.SpecifyKind(context.CreatedAtUtc, DateTimeKind.Utc);

            var main = document.AddMainDocumentPart();
            main.AddNewPart<StyleDefinitionsPart>().Styles = BuildStyles();

            var body = new Body();
            body.Append(new Paragraph(
                new ParagraphProperties(new ParagraphStyleId { Val = "Subtitle" }),
                TextRunElement($"{context.Title} · AIChat · {context.CreatedAtText}", new TextRun(""))));

            foreach (var block in blocks)
            {
                body.Append(Convert(block));
            }

            body.Append(new SectionProperties(
                new PageSize { Width = 11906U, Height = 16838U },
                new PageMargin { Top = 1418, Right = 1440U, Bottom = 1418, Left = 1440U, Header = 708U, Footer = 708U, Gutter = 0U }));
            main.Document = new Document(body);
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static IEnumerable<OpenXmlElement> Convert(DocBlock block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                yield return StyledParagraph($"Heading{Math.Min(heading.Level, 3)}", heading.Runs);
                break;

            case ParagraphBlock paragraph:
                yield return StyledParagraph(null, paragraph.Runs);
                break;

            case ListItemBlock item:
                var left = 360 * (item.Depth + 1);
                var properties = new ParagraphProperties(
                    new SpacingBetweenLines { After = "60" },
                    new Indentation { Left = left.ToString(), Hanging = "360" });
                var paragraphElement = new Paragraph(properties);
                if (item.Marker.Length > 0)
                {
                    // The hanging indent acts as a tab stop, so marker + tab lines the text up like a real list.
                    paragraphElement.Append(new Run(new Text(item.Marker)), new Run(new TabChar()));
                }
                paragraphElement.Append(Runs(item.Runs));
                yield return paragraphElement;
                break;

            case CodeBlock code:
                yield return StyledParagraph("Code", [new TextRun(code.Text)]);
                break;

            case QuoteBlock quote:
                yield return StyledParagraph("Quote", quote.Runs);
                break;

            case TableBlock table:
                yield return BuildTable(table);
                yield return new Paragraph(); // Word needs a paragraph between consecutive tables
                break;

            case RuleBlock:
                yield return new Paragraph(new ParagraphProperties(new ParagraphBorders(
                    new BottomBorder { Val = BorderValues.Single, Size = 6U, Color = "D1D5DB", Space = 1U })));
                break;
        }
    }

    private static Paragraph StyledParagraph(string? styleId, IReadOnlyList<TextRun> runs)
    {
        var paragraph = new Paragraph();
        if (styleId is not null)
        {
            paragraph.Append(new ParagraphProperties(new ParagraphStyleId { Val = styleId }));
        }
        paragraph.Append(Runs(runs));
        return paragraph;
    }

    private static IEnumerable<Run> Runs(IReadOnlyList<TextRun> runs) => runs.Select(run => TextRunElement(run.Text, run));

    /// <summary>One Word run; "\n" becomes a line break inside the paragraph.</summary>
    private static Run TextRunElement(string text, TextRun format)
    {
        var run = new Run();
        var props = new RunProperties();
        if (format.Code)
        {
            props.Append(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas", ComplexScript = "Consolas" });
        }
        if (format.Bold)
        {
            props.Append(new Bold(), new BoldComplexScript());
        }
        if (format.Italic)
        {
            props.Append(new Italic(), new ItalicComplexScript());
        }
        if (format.Code)
        {
            props.Append(new FontSize { Val = "20" }, new Shading { Val = ShadingPatternValues.Clear, Fill = "F3F4F6" });
        }
        if (props.HasChildren)
        {
            run.Append(props);
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                run.Append(new Break());
            }
            run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
        }
        return run;
    }

    private static Table BuildTable(TableBlock table)
    {
        var columnWidth = TextWidthTwips / Math.Max(1, table.Header.Count);
        static T Edge<T>() where T : BorderType, new() => new() { Val = BorderValues.Single, Size = 4U, Color = "D1D5DB" };

        var grid = new Table(
            new TableProperties(
                new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                new TableBorders(Edge<TopBorder>(), Edge<LeftBorder>(), Edge<BottomBorder>(), Edge<RightBorder>(),
                    Edge<InsideHorizontalBorder>(), Edge<InsideVerticalBorder>()),
                new TableCellMarginDefault(
                    new TopMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                    new TableCellLeftMargin { Width = 100, Type = TableWidthValues.Dxa },
                    new BottomMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                    new TableCellRightMargin { Width = 100, Type = TableWidthValues.Dxa })),
            new TableGrid(table.Header.Select(_ => new GridColumn { Width = columnWidth.ToString() })));

        var headerRow = new TableRow(new TableRowProperties(new TableHeader())); // repeats on every page
        headerRow.Append(table.Header.Select(text => Cell(text, header: true)));
        grid.Append(headerRow);
        foreach (var row in table.Rows)
        {
            grid.Append(new TableRow(row.Select(text => Cell(text, header: false))));
        }
        return grid;
    }

    private static TableCell Cell(string text, bool header)
    {
        var cell = new TableCell();
        if (header)
        {
            cell.Append(new TableCellProperties(new Shading { Val = ShadingPatternValues.Clear, Fill = "EEF2FF" }));
        }
        cell.Append(new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { After = "0" }),
            TextRunElement(text, new TextRun(text, Bold: header))));
        return cell;
    }

    private static Styles BuildStyles()
    {
        static Style ParagraphStyle(string id, string name, StyleParagraphProperties? paragraph, StyleRunProperties? run, bool isDefault = false)
        {
            var style = new Style { Type = StyleValues.Paragraph, StyleId = id, Default = isDefault ? true : null };
            style.Append(new StyleName { Val = name });
            if (!isDefault)
            {
                style.Append(new BasedOn { Val = "Normal" }, new NextParagraphStyle { Val = "Normal" });
            }
            style.Append(new PrimaryStyle());
            if (paragraph is not null)
            {
                style.Append(paragraph);
            }
            if (run is not null)
            {
                style.Append(run);
            }
            return style;
        }

        static Style Heading(int level, string halfPoints, string before) => ParagraphStyle(
            $"Heading{level}", $"heading {level}",
            new StyleParagraphProperties(new KeepNext(), new KeepLines(),
                new SpacingBetweenLines { Before = before, After = "80" }, new OutlineLevel { Val = level - 1 }),
            new StyleRunProperties(new Bold(), new BoldComplexScript(), new Color { Val = "1F2937" },
                new FontSize { Val = halfPoints }, new FontSizeComplexScript { Val = halfPoints }));

        return new Styles(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    // Complex-script text (Hindi and other Indic scripts) uses Nirmala UI, which ships with Windows and Office.
                    new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", EastAsia = "Calibri", ComplexScript = "Nirmala UI" },
                    new FontSize { Val = "22" }, new FontSizeComplexScript { Val = "22" },
                    new Languages { Val = "en-US" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                    new SpacingBetweenLines { After = "120", Line = "276", LineRule = LineSpacingRuleValues.Auto }))),
            ParagraphStyle("Normal", "Normal", null, null, isDefault: true),
            Heading(1, "36", "360"),
            Heading(2, "30", "280"),
            Heading(3, "25", "220"),
            ParagraphStyle("Subtitle", "Subtitle",
                new StyleParagraphProperties(new SpacingBetweenLines { After = "240" }),
                new StyleRunProperties(new Color { Val = "6B7280" }, new FontSize { Val = "18" }, new FontSizeComplexScript { Val = "18" })),
            ParagraphStyle("Quote", "Quote",
                new StyleParagraphProperties(
                    new ParagraphBorders(new LeftBorder { Val = BorderValues.Single, Size = 18U, Color = "A5B4FC", Space = 8U }),
                    new Indentation { Left = "300" }),
                new StyleRunProperties(new Italic(), new ItalicComplexScript(), new Color { Val = "4B5563" })),
            ParagraphStyle("Code", "Code",
                new StyleParagraphProperties(
                    new Shading { Val = ShadingPatternValues.Clear, Fill = "F3F4F6" },
                    new SpacingBetweenLines { After = "120", Line = "240", LineRule = LineSpacingRuleValues.Auto },
                    new Indentation { Left = "120", Right = "120" }),
                new StyleRunProperties(
                    new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas", ComplexScript = "Consolas" },
                    new FontSize { Val = "19" }, new FontSizeComplexScript { Val = "19" })));
    }
}
