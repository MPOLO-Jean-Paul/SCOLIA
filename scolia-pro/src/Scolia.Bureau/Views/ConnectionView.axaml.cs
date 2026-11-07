using Avalonia.Controls;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.Views;

public partial class ConnectionView : UserControl
{
    public ConnectionView()
    {
        InitializeComponent();
        // Même premier écran iOS sur le téléphone et sur l'ordinateur ; l'ancienne page reste en réserve.
        var ios = AuthLayout.UseIosStyle;
        ClassicConnectionScreen.IsVisible = !ios;
        IosConnectionScreen.IsVisible = ios;
        IosConnectionThemeScope.IsVisible = ios;
        // Les options secondaires restent disponibles, mais ne surchargent pas un petit écran.
        bool? previouslyCompact = null;
        AuthLayout.Track(IosConnectionScreen, size =>
        {
            var compact = size.Width < AuthLayout.CompactWidth || size.Height < AuthLayout.ShortHeight;
            if (previouslyCompact == compact) return;
            IosQrOptions.IsExpanded = !compact;
            IosWifiOptions.IsExpanded = !compact;
            IosNearbyServers.IsExpanded = !compact;
            previouslyCompact = compact;
        });
    }
}
