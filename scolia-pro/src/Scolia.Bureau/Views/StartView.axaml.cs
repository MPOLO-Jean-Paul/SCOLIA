using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Scolia.Bureau.Views;

public partial class StartView : UserControl
{
    public StartView() => InitializeComponent();

    private void OnPreviousRecommendation(object? sender, RoutedEventArgs e) => MoveRecommendations(-1);
    private void OnNextRecommendation(object? sender, RoutedEventArgs e) => MoveRecommendations(1);

    private void MoveRecommendations(int direction)
    {
        var scroll = RecommendationScroll;
        var max = Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width);
        var step = Math.Max(220, Math.Floor(scroll.Viewport.Width / 220) * 220);
        scroll.Offset = new Vector(Math.Clamp(scroll.Offset.X + direction * step, 0, max), scroll.Offset.Y);
    }

    private void OnRecommendationScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroll || PreviousRecommendation is null || NextRecommendation is null) return;
        var max = Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width);
        PreviousRecommendation.IsEnabled = scroll.Offset.X > 1;
        NextRecommendation.IsEnabled = scroll.Offset.X < max - 1;
    }
}
