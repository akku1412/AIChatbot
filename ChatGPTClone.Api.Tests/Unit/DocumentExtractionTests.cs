using ChatGPTClone.Api.Models;
using ChatGPTClone.Api.Services.Files;

namespace ChatGPTClone.Api.Tests.Unit;

public class DocumentExtractionTests
{
    private static readonly ExtractionLimits Limits = new(1_000_000, 50_000);

    private static ExtractionResult Run(IDocumentExtractor extractor, byte[] bytes, string extension) =>
        extractor.Extract(new MemoryStream(bytes), extension, Limits);

    // ---------- PDF ----------

    [Fact]
    public void Pdf_text_is_extracted_per_page_with_page_numbers()
    {
        var pdf = TestDocuments.Pdf(["Quarterly report", "Revenue grew 12 percent."], ["Action items", "Hire two engineers by June."]);

        var result = Run(new PdfExtractor(), pdf, ".pdf");

        Assert.Equal(2, result.PageCount);
        Assert.Equal(["Page 1", "Page 2"], result.Sections.Select(s => s.Location));
        Assert.Contains("Revenue grew 12 percent", result.Sections[0].Text);
        Assert.Contains("Hire two engineers", result.Sections[1].Text);
    }

    [Fact]
    public void Scanned_pdf_without_text_asks_for_ocr()
    {
        var ex = Assert.Throws<DocumentExtractionException>(() => Run(new PdfExtractor(), TestDocuments.ImageOnlyPdf(), ".pdf"));
        Assert.Equal("ocr_required", ex.Reason);
        Assert.Contains("OCR", ex.Message);
    }

    [Fact]
    public void Damaged_pdf_is_reported_as_corrupted()
    {
        var bytes = "%PDF-1.7\nthis is not really a pdf"u8.ToArray();
        var ex = Assert.Throws<DocumentExtractionException>(() => Run(new PdfExtractor(), bytes, ".pdf"));
        Assert.Equal("corrupted", ex.Reason);
    }

    [Fact]
    public void Extraction_stops_at_the_character_limit_with_a_warning()
    {
        var pdf = TestDocuments.Pdf(Enumerable.Range(1, 5).Select(i => new[] { new string('x', 80) + $" page {i}" }).ToArray());

        var result = new PdfExtractor().Extract(new MemoryStream(pdf), ".pdf", new ExtractionLimits(150, 100));

        Assert.True(result.Sections.Sum(s => s.Text.Length) <= 150);
        Assert.NotNull(result.Warning);
    }

    // ---------- Word ----------

    [Fact]
    public void Docx_keeps_headings_paragraphs_and_tables()
    {
        var docx = TestDocuments.Docx("Project Plan", ["Kickoff is on 2026-11-02.", "Owner: Priya."],
            [["Task", "Due"], ["Design", "Nov 10"], ["Build", "Dec 1"]]);

        var result = Run(new DocxExtractor(), docx, ".docx");

        var section = Assert.Single(result.Sections);
        Assert.Equal("Section: Project Plan", section.Location);
        Assert.Contains("# Project Plan", section.Text);
        Assert.Contains("Kickoff is on 2026-11-02.", section.Text);
        Assert.Contains("| Task | Due |", section.Text);
        Assert.Contains("| Build | Dec 1 |", section.Text);
    }

    [Fact]
    public void Empty_docx_is_rejected()
    {
        var ex = Assert.Throws<DocumentExtractionException>(() => Run(new DocxExtractor(), TestDocuments.EmptyDocx(), ".docx"));
        Assert.Equal("empty", ex.Reason);
    }

    [Fact]
    public void Corrupted_docx_is_rejected()
    {
        var ex = Assert.Throws<DocumentExtractionException>(() => Run(new DocxExtractor(), TestDocuments.CorruptZip(), ".docx"));
        Assert.Equal("corrupted", ex.Reason);
    }

    // ---------- Excel ----------

