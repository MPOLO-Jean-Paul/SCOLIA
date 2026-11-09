using Avalonia;
using Avalonia.Controls;

namespace JPServices.Owner;

public sealed class OwnerWindow : Window
{
    public OwnerWindow()
    {
        Title = "SCOLIA Console";
        Background = OwnerPalette.Background;
        Width = 1220;
        Height = 800;
        MinWidth = 720;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = new OwnerShell();
    }
}
