using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Material.Icons;
using Material.Icons.Avalonia;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Views;

internal static class MobileMotionCapture
{
    public static void Run(string output)
    {
        var oldMobile = Platform.IsMobile;
        var oldPhone = PhoneLayout.Active;
        var oldHaptic = Platform.Haptic;
        var checks = 0;
        void Check(string label, bool ok)
        {
            if (!ok) throw new InvalidOperationException(label);
            checks++;
        }
        try
        {
            foreach (var (mobile, dark, width) in new[] { (true, false, 320), (true, true, 390), (false, false, 1024) })
            {
                Platform.IsMobile = mobile;
                PhoneLayout.Set(mobile);
                Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                var taps = 0;
                Platform.Haptic = kind => { if (kind == HapticKind.Tap) taps++; };
                var view = new MainView();
                var scaler = view.FindControl<LayoutTransformControl>("Scaler")!;
                scaler.Classes.Set("anim", true);
                scaler.Classes.Set("phone", mobile);
                var stack = new StackPanel { Spacing = 12, Margin = new Thickness(18) };
                stack.Children.Add(new TextBlock { Text = "SCOLIA · Contact et navigation", FontSize = 18 });
                var samples = new List<(Button Button, Image Artwork, MaterialIcon Glyph)>();
                foreach (var kind in new[] { "cardbutton", "tooltile", "settingsentry", "workspacerow" })
                {
                    var image = new Image { Source = FluentGlyphs.ArtworkFor(MaterialIconKind.Home), Width = 24, Height = 24 };
                    image.Classes.Add("featureillustration");
                    var glyph = new MaterialIcon { Kind = MaterialIconKind.ArrowLeft, Width = 20, Height = 20, Foreground = Brushes.RoyalBlue };
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
                    row.Children.Add(image); row.Children.Add(glyph); row.Children.Add(new TextBlock { Text = "Ouvrir une fonctionnalité", VerticalAlignment = VerticalAlignment.Center });
                    var frame = new Border { Child = row };
                    frame.Classes.AddRange(["toolcard", "feature"]);
                    var button = new Button { Content = frame, Width = 280, Height = 72, HorizontalAlignment = HorizontalAlignment.Left };
                    button.Classes.AddRange([kind, "primary"]);
                    stack.Children.Add(button);
                    samples.Add((button, image, glyph));
                }
                var area = new Grid { Name = "DesktopArea" };
                area.Children.Add(stack);
                scaler.Child = area;
                var window = new Window { Width = width, Height = 640, Content = view };
                window.Show();
                Pump(window, 260);
                Check("Le contrôle conserve les animations actives", scaler.Classes.Contains("anim"));
                foreach (var (button, image, glyph) in samples)
                {
                    window.MouseMove(new Point(4, 4)); Pump(window, 220);
                    var before = new[] { Bounds(button, window), Bounds(image, window), Bounds(glyph, window) };
                    var center = before[0].Center;
                    var clicks = 0;
                    button.Click += (_, _) => clicks++;
                    var initialTaps = taps;
                    window.MouseMove(center); Pump(window, 220);
                    if (!mobile)
                    {
                        if (button.Classes.Contains("cardbutton"))
                            Check("Le contrôle témoin desktop exerce bien le survol", !Same(before[1], Bounds(image, window)));
                        continue;
                    }
                    Check("Mobile : aucun mouvement au passage du pointeur", Stable());
                    Check("Mobile : le passage ne lance aucune action", clicks == 0 && taps == initialTaps);
                    window.MouseDown(center, MouseButton.Left); Pump(window, 180);
                    Check($"Mobile {string.Join(',', button.Classes)} ({string.Join(',', scaler.Classes)}) : positions au premier contact "
                        + $"{string.Join(';', before)} -> {Bounds(button, window)};{Bounds(image, window)};{Bounds(glyph, window)}", Stable());
                    Check("Mobile : aucune vibration avant le clic confirmé", taps == initialTaps && clicks == 0);
                    window.MouseMove(new Point(4, 4)); window.MouseUp(new Point(4, 4), MouseButton.Left); Pump(window, 220);
                    Check("Mobile : un geste abandonné ne déclenche rien", clicks == 0 && taps == initialTaps && Stable());
                    window.MouseMove(center); window.MouseDown(center, MouseButton.Left); window.MouseUp(center, MouseButton.Left); Pump(window, 60);
                    Check("Mobile : un appui confirmé exécute une seule action", clicks == 1 && taps == initialTaps + 1);
                    Check("Mobile : le retour tactile ne déplace pas le contenu", Stable());
                    bool Stable() => Same(before[0], Bounds(button, window)) && Same(before[1], Bounds(image, window)) && Same(before[2], Bounds(glyph, window));
                }
                using var bitmap = new RenderTargetBitmap(new PixelSize(width, 640));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(output, $"contact-{(mobile ? "mobile" : "desktop")}-{width}-{(dark ? "sombre" : "clair")}.png"));
                window.Close();
            }
        }
        finally { Platform.IsMobile = oldMobile; PhoneLayout.Set(oldPhone); Platform.Haptic = oldHaptic; }
        Console.WriteLine($"OK {checks} contrôles : cartes stables, gestes annulés, clic confirmé et témoin desktop.");
    }

    private static Rect Bounds(Control control, Window window)
        => new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window) ?? throw new InvalidOperationException("Contrôle non attaché."));
    private static bool Same(Rect a, Rect b) => Math.Abs(a.X - b.X) < .01 && Math.Abs(a.Y - b.Y) < .01 && Math.Abs(a.Width - b.Width) < .01 && Math.Abs(a.Height - b.Height) < .01;
    private static void Pump(Window window, int milliseconds)
    {
        var until = Environment.TickCount64 + milliseconds;
        do { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(8); }
        while (Environment.TickCount64 < until);
    }
}
