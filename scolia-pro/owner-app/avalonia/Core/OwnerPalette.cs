using Avalonia.Media;

namespace JPServices.Owner;

/// <summary>Un seul système visuel Avalonia, partagé bit pour bit sur Android et Windows.</summary>
internal static class OwnerPalette
{
    internal static readonly SolidColorBrush Navy = Brush("#112440");
    internal static readonly SolidColorBrush NavyLight = Brush("#1C3C67");
    internal static readonly SolidColorBrush Blue = Brush("#005FB8");
    internal static readonly SolidColorBrush BlueWash = Brush("#E5F0FF");
    internal static readonly SolidColorBrush Background = Brush("#F4F6FA");
    internal static readonly SolidColorBrush Paper = Brush("#FFFFFF");
    internal static readonly SolidColorBrush Ink = Brush("#1D2735");
    internal static readonly SolidColorBrush Muted = Brush("#536278");
    internal static readonly SolidColorBrush Border = Brush("#DFE5EE");
    internal static readonly SolidColorBrush Teal = Brush("#107C41");
    internal static readonly SolidColorBrush GreenWash = Brush("#E8F5ED");
    internal static readonly SolidColorBrush Amber = Brush("#9C4E00");
    internal static readonly SolidColorBrush AmberWash = Brush("#FFF3DA");
    internal static readonly SolidColorBrush Red = Brush("#B42335");
    internal static readonly SolidColorBrush RedWash = Brush("#FFF0F2");
    internal static readonly SolidColorBrush OnBlue = Brush("#FFFFFF");

    internal static void Apply(bool dark)
    {
        Set(Background, dark ? "#10141C" : "#F4F6FA");
        Set(Paper, dark ? "#1A202B" : "#FFFFFF");
        Set(Ink, dark ? "#EEF3FA" : "#1D2735");
        Set(Muted, dark ? "#AEBBCD" : "#536278");
        Set(Border, dark ? "#303A4A" : "#DFE5EE");
        Set(Blue, dark ? "#9ECAFF" : "#005FB8");
        Set(BlueWash, dark ? "#173A58" : "#E5F0FF");
        Set(NavyLight, dark ? "#BBD8FA" : "#1C3C67");
        Set(OnBlue, dark ? "#062440" : "#FFFFFF");
        Set(Teal, dark ? "#83DDA3" : "#107C41");
        Set(GreenWash, dark ? "#183A2A" : "#E8F5ED");
        Set(Amber, dark ? "#FFD28A" : "#9C4E00");
        Set(AmberWash, dark ? "#443019" : "#FFF3DA");
        Set(Red, dark ? "#FFACB6" : "#B42335");
        Set(RedWash, dark ? "#48242F" : "#FFF0F2");
    }

    private static void Set(SolidColorBrush brush, string color) => brush.Color = Color.Parse(color);

    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));
}
