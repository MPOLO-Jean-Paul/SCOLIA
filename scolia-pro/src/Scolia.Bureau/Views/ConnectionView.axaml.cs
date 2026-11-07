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
    }
}
