using System.IO.Compression;
using SkiaSharp;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Le fichier choisi n'est pas une image que SCOLIA sait lire.</summary>
public sealed class LogoImageException(string message) : Exception(message);

/// <summary>
/// Logo de l'établissement : n'importe quelle image courante est acceptée, puis convertie en un PNG net et léger,
/// redressé selon la prise de vue et réduit à 512 points au plus. Le serveur, les reçus et les cartes reçoivent
/// toujours le même format, quel que soit le fichier d'origine.
/// </summary>
public static class LogoImage
{
    /// <summary>Plus grand côté du logo enregistré : assez pour une carte imprimée, léger pour le réseau.</summary>
    public const int MaxSide = 512;

    /// <summary>Taille la plus grande d'un fichier d'origine : une photo d'appareil récent tient largement.</summary>
    public const int MaxInputBytes = 40 * 1024 * 1024;

    /// <summary>Extensions proposées dans le sélecteur de fichiers.</summary>
    public static readonly IReadOnlyList<string> Extensions =
    [
        ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".bmp", ".dib", ".webp", ".ico", ".wbmp",
        ".svg", ".svgz", ".heic", ".heif", ".avif", ".tif", ".tiff",
    ];

    /// <summary>Formats lus par SCOLIA, pour les messages.</summary>
    public const string Formats = "PNG, JPEG, GIF, BMP, WebP, ICO ou SVG";

    /// <summary>Convertit l'image choisie en PNG prêt à enregistrer.</summary>
    public static byte[] ToPng(byte[] data, string? name)
    {
        if (data is null || data.Length == 0)
            throw new LogoImageException("Le fichier choisi est vide.");
        if (data.Length > MaxInputBytes)
            throw new LogoImageException("Le fichier choisi dépasse 40 Mo. Choisissez une image plus légère.");

        using var bitmap = (IsSvg(data, name) ? RenderSvg(data) : DecodeRaster(data))
            ?? DecodeWithDevice(data)
            ?? throw new LogoImageException(
                $"SCOLIA ne sait pas lire ce fichier. Formats acceptés : {Formats}" +
                (Platform.DecodeImage is null ? "." : ", et les photos HEIC du téléphone.") +
                " Sinon, ouvrez le logo dans Photos ou Paint et enregistrez-le en PNG.");

        using var fitted = Fit(bitmap);
        using var image = SKImage.FromBitmap(fitted);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new LogoImageException("Le logo n'a pas pu être préparé. Essayez une autre image.");
        return png.ToArray();
    }

    private static bool IsSvg(byte[] data, string? name)
    {
        var extension = Path.GetExtension(name ?? string.Empty).ToLowerInvariant();
        if (extension is ".svg" or ".svgz") return true;
        if (data is [0x1F, 0x8B, ..]) return extension == ".svgz";
        // Un SVG est du texte : « <svg » ou « <?xml » dans les premiers octets.
        var head = System.Text.Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, 512)).TrimStart('﻿', ' ', '\r', '\n', '\t');
        return head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
            || (head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) && head.Contains("<svg", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>PNG, JPEG, GIF (première image), BMP, WebP, ICO… redressé selon l'orientation de la prise de vue.</summary>
    private static SKBitmap? DecodeRaster(byte[] data)
    {
        using var codec = SKCodec.Create(new MemoryStream(data));
        if (codec is null) return null;
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (info.Width <= 0 || info.Height <= 0) return null;
        var decoded = new SKBitmap(info);
        var result = codec.GetPixels(info, decoded.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            decoded.Dispose();
            return null;
        }
        return Orient(decoded, codec.EncodedOrigin);
    }

    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return source;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var rotated = new SKBitmap(swap ? source.Height : source.Width, swap ? source.Width : source.Height, source.ColorType, source.AlphaType);
        using (var canvas = new SKCanvas(rotated))
        {
            canvas.Clear(SKColors.Transparent);
            float w = source.Width, h = source.Height;
            switch (origin)
            {
                case SKEncodedOrigin.TopRight: canvas.Translate(w, 0); canvas.Scale(-1, 1); break;
                case SKEncodedOrigin.BottomRight: canvas.Translate(w, h); canvas.RotateDegrees(180); break;
                case SKEncodedOrigin.BottomLeft: canvas.Translate(0, h); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.LeftTop: canvas.Scale(-1, 1); canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.RightTop: canvas.Translate(h, 0); canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.RightBottom: canvas.Translate(h, w); canvas.Scale(1, -1); canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.LeftBottom: canvas.Translate(0, w); canvas.RotateDegrees(-90); break;
            }
            canvas.DrawBitmap(source, 0, 0);
        }
        source.Dispose();
        return rotated;
    }

    /// <summary>Logo vectoriel : dessiné net à 512 points sur fond transparent.</summary>
    private static SKBitmap? RenderSvg(byte[] data)
    {
        try
        {
            var bytes = data is [0x1F, 0x8B, ..] ? Gunzip(data) : data;
            using var svg = new Svg.Skia.SKSvg();
            using var stream = new MemoryStream(bytes);
            if (svg.Load(stream) is not { } picture) return null;
            var bounds = picture.CullRect;
            if (bounds.Width <= 0 || bounds.Height <= 0) return null;
            var scale = MaxSide / Math.Max(bounds.Width, bounds.Height);
            var bitmap = new SKBitmap(Math.Max(1, (int)Math.Ceiling(bounds.Width * scale)), Math.Max(1, (int)Math.Ceiling(bounds.Height * scale)),
                SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scale);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
            return bitmap;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Fichier SVG mal formé : SCOLIA le dit au lieu de s'arrêter.
            return null;
        }
    }

    private static byte[] Gunzip(byte[] data)
    {
        using var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>Formats que seul le téléphone sait lire (photo HEIC de l'iPhone, TIFF…).</summary>
    private static SKBitmap? DecodeWithDevice(byte[] data)
    {
        if (Platform.DecodeImage is not { } decode) return null;
        try
        {
            return decode(data) is { Length: > 0 } png ? DecodeRaster(png) : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Réduit au plus à 512 points de côté, sans jamais agrandir ni déformer.</summary>
    private static SKBitmap Fit(SKBitmap source)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= MaxSide) return source.Copy();
        var scale = (double)MaxSide / longest;
        var info = new SKImageInfo(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)),
            SKColorType.Rgba8888, SKAlphaType.Premul);
        return source.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell)) ?? source.Copy();
    }
}
