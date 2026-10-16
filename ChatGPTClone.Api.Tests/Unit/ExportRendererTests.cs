using System.Text;
using ChatGPTClone.Api.Services.Export;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using WordTable = DocumentFormat.OpenXml.Wordprocessing.Table;

namespace ChatGPTClone.Api.Tests.Unit;

/// <summary>Markdown → TXT/PDF/DOCX/XLSX. Every generated file is opened again with an independent reader.</summary>
public class ExportRendererTests
{
    private const string Reply = """
        # Quarterly Sales Report

        Revenue grew **12 percent** this quarter, driven by *strong* demand. Use `SUM()` to check.

        ## Regional breakdown

        | Region | Revenue | Growth | Last order |
        |--------|--------:|-------:|------------|
        | North  | 1,200.50 | 12% | 2026-03-01 |
        | South  | ₹850 | -3% | 2026-02-15 |
        | West   | =1+1 | 5.5% | n/a |

        ### Next steps

        1. Hire two engineers
        2. Open the Pune office
           - Sign the lease
        - Review pricing

        > Numbers are unaudited.

        ```sql
        SELECT Region, SUM(Revenue) FROM Sales GROUP BY Region;
        ```

        ---

        See [the dashboard](https://example.test/dash) <script>alert(1)</script>
        """;

    private static readonly ExportContext Context = new("Sales: Q1 / Review", new DateTime(2026, 10, 16, 6, 30, 0, DateTimeKind.Utc));

    private static IReadOnlyList<DocBlock> Blocks(string markdown = Reply) => MarkdownReader.Parse(markdown);

    // ---------- Markdown model ----------

    [Fact]
    public void Markdown_is_read_into_headings_paragraphs_lists_tables_quotes_and_code()
    {
        var blocks = Blocks();

        Assert.Equal([1, 2, 3], blocks.OfType<HeadingBlock>().Select(h => h.Level));
        var paragraph = blocks.OfType<ParagraphBlock>().First();
        Assert.Contains(paragraph.Runs, r => r.Text == "12 percent" && r.Bold);
        Assert.Contains(paragraph.Runs, r => r.Text == "strong" && r.Italic);
        Assert.Contains(paragraph.Runs, r => r.Text == "SUM()" && r.Code);

        var items = blocks.OfType<ListItemBlock>().ToList();
        Assert.Equal(["1.", "2.", "•", "•"], items.Select(i => i.Marker));
        Assert.Equal([0, 0, 1, 0], items.Select(i => i.Depth));

        var table = Assert.Single(blocks.OfType<TableBlock>());
        Assert.Equal(["Region", "Revenue", "Growth", "Last order"], table.Header);
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal("₹850", table.Rows[1][1]);

        Assert.Single(blocks.OfType<QuoteBlock>());
        Assert.StartsWith("SELECT Region", Assert.Single(blocks.OfType<CodeBlock>()).Text);
        Assert.Single(blocks.OfType<RuleBlock>());
        // Links keep their address; raw HTML stays literal text (never executed or rendered).
        var last = RunText.Plain(blocks.OfType<ParagraphBlock>().Last().Runs);
        Assert.Contains("the dashboard (https://example.test/dash)", last);
        Assert.Contains("<script>", last);
    }

    // ---------- TXT ----------

    [Fact]
    public void Plain_text_has_no_markdown_symbols_and_keeps_structure()
    {
        var text = PlainTextRenderer.Render(Blocks());

        Assert.StartsWith("Quarterly Sales Report\r\n======================\r\n", text);
        Assert.Contains("Revenue grew 12 percent this quarter, driven by strong demand.", text);
        Assert.DoesNotContain("**", text);
        Assert.DoesNotContain("## ", text);
        Assert.Contains("1. Hire two engineers\r\n2. Open the Pune office\r\n   • Sign the lease\r\n• Review pricing", text);
        Assert.Contains("Region | Revenue  | Growth | Last order", text);
        Assert.Contains("North  | 1,200.50 | 12%    | 2026-03-01", text);
        Assert.Contains("> Numbers are unaudited.", text);
        Assert.Contains("    SELECT Region", text);
        Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
    }

