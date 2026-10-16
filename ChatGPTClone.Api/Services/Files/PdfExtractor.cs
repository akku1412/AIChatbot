using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace ChatGPTClone.Api.Services.Files;

/// <summary>PDF text per page via PdfPig (Apache-2.0). Image-only (scanned) PDFs are detected, not OCR'd.</summary>
public sealed class PdfExtractor : IDocumentExtractor
{
    public bool CanHandle(string extension) => extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public ExtractionResult Extract(Stream content, string extension, ExtractionLimits limits)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(content);
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
            var budget = new TextBudget(limits.MaxCharacters);
            var sections = new List<ExtractedSection>();
            var pageCount = document.NumberOfPages;
            var meaningfulCharacters = 0;

            try
            {
                for (var number = 1; number <= pageCount && !budget.Exhausted; number++)
                {
                    var page = document.GetPage(number);
                    var text = Normalize(ContentOrderTextExtractor.GetText(page));
                    meaningfulCharacters += text.Count(c => !char.IsWhiteSpace(c));
                    if (text.Length > 0)
                    {
                        sections.Add(new ExtractedSection($"Page {number}", budget.Take(text)));
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                throw new DocumentExtractionException("This PDF could not be read completely; it may be damaged.", "corrupted", ex);
            }

            // Scanned PDFs are pages of images: no text layer at all, or only stray characters (page numbers,
            // scanner stamps) across several pages. A short one-page text PDF is still a real text PDF.
            var looksScanned = meaningfulCharacters == 0 || (pageCount > 1 && meaningfulCharacters < pageCount * 10);
            if (looksScanned)
            {
                throw new DocumentExtractionException(
                    "This PDF appears to be scanned (images only), so it has no text to read. " +
                    "OCR (text recognition) is not enabled. Upload a text-based PDF or a Word file instead.",
                    "ocr_required");
            }

            var warning = budget.Exhausted
                ? $"Only the first part of this PDF was read (limit {limits.MaxCharacters:N0} characters)."
                : null;
            return new ExtractionResult(sections, pageCount, warning);
        }
    }

    private static string Normalize(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd());
        return string.Join('\n', lines).Trim();
    }
}
