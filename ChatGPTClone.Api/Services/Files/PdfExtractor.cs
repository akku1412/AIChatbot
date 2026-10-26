using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Models;
using Microsoft.Extensions.Options;
using PDFtoImage;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace ChatGPTClone.Api.Services.Files;

/// <summary>
/// PDF text per page via PdfPig (Apache-2.0). Pages without a text layer (scanned pages) are rendered with PDFium
/// (PDFtoImage, MIT) and read with OCR when it is available; those chunks are marked Source = OCR.
/// Without OCR, an image-only PDF is rejected with a clear message.
/// </summary>
public sealed class PdfExtractor(IOcrEngine ocr, IOptions<OcrOptions> ocrOptions) : IDocumentExtractor
{
    /// <summary>A page with fewer visible characters than this is treated as scanned (page numbers, stamps...).</summary>
    private const int MinCharactersForTextPage = 10;

    /// <summary>Without OCR (unit tests and when OCR is disabled).</summary>
    public PdfExtractor() : this(new NoOcrEngine(), Options.Create(new OcrOptions { Enabled = false }))
    {
    }

    public bool CanHandle(string extension) => extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public ExtractionResult Extract(Stream content, string extension, ExtractionLimits limits)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        var bytes = buffer.ToArray();

        PdfDocument document;
        try
        {
            document = PdfDocument.Open(bytes);
        }
        catch (PdfDocumentEncryptedException ex)
        {
            throw new DocumentExtractionException("This PDF is password-protected. Remove the password and upload it again.", "encrypted", ex);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new DocumentExtractionException("This PDF is damaged or not a valid PDF.", "corrupted", ex);
        }

        using (document)
        {
            var pageCount = document.NumberOfPages;
            var pageTexts = new Dictionary<int, string>();
            var scannedPages = new List<int>();
            try
            {
                for (var number = 1; number <= pageCount; number++)
                {
                    var text = Normalize(ContentOrderTextExtractor.GetText(document.GetPage(number)));
                    if (text.Count(c => !char.IsWhiteSpace(c)) >= MinCharactersForTextPage)
                    {
                        pageTexts[number] = text;
                    }
                    else
                    {
                        scannedPages.Add(number);
                        if (text.Length > 0)
                        {
                            pageTexts[number] = text; // keep stray text (page numbers) unless OCR replaces it
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                throw new DocumentExtractionException("This PDF could not be read completely; it may be damaged.", "corrupted", ex);
            }

            var warnings = new List<string>();
            var ocrPages = new HashSet<int>();
            if (scannedPages.Count > 0 && ocr.IsAvailable)
            {
                var settings = ocrOptions.Value;
                var toRead = scannedPages.Take(settings.MaxOcrPages).ToList();
                if (scannedPages.Count > toRead.Count)
                {
                    warnings.Add($"Only the first {toRead.Count} of {scannedPages.Count} scanned pages were read with OCR.");
                }
                for (var i = 0; i < toRead.Count; i++)
                {
                    limits.ReportProgress?.Invoke(i, toRead.Count);
                    var text = OcrPage(bytes, toRead[i], settings.PdfRenderDpi);
                    if (text.Length > 0)
                    {
                        pageTexts[toRead[i]] = text;
                        ocrPages.Add(toRead[i]);
                    }
                }
                limits.ReportProgress?.Invoke(toRead.Count, toRead.Count);
                if (ocrPages.Count > 0)
                {
                    warnings.Add($"{ocrPages.Count} scanned page(s) were read with OCR; recognised text may contain errors.");
                }
            }
            else if (scannedPages.Count > 0)
            {
                // Scanned PDFs are pages of images: no text layer at all, or only stray characters on several pages.
                var visible = pageTexts.Values.Sum(t => t.Count(c => !char.IsWhiteSpace(c)));
                if (visible == 0 || (pageCount > 1 && visible < pageCount * MinCharactersForTextPage))
                {
                    throw new DocumentExtractionException(
                        "This PDF appears to be scanned (images only), so it has no text to read, and OCR (text recognition) " +
                        "is not enabled. Upload a text-based PDF or a Word file instead.",
                        "ocr_required");
                }
            }

            var budget = new TextBudget(limits.MaxCharacters);
            var sections = new List<ExtractedSection>();
            foreach (var (number, text) in pageTexts.OrderBy(p => p.Key))
            {
                if (budget.Exhausted)
                {
                    break;
                }
                var source = ocrPages.Contains(number) ? ChunkSource.Ocr : ChunkSource.Text;
                sections.Add(new ExtractedSection($"Page {number}", budget.Take(text), Source: source));
            }

            if (sections.Sum(s => s.Text.Count(c => !char.IsWhiteSpace(c))) == 0)
            {
                throw new DocumentExtractionException(
                    "No text could be found in this PDF, not even with OCR. If it is a photo or drawing, upload it as an image instead.",
                    "no_text");
            }

            if (budget.Exhausted)
            {
                warnings.Add($"Only the first part of this PDF was read (limit {limits.MaxCharacters:N0} characters).");
            }
            return new ExtractionResult(sections, pageCount, warnings.Count > 0 ? string.Join(' ', warnings) : null);
        }
    }

    private string OcrPage(byte[] pdf, int pageNumber, int dpi)
    {
        try
        {
            using var bitmap = Conversion.ToImage(pdf, pageNumber - 1, null, new RenderOptions(Dpi: dpi, WithAnnotations: true));
            using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            return ocr.Recognize(png.ToArray());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // One unreadable page shouldn't fail the whole document.
            return string.Empty;
        }
    }

    private static string Normalize(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd());
        return string.Join('\n', lines).Trim();
    }
}
