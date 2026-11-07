using Avalonia.Controls;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.Views;

public partial class ConnectionView : UserControl
{
    public ConnectionView()
    {
        InitializeComponent();
        // Android accueille avec ses propres groupes iOS ; la page Windows reste inchangée.
        var mobile = Platform.IsMobile;
        ClassicConnectionScreen.IsVisible = !mobile;
        IosConnectionScreen.IsVisible = mobile;
        // Les options secondaires restent disponibles, mais ne surchargent pas un petit écran.
        bool? previouslyCompact = null;
        IosConnectionScreen.SizeChanged += (_, e) =>
        {
            var compact = e.NewSize.Width < 390;
            IosConnectionScreen.Classes.Set("compact", compact);
            if (previouslyCompact == compact) return;
            IosQrOptions.IsExpanded = !compact;
            IosWifiOptions.IsExpanded = !compact;
            IosNearbyServers.IsExpanded = !compact;
            previouslyCompact = compact;
        };
    }
}
