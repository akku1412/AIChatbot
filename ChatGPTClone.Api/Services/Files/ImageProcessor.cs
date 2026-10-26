using SkiaSharp;

namespace ChatGPTClone.Api.Services.Files;

/// <summary>A cleaned image ready to store.</summary>
public sealed record CleanImage(byte[] Bytes, int Width, int Height);

/// <summary>
/// Makes uploaded images safe and sensible to store and send to a vision model (SkiaSharp, MIT):
/// the size is checked from the header before decoding (no "decompression bombs"), then the image is decoded and
/// re-encoded in its own format — which drops EXIF/XMP metadata such as GPS location and camera details — and
/// downscaled if very large. EXIF orientation is applied first so photos stay upright.
/// </summary>
public static class ImageProcessor
{
    /// <summary>Longest side after cleaning; vision models don't benefit from more.</summary>
    public const int MaxSide = 2048;

    /// <summary>Refused before decoding: ~40 megapixels.</summary>
    private const long MaxPixels = 40_000_000;

    public static CleanImage Clean(byte[] original, string extension)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(original))
            ?? throw new DocumentExtractionException("This image is damaged or not a supported PNG, JPEG or WebP image.", "corrupted");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels)
        {
            throw new DocumentExtractionException(
                $"This image is too large ({info.Width}×{info.Height} pixels). Use an image under 40 megapixels.", "too_many_pixels");
        }

        using var decoded = SKBitmap.Decode(codec)
            ?? throw new DocumentExtractionException("This image could not be decoded.", "corrupted");
        using var upright = ApplyOrientation(decoded, codec.EncodedOrigin);

        var scale = Math.Min(1.0, (double)MaxSide / Math.Max(upright.Width, upright.Height));
        using var resized = scale < 1.0
            ? upright.Resize(new SKImageInfo((int)(upright.Width * scale), (int)(upright.Height * scale)), new SKSamplingOptions(SKCubicResampler.Mitchell))
            : null;
        var final = resized ?? upright;

        var (format, quality) = extension.ToLowerInvariant() switch
        {
            ".png" => (SKEncodedImageFormat.Png, 100),
            ".webp" => (SKEncodedImageFormat.Webp, 90),
            _ => (SKEncodedImageFormat.Jpeg, 90),
        };
        using var data = final.Encode(format, quality)
            ?? throw new DocumentExtractionException("This image could not be processed.", "corrupted");
        return new CleanImage(data.ToArray(), final.Width, final.Height);
    }

    /// <summary>Rotates/flips pixels according to the EXIF orientation, because the metadata itself is removed.</summary>
    private static SKBitmap ApplyOrientation(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default)
        {
            return bitmap.Copy();
        }

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? bitmap.Height : bitmap.Width, swap ? bitmap.Width : bitmap.Height, bitmap.ColorType, bitmap.AlphaType);
        using var canvas = new SKCanvas(result);
        var (w, h) = (result.Width, result.Height);
        canvas.SetMatrix(origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, w, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, w, -1, 0, h, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, h, 0, 0, 1),
            _ => SKMatrix.Identity,
        });
        canvas.DrawBitmap(bitmap, 0, 0);
        return result;
    }
}
