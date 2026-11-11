using Avalonia.Media;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Contraste WCAG : le texte des boutons reste lisible au repos comme au survol.</summary>
public static class ThemeContrast
{
    public static double Ratio(Color first, Color second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    public static Color OnAccent(Color background, Color hover)
    {
        var dark = Color.Parse("#111111");
        return Math.Min(Ratio(Colors.White, background), Ratio(Colors.White, hover))
            >= Math.Min(Ratio(dark, background), Ratio(dark, hover)) ? Colors.White : dark;
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }
}