    // ---------- PDF ----------

    [Fact]
    public void Pdf_is_a_valid_document_with_the_reply_text_and_title()
    {
        var bytes = PdfRenderer.Render(Blocks(), Context);

        Assert.Equal("%PDF-"u8.ToArray(), bytes[..5]);
        using var pdf = PdfDocument.Open(bytes);
        var text = string.Join("\n", pdf.GetPages().Select(p => p.Text));
        Assert.Contains("Quarterly Sales Report", text);
        Assert.Contains("Regional breakdown", text);
        Assert.Contains("North", text);
        Assert.Contains("1,200.50", text);
        Assert.Contains("Hire two engineers", text);
        Assert.Contains("SELECT Region", text);
        Assert.Equal("Sales: Q1 / Review", pdf.Information.Title);
    }

    [Fact]
    public void Long_pdf_flows_onto_more_pages_with_page_numbers()
    {
        var rows = string.Join("\n", Enumerable.Range(1, 150).Select(i => $"| Item {i} | {i * 10} |"));
        var bytes = PdfRenderer.Render(Blocks($"# Big table\n\n| Name | Value |\n|---|---|\n{rows}"), Context);

        using var pdf = PdfDocument.Open(bytes);
        Assert.True(pdf.NumberOfPages > 1);
        Assert.Contains($"1 / {pdf.NumberOfPages}", pdf.GetPage(1).Text.Replace("1/", "1 / "));
        Assert.Contains("Item 150", string.Concat(pdf.GetPages().Select(p => p.Text)));
    }

    // ---------- DOCX ----------

    [Fact]
    public void Docx_is_schema_valid_with_real_headings_lists_and_table()
    {
        var bytes = DocxRenderer.Render(Blocks(), Context);

        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var errors = new OpenXmlValidator().Validate(document).Select(e => $"{e.Path?.XPath}: {e.Description}").ToList();
        Assert.Empty(errors);

        var body = document.MainDocumentPart!.Document!.Body!;
        string StyleOf(Paragraph p) => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
        var headings = body.Elements<Paragraph>().Where(p => StyleOf(p).StartsWith("Heading")).Select(p => (StyleOf(p), p.InnerText)).ToList();
        Assert.Equal([("Heading1", "Quarterly Sales Report"), ("Heading2", "Regional breakdown"), ("Heading3", "Next steps")], headings);

        Assert.Contains(body.Descendants<Run>(), r => r.InnerText == "12 percent" && r.RunProperties?.Bold is not null);
        Assert.Contains(body.Elements<Paragraph>(), p => p.InnerText == "1.Hire two engineers"); // marker + tab + text
        Assert.Contains(body.Elements<Paragraph>(), p => StyleOf(p) == "Code" && p.InnerText.StartsWith("SELECT Region"));

        var table = Assert.Single(body.Elements<WordTable>());
        var rows = table.Elements<TableRow>().ToList();
        Assert.Equal(4, rows.Count);
        Assert.NotNull(rows[0].TableRowProperties?.GetFirstChild<TableHeader>());
        Assert.Equal(["Region", "Revenue", "Growth", "Last order"], rows[0].Elements<TableCell>().Select(c => c.InnerText));
        Assert.Equal("Sales: Q1 / Review", document.PackageProperties.Title);
    }

    // ---------- XLSX ----------

