using System.ComponentModel.DataAnnotations;

namespace ChatGPTClone.Api.Configuration;

/// <summary>Bound from "Ocr". Text recognition for scanned PDF pages and images (Tesseract).</summary>
public sealed class OcrOptions
{
    public const string SectionName = "Ocr";

    public bool Enabled { get; init; } = true;

    /// <summary>Folder with the *.traineddata files (relative to the app). See scripts/download-models.ps1.</summary>
    [Required]
    public string TessDataPath { get; init; } = "tessdata";

    /// <summary>Tesseract language codes joined with "+", e.g. "eng+hin".</summary>
    [Required]
    public string Languages { get; init; } = "eng+hin";

    /// <summary>Render resolution for scanned PDF pages; higher is more accurate and slower.</summary>
    [Range(100, 400)]
    public int PdfRenderDpi { get; init; } = 250;

    /// <summary>At most this many scanned pages are OCR'd per PDF (the rest is reported in the file's note).</summary>
    [Range(1, 2000)]
    public int MaxOcrPages { get; init; } = 100;
}
