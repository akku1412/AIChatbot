using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.Services.Files;

/// <summary>
/// Images (PNG/JPEG/WebP): the picture itself goes to the vision model; OCR text found in it (screenshots, scanned
/// receipts, slides) is also stored so it can be searched and cited. A photo without text is fine — it just has no chunks.
/// </summary>
public sealed class ImageExtractor(IOcrEngine ocr) : IDocumentExtractor
{
    public bool CanHandle(string extension) => FileTypes.IsImage(extension);

    public ExtractionResult Extract(Stream content, string extension, ExtractionLimits limits)
    {
        if (!ocr.IsAvailable)
        {
            return new ExtractionResult([], null, null);
        }

        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        limits.ReportProgress?.Invoke(0, 1);
        string text;
        try
        {
            text = ocr.Recognize(buffer.ToArray());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new DocumentExtractionException("This image could not be read.", "corrupted", ex);
        }
        limits.ReportProgress?.Invoke(1, 1);

        // OCR on photos often finds a few stray characters; only keep real text.
        if (text.Count(char.IsLetterOrDigit) < 8)
        {
            return new ExtractionResult([], null, null);
        }
        var budget = new TextBudget(limits.MaxCharacters);
        return new ExtractionResult(
            [new ExtractedSection("Text in the image", budget.Take(text), Source: ChunkSource.Ocr)],
            null,
            "Text in this image was read with OCR; it may contain errors.");
    }
}