    [Fact]
    public void Xlsx_has_named_sheet_headers_and_typed_values()
    {
        var bytes = XlsxRenderer.Render(Blocks(), Context);

        using (var package = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(new MemoryStream(bytes), false))
        {
            Assert.Empty(new OpenXmlValidator().Validate(package).Select(e => e.Description));
        }

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = Assert.Single(workbook.Worksheets);
        Assert.Equal("Regional breakdown", sheet.Name);
        Assert.Equal(["Region", "Revenue", "Growth", "Last order"], sheet.Row(1).Cells(1, 4).Select(c => c.GetString()));
        Assert.True(sheet.Cell("A1").Style.Font.Bold);
        Assert.Equal(1, sheet.SheetView.SplitRow); // header frozen

        Assert.Equal(XLDataType.Number, sheet.Cell("B2").DataType);
        Assert.Equal(1200.5, sheet.Cell("B2").GetDouble());
        Assert.Equal(850, sheet.Cell("B3").GetDouble());
        Assert.Equal(0.12, sheet.Cell("C2").GetDouble(), 6);
        Assert.Equal(-0.03, sheet.Cell("C3").GetDouble(), 6);
        Assert.Equal(XLDataType.DateTime, sheet.Cell("D2").DataType);
        Assert.Equal(new DateTime(2026, 3, 1), sheet.Cell("D2").GetDateTime());

        // Formula-looking text stays text: nothing in a reply can make Excel run a formula.
        Assert.False(sheet.Cell("B4").HasFormula);
        Assert.Equal("=1+1", sheet.Cell("B4").GetString());
        Assert.Equal("n/a", sheet.Cell("D4").GetString());
    }

    [Fact]
    public void Xlsx_from_a_label_value_list_when_there_is_no_table()
    {
        var bytes = XlsxRenderer.Render(Blocks("""
            ## Wedding details
            - **Venue:** Shubh Banquet Hall, Jaipur
            - **Guests:** 320
            - **Per plate:** ₹850
            """), Context);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);
        Assert.Equal("Wedding details", sheet.Name);
        Assert.Equal(["Item", "Value"], sheet.Row(1).Cells(1, 2).Select(c => c.GetString()));
        Assert.Equal("Venue", sheet.Cell("A2").GetString());
        Assert.Equal("Shubh Banquet Hall, Jaipur", sheet.Cell("B2").GetString());
        Assert.Equal(320, sheet.Cell("B3").GetDouble());
    }

    [Fact]
    public void Xlsx_without_tabular_data_is_refused()
    {
        var ex = Assert.Throws<ExportNotPossibleException>(() => XlsxRenderer.Render(Blocks("Just a sentence.\n\n- one\n- two"), Context));
        Assert.Contains("no table", ex.Message);
    }

    [Fact]
    public void Several_tables_get_unique_valid_sheet_names()
    {
        var md = "## Sales: 2026/Q1 [draft]?\n\n| a |\n|---|\n| 1 |\n\n| b |\n|---|\n| 2 |\n\n## " + new string('x', 40) + "\n\n| c |\n|---|\n| 3 |";
        using var workbook = new XLWorkbook(new MemoryStream(XlsxRenderer.Render(Blocks(md), Context)));
        Assert.Equal(["Sales 2026 Q1 draft", "Table 2", new string('x', 31)], workbook.Worksheets.Select(s => s.Name));

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Data" };
        Assert.Equal("data (2)", XlsxRenderer.UniqueSheetName("data", used)); // clashes ignore case
    }

    // ---------- Formats ----------

    [Theory]
    [InlineData("pdf", ExportFormat.Pdf)]
    [InlineData("DOCX", ExportFormat.Docx)]
    [InlineData(".xlsx", ExportFormat.Xlsx)]
    [InlineData("txt", ExportFormat.Txt)]
    public void Formats_parse(string value, ExportFormat expected)
    {
        Assert.True(ExportFormats.TryParse(value, out var format));
        Assert.Equal(expected, format);
    }

    [Theory]
    [InlineData("exe")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("4")]
    public void Unknown_formats_are_rejected(string? value) => Assert.False(ExportFormats.TryParse(value, out _));

    [Fact]
    public void Hindi_text_survives_in_docx_and_txt()
    {
        var blocks = Blocks("## सारांश\n\nमेहंदी 20 फरवरी को है।");
        Assert.Contains("मेहंदी 20 फरवरी को है।", PlainTextRenderer.Render(blocks));
        using var document = WordprocessingDocument.Open(new MemoryStream(DocxRenderer.Render(blocks, Context)), false);
        Assert.Contains("मेहंदी 20 फरवरी को है।", document.MainDocumentPart!.Document!.Body!.InnerText);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(PdfRenderer.Render(blocks, Context)[..5]));
    }
}