    [Fact]
    public void Xlsx_rows_keep_sheet_and_row_references_and_typed_values()
    {
        var result = Run(new SpreadsheetExtractor(), TestDocuments.SalesXlsx(), ".xlsx");

        Assert.Equal(2, result.PageCount);
        var rows = Assert.Single(result.Sections, s => s.Kind == DocumentChunkKind.Text);
        Assert.Equal("Sheet 'Sales' rows 2-5", rows.Location);
        Assert.Contains("R2: Region=North; Month=2026-01-01; Revenue=1200.5; Units=10", rows.Text);
        Assert.Contains("R4: Region=East; Month=2026-03-01; Units=4", rows.Text); // blank cell omitted
    }

    [Fact]
    public void Xlsx_summary_has_exact_totals_averages_blanks_and_date_ranges()
    {
        var result = Run(new SpreadsheetExtractor(), TestDocuments.SalesXlsx(), ".xlsx");

        var summary = result.Sections.Single(s => s.Kind == DocumentChunkKind.Summary && s.Location.Contains("Sales")).Text;
        Assert.Contains("4 data rows", summary);
        Assert.Contains("Column C 'Revenue': 3 values, 1 blank; numeric 3: sum=4000, average=1333.3333, min=800, max=1999.5", summary);
        Assert.Contains("Column D 'Units': 4 values, 0 blank; numeric 4: sum=36, average=9, min=4, max=15", summary);
        Assert.Contains("earliest=2026-01-01, latest=2026-04-01", summary);
        Assert.Contains(result.Sections, s => s.Text.Contains("Sheet 'Empty' is empty."));
    }

    [Fact]
    public void Corrupted_xlsx_is_rejected()
    {
        var ex = Assert.Throws<DocumentExtractionException>(() => Run(new SpreadsheetExtractor(), TestDocuments.CorruptZip(), ".xlsx"));
        Assert.Equal("corrupted", ex.Reason);
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(701, "ZZ")]
    public void Column_letters_follow_excel(int index, string expected) =>
        Assert.Equal(expected, SpreadsheetExtractor.ColumnLetter(index));

    // ---------- Signatures, names, chunking ----------

    [Fact]
    public void Signature_check_rejects_renamed_and_macro_files()
    {
        Assert.Null(FileSignature.Validate(new MemoryStream(TestDocuments.Pdf(["ok"])), ".pdf"));
        Assert.Null(FileSignature.Validate(new MemoryStream(TestDocuments.SalesXlsx()), ".xlsx"));
        Assert.NotNull(FileSignature.Validate(new MemoryStream(TestDocuments.FakeExe()), ".pdf"));
        Assert.NotNull(FileSignature.Validate(new MemoryStream(TestDocuments.SalesXlsx()), ".docx")); // xlsx renamed to .docx
        Assert.NotNull(FileSignature.Validate(new MemoryStream(TestDocuments.CorruptZip()), ".xlsx"));
    }

    [Theory]
    [InlineData("../../etc/passwd.pdf", "passwd.pdf")]
    [InlineData("C:\\Users\\x\\report.docx", "report.docx")]
    [InlineData("bad<name>|.xlsx", "badname.xlsx")]
    [InlineData("", "file")]
    public void File_names_are_sanitized(string input, string expected) =>
        Assert.Equal(expected, FileService.SanitizeFileName(input));

    [Fact]
    public void Chunker_splits_long_sections_and_keeps_locations()
    {
        var text = string.Join("\n\n", Enumerable.Range(1, 30).Select(i => $"Paragraph {i} " + new string('w', 90)));
        var chunks = DocumentChunker.Chunk(Guid.NewGuid(), [new ExtractedSection("Page 7", text)], 500);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Content.Length <= 500));
        Assert.All(chunks, c => Assert.StartsWith("Page 7 (part ", c.Location));
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.ChunkIndex));
        Assert.Equal(text.Replace("\n", "").Replace(" ", ""), string.Concat(chunks.Select(c => c.Content)).Replace("\n", "").Replace(" ", ""));
    }

    [Fact]
    public void Retrieval_terms_drop_stop_words()
    {
        var terms = Services.Files.DocumentContextBuilder.Terms("What is the total revenue in this spreadsheet for 2026?");
        Assert.Equal(["total", "revenue", "2026"], terms);
    }
}
