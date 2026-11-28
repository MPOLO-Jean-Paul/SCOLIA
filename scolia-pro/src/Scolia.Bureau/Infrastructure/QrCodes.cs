using Avalonia.Media.Imaging;
using QRCoder;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Codes QR de liaison : dessinés pour l'écran du serveur, lus dans une photo ou une image sur l'autre appareil.
/// Tout se fait sur l'appareil, sans Internet.
/// </summary>
public static class QrCodes
{
    /// <summary>Image nette du code, noir sur blanc, avec la marge blanche que demandent les lecteurs.</summary>
    public static Bitmap Render(string text, int pixelsPerModule = 10)
    {
        using var stream = new MemoryStream(Png(text, pixelsPerModule));
        return new Bitmap(stream);
    }

    /// <summary>Le même code en PNG, pour l'enregistrer ou le partager.</summary>
    public static byte[] Png(string text, int pixelsPerModule = 10)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        // Noir pur avec large zone blanche ; les photos de petits écrans
        // et le zoom automatique décodent mieux que les modules bleu nuit.
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule, [0, 0, 0], [255, 255, 255], drawQuietZones: true);
    }

    /// <summary>
    /// Texte du premier code QR trouvé dans une image (photo de l'écran du serveur, capture, fichier), ou null.
    /// Une grande photo est d'abord réduite : la lecture reste rapide sur un téléphone.
    /// </summary>
    public static string? Read(byte[] image)
    {
        if (image.Length == 0) return null;
        // Fichier qui n'est pas une image (ou image abîmée) : simplement « aucun code », jamais une erreur.
        using var codec = SKCodec.Create(new MemoryStream(image));
        if (codec is null) return null;
        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null || decoded.Width == 0 || decoded.Height == 0) return null;
        foreach (var side in new[] { 1600, 900, 2600 })
            if (ReadAt(decoded, side) is { Length: > 0 } text) return text;
        return null;
    }

    private static string? ReadAt(SKBitmap source, int maxSide)
    {
        var scale = Math.Min(1d, (double)maxSide / Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)(source.Width * scale));
        var height = Math.Max(1, (int)(source.Height * scale));
        using var resized = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(resized))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint();
            using var image = SKImage.FromBitmap(source);
            canvas.DrawImage(image, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear), paint);
        }
        var pixels = resized.Bytes;
        var luminance = new RGBLuminanceSource(pixels, width, height, RGBLuminanceSource.BitmapFormat.RGBA32);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = [BarcodeFormat.QR_CODE],
            },
        };
        return reader.Decode(luminance)?.Text;
    }
}
