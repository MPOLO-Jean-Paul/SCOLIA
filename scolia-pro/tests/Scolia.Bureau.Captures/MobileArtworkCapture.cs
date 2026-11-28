using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Material.Icons;
using Scolia.Bureau.Controls;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;

internal static class MobileArtworkCapture
{
    public static void Run(string output)
    {
        var checks = 0;
        void Check(string label, bool ok) { if (!ok) throw new InvalidOperationException(label); checks++; }
        foreach (var kind in FeatureVisuals.PageIcons.Values)
        {
            Check($"{kind} : symbole arrondi natif", MobileArtwork.HasSymbol(kind));
            Check($"{kind} : cache partagé", ReferenceEquals(MobileArtwork.For(kind), MobileArtwork.For(kind)));
            var bounds = MobileArtwork.Glyph(kind).Bounds;
            Check($"{kind} : géométrie sur sa grille de 24 points", bounds.X >= -.01 && bounds.Y >= -.01 && bounds.Right <= 24.01 && bounds.Bottom <= 24.01);
            Check($"{kind} : carré de 32 points", MobileArtwork.For(kind).Size == new Size(32, 32));
        }
        foreach (var size in new[] { 16, 24, 32, 48 })
        {
            var hashes = new HashSet<string>();
            foreach (var (page, kind) in FeatureVisuals.PageIcons)
            {
                var glyph = new FluentIcon { Data = MobileArtwork.Glyph(kind), Foreground = Brushes.Black };
                glyph.Measure(new Size(size, size)); glyph.Arrange(new Rect(0, 0, size, size));
                var pixels = FluentIconCapture.Render(glyph, size, size);
                Check($"{page.Name} {size} : symbole visible", pixels.Where((_, index) => index % 4 == 3).Any(alpha => alpha > 100));
                Check($"{page.Name} {size} : identité distincte", hashes.Add(Convert.ToHexString(SHA256.HashData(pixels))));
            }
        }
        foreach (var dark in new[] { false, true })
        {
            Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var ink = new SolidColorBrush(Color.Parse(dark ? "#F3F6FF" : "#15253F"));
            var background = new SolidColorBrush(Color.Parse(dark ? "#111A28" : "#F4F7FC"));
            var surface = new SolidColorBrush(Color.Parse(dark ? "#1C293B" : "#FFFFFF"));
            var grid = new UniformGrid { Columns = 4 };
            foreach (var (page, kind) in FeatureVisuals.PageIcons)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                foreach (var size in new[] { 40, 24, 32 })
                    row.Children.Add(new Image { Source = MobileArtwork.For(kind), Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center });
                var stack = new StackPanel { Spacing = 12 };
                stack.Children.Add(new TextBlock { Text = FluentIconCapture.Label(page), FontSize = 13, FontWeight = FontWeight.Medium, Foreground = ink });
                stack.Children.Add(row);
                grid.Children.Add(new Border { Child = stack, Background = surface, Margin = new Thickness(5), Padding = new Thickness(14), CornerRadius = new CornerRadius(12) });
            }
            var content = new StackPanel { Spacing = 16 };
            content.Children.Add(new TextBlock { Text = "SCOLIA · Icônes mobiles", FontSize = 25, Foreground = ink, FontWeight = FontWeight.SemiBold });
            content.Children.Add(new TextBlock { Text = "Symboles arrondis · Carrés bleus et verts · Reflets fixes · Vecteurs natifs", Foreground = ink, FontSize = 14 });
            content.Children.Add(grid);
            var window = new Window { Width = 1100, Height = 1660, Content = new Border { Padding = new Thickness(22), Child = content }, Background = background };
            window.Show();
            for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
            using var bitmap = new RenderTargetBitmap(new PixelSize(1100, 1660));
            bitmap.Render(window); bitmap.Save(Path.Combine(output, dark ? "icones-mobiles-sombre.png" : "icones-mobiles-clair.png"));
            window.Close();
        }
        Console.WriteLine($"OK {checks} contrôles des icônes mobiles : symboles arrondis, dimensions et identités distinctes.");
    }
}
