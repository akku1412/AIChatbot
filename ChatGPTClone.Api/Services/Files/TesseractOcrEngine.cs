using ChatGPTClone.Api.Configuration;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Tesseract;

namespace ChatGPTClone.Api.Services.Files;

/// <summary>
/// OCR with Tesseract 5 (Apache-2.0) and the "fast" language data in <c>tessdata/</c> (English + Hindi by default).
/// One engine is reused under a lock: Tesseract engines aren't thread-safe, and documents are processed one at a time
/// by the background worker anyway. Images are normalised to PNG first (Leptonica can't read every WebP variant).
/// </summary>
public sealed class TesseractOcrEngine : IOcrEngine, IDisposable
{
    private readonly object _gate = new();
    private readonly string? _dataPath;
    private readonly string _languages;
    private readonly ILogger<TesseractOcrEngine> _logger;
    private TesseractEngine? _engine;

    public TesseractOcrEngine(IOptions<OcrOptions> options, IHostEnvironment environment, ILogger<TesseractOcrEngine> logger)
    {
        _logger = logger;
        var settings = options.Value;
        _languages = settings.Languages;
        if (!settings.Enabled)
        {
            return;
        }

        // Look next to the app first (published output), then in the content root (dotnet run).
        var candidates = Path.IsPathRooted(settings.TessDataPath)
            ? [settings.TessDataPath]
            : new[] { Path.Combine(AppContext.BaseDirectory, settings.TessDataPath), Path.Combine(environment.ContentRootPath, settings.TessDataPath) };
        var languages = _languages.Split('+', StringSplitOptions.RemoveEmptyEntries);
        _dataPath = candidates.FirstOrDefault(dir => languages.All(lang => File.Exists(Path.Combine(dir, $"{lang}.traineddata"))));
        if (_dataPath is null)
        {
            logger.LogWarning("OCR is disabled: language data for {Languages} not found in {Path}. Run scripts/download-models.ps1.",
                _languages, settings.TessDataPath);
        }
    }

    public bool IsAvailable => _dataPath is not null;

    public string Recognize(byte[] image)
    {
        if (_dataPath is null)
        {
            throw new InvalidOperationException("OCR is not available.");
        }

        var png = ToPng(image);
        lock (_gate)
        {
            _engine ??= new TesseractEngine(_dataPath, _languages, EngineMode.Default);
            using var pix = Pix.LoadFromMemory(png);
            using var page = _engine.Process(pix);
            return Normalize(page.GetText());
        }
    }

    private static byte[] ToPng(byte[] image)
    {
        using var bitmap = SKBitmap.Decode(image) ?? throw new InvalidDataException("The image could not be decoded.");
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static string Normalize(string text) =>
        string.Join('\n', text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0)).Trim();

    public void Dispose()
    {
        lock (_gate)
        {
            _engine?.Dispose();
            _engine = null;
        }
    }
}

/// <summary>No OCR (tests, or when disabled): scanned documents are rejected with a clear message.</summary>
public sealed class NoOcrEngine : IOcrEngine
{
    public bool IsAvailable => false;
    public string Recognize(byte[] image) => throw new InvalidOperationException("OCR is not available.");
}
