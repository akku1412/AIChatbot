using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SkiaSharp;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
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

    /// <summary>A real "scan": every page is a picture of text (no text layer), so only OCR can read it.</summary>
    public static byte[] ScannedPdf(params string[][] pages)
    {
        var builder = new PdfDocumentBuilder();
        foreach (var lines in pages)
        {
            var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
            page.AddPng(TextPng(lines), new PdfRectangle(0, 0, 595, 842));
        }
        return builder.Build();
    }

    /// <summary>Black text on white, large enough for OCR (like a screenshot or photographed page).</summary>
    public static byte[] TextPng(IReadOnlyList<string> lines, int width = 1240, int height = 1754)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var font = new SKFont(SKTypeface.FromFamilyName("Arial"), 44);
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            var y = 140f;
            foreach (var line in lines)
            {
                canvas.DrawText(line, 80, y, font, paint);
                y += 80;
            }
        }
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>A JPEG carrying EXIF metadata (with a recognisable marker, like a phone photo's GPS position).</summary>
    public static byte[] JpegWithExif(string marker, int width = 400, int height = 300)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.SkyBlue);
        }
        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
        var jpeg = data.ToArray();

        // APP1 "Exif" segment with a minimal little-endian TIFF header followed by the marker text.
        var payload = "Exif\0\0"u8.ToArray().Concat(new byte[] { 0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00 })
            .Concat(System.Text.Encoding.ASCII.GetBytes(marker)).ToArray();
        var length = payload.Length + 2;
        byte[] app1 = [0xFF, 0xE1, (byte)(length >> 8), (byte)(length & 0xFF), .. payload];
        return [.. jpeg[..2], .. app1, .. jpeg[2..]]; // right after the SOI marker
    }

    /// <summary>A PNG of random pixels (incompressible, so its size is predictable).</summary>
    public static byte[] NoisePng(int width, int height)
    {
        var random = new Random(42);
        using var bitmap = new SKBitmap(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
            }
        }
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>A .pptx: one slide per entry (title + bullet lines) with optional speaker notes.</summary>
    public static byte[] Pptx(params (string Title, string[] Lines, string? Notes)[] slides)
    {
        using var stream = new MemoryStream();
        using (var document = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var presentationPart = document.AddPresentationPart();
            var slideIds = new P.SlideIdList();
            uint id = 256;
            foreach (var (title, lines, notes) in slides)
            {
                var slidePart = presentationPart.AddNewPart<SlidePart>();
                slidePart.Slide = new P.Slide(new P.CommonSlideData(new P.ShapeTree(
                    TextShape([title]),
                    TextShape(lines))));
                if (notes is not null)
                {
                    var notesPart = slidePart.AddNewPart<NotesSlidePart>();
                    notesPart.NotesSlide = new P.NotesSlide(new P.CommonSlideData(new P.ShapeTree(TextShape([notes]))));
                }
                slideIds.Append(new P.SlideId { Id = id++, RelationshipId = presentationPart.GetIdOfPart(slidePart) });
            }
            presentationPart.Presentation = new P.Presentation(slideIds, new P.SlideSize { Cx = 9144000, Cy = 6858000 },
                new P.NotesSize { Cx = 6858000, Cy = 9144000 });
        }
        return stream.ToArray();

        static P.Shape TextShape(IEnumerable<string> lines) =>
            new(new P.TextBody(new OpenXmlElement[] { new A.BodyProperties() }
                .Concat(lines.Select(l => new A.Paragraph(new A.Run(new A.Text(l)))))));
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
