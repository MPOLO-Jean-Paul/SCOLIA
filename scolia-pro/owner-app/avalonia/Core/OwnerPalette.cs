using Avalonia.Media;

namespace JPServices.Owner;

/// <summary>Un seul système visuel Avalonia, partagé bit pour bit sur Android et Windows.</summary>
internal static class OwnerPalette
{
    internal static readonly IBrush Navy = Brush("#112440");
    internal static readonly IBrush NavyLight = Brush("#1C3C67");
    internal static readonly IBrush Blue = Brush("#286DE5");
    internal static readonly IBrush BlueWash = Brush("#EAF1FF");
    internal static readonly IBrush Background = Brush("#F5F7FC");
    internal static readonly IBrush Paper = Brush("#FFFFFF");
    internal static readonly IBrush Ink = Brush("#152C4D");
    internal static readonly IBrush Muted = Brush("#6D7E98");
    internal static readonly IBrush Border = Brush("#E3EAF3");
    internal static readonly IBrush Teal = Brush("#128D86");
    internal static readonly IBrush GreenWash = Brush("#E5F6F1");
    internal static readonly IBrush Amber = Brush("#B77B1B");
    internal static readonly IBrush AmberWash = Brush("#FFF5DE");
    internal static readonly IBrush Red = Brush("#B34559");
    internal static readonly IBrush RedWash = Brush("#FFF0F2");

    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
