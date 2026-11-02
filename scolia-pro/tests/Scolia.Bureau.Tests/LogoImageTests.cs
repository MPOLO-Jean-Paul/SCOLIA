using Scolia.Bureau.Infrastructure;
using SkiaSharp;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Logo de l'établissement : toute image courante devient un PNG d'au plus 512 points.</summary>
public sealed class LogoImageTests
{
    private static byte[] Image(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Blue };
            canvas.DrawRect(0, 0, width / 2f, height, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private static SKSizeI SizeOf(byte[] png)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
        using var bitmap = SKBitmap.Decode(png);
        return new SKSizeI(bitmap.Width, bitmap.Height);
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png, "logo.png")]
    [InlineData(SKEncodedImageFormat.Jpeg, "logo.jpg")]
    [InlineData(SKEncodedImageFormat.Webp, "logo.webp")]
    public void Une_grande_image_devient_un_png_reduit_sans_deformation(SKEncodedImageFormat format, string name)
    {
        var size = SizeOf(LogoImage.ToPng(Image(2000, 1000, format), name));
        Assert.Equal(512, size.Width);
        Assert.Equal(256, size.Height);
    }

    [Fact]
    public void Une_petite_image_n_est_pas_agrandie()
    {
        var size = SizeOf(LogoImage.ToPng(Image(120, 80, SKEncodedImageFormat.Png), "petit.png"));
        Assert.Equal(new SKSizeI(120, 80), size);
    }

    [Fact]
    public void Un_logo_svg_est_dessine_net_a_512_points()
    {
        var svg = """<svg xmlns="http://www.w3.org/2000/svg" width="40" height="20" viewBox="0 0 40 20"><rect width="40" height="20" fill="#1e4fd8"/></svg>"""u8.ToArray();
        var size = SizeOf(LogoImage.ToPng(svg, "logo.svg"));
        Assert.Equal(512, size.Width);
        Assert.Equal(256, size.Height);
    }

    [Fact]
    public void Un_svg_est_reconnu_meme_sans_extension()
    {
        var svg = """<?xml version="1.0"?><svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><circle cx="5" cy="5" r="5"/></svg>"""u8.ToArray();
        Assert.Equal(512, SizeOf(LogoImage.ToPng(svg, "logo")).Width);
    }

    [Fact]
    public void Un_fichier_illisible_donne_un_message_clair()
    {
        var error = Assert.Throws<LogoImageException>(() => LogoImage.ToPng("bonjour"u8.ToArray(), "logo.png"));
        Assert.Contains("PNG, JPEG", error.Message);
    }
}
