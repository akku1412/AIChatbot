using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace ChatGPTClone.Api.Tests;

/// <summary>Builds real PDF / DOCX / XLSX files in memory for the tests.</summary>
public static class TestDocuments
{
    /// <summary>One PDF page per entry; each entry's lines are written top to bottom.</summary>
    public static byte[] Pdf(params string[][] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var lines in pages)
        {
            var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
            var y = 780.0;
            foreach (var line in lines)
            {
                page.AddText(line, 11, new PdfPoint(40, y), font);
                y -= 16;
            }
        }
        return builder.Build();
    }

    /// <summary>A PDF whose pages contain only drawings (like a scanned document: no text layer).</summary>
    public static byte[] ImageOnlyPdf(int pages = 2)
    {
        var builder = new PdfDocumentBuilder();
        for (var i = 0; i < pages; i++)
        {
            var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
            page.DrawRectangle(new PdfPoint(50, 50), 400, 600, 3);
        }
        return builder.Build();
    }

    public static byte[] Docx(string heading, IEnumerable<string> paragraphs, string[][]? table = null)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            var body = new Body();
            body.Append(new Paragraph(
                new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
                new Run(new Text(heading))));
            foreach (var text in paragraphs)
            {
                body.Append(new Paragraph(new Run(new Text(text))));
            }
            if (table is not null)
            {
                var t = new Table();
                foreach (var row in table)
                {
                    t.Append(new TableRow(row.Select(cell => new TableCell(new Paragraph(new Run(new Text(cell)))))));
                }
                body.Append(t);
            }
            main.Document = new Document(body);
        }
        return stream.ToArray();
    }

    public static byte[] EmptyDocx() => Docx("", []).Length > 0 ? DocxWithoutText() : [];

    private static byte[] DocxWithoutText()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            document.AddMainDocumentPart().Document = new Document(new Body(new Paragraph()));
        }
        return stream.ToArray();
    }

    /// <summary>Sheet "Sales" with Region / Month / Revenue / Units; one blank revenue cell.</summary>
    public static byte[] SalesXlsx()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sales");
        sheet.Cell("A1").Value = "Region";
        sheet.Cell("B1").Value = "Month";
        sheet.Cell("C1").Value = "Revenue";
        sheet.Cell("D1").Value = "Units";
        object?[][] rows =
        [
            ["North", new DateTime(2026, 1, 1), 1200.5, 10],
            ["South", new DateTime(2026, 2, 1), 800, 7],
            ["East", new DateTime(2026, 3, 1), null, 4],
            ["West", new DateTime(2026, 4, 1), 1999.5, 15],
        ];
        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                var cell = sheet.Cell(r + 2, c + 1);
                switch (rows[r][c])
                {
                    case string s: cell.Value = s; break;
                    case DateTime d: cell.Value = d; break;
                    case double d: cell.Value = d; break;
                    case int i: cell.Value = i; break;
                }
            }
        }
        workbook.AddWorksheet("Empty");
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Starts like a ZIP (so it passes the first byte check) but is not a valid archive.</summary>
    public static byte[] CorruptZip() => [0x50, 0x4B, 0x03, 0x04, .. Enumerable.Repeat((byte)0x41, 200)];

    /// <summary>A Windows executable header, to be uploaded under a .pdf name.</summary>
    public static byte[] FakeExe() => [0x4D, 0x5A, 0x90, 0x00, .. Enumerable.Repeat((byte)0x00, 200)];
}
