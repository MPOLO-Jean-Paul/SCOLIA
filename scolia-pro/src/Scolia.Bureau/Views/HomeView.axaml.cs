using Avalonia;
using Avalonia.Controls;

namespace Scolia.Bureau.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        // Conserver la zone des boutons sur la DROITE, y compris lorsque
        // la fenêtre est réduite ou le menu latéral ouvert. Seule leur grille
        // interne s'adapte, jamais la place de la colonne principale.
        WelcomeLayout.SizeChanged += (_, e) => FitWelcomeActions(e.NewSize.Width);
    }

    private void FitWelcomeActions(double available)
    {
        if (available <= 0) return;
        // macOS 27 : colonne d'actions stable, plus fine pour éviter de
        // comprimer le nom de l'école. Les boutons restent à droite même étroits.
        var rightWidth = Math.Min(244, Math.Max(98, available * 0.34));
        if (Math.Abs(WelcomeQuickActions.Width - rightWidth) >= 0.5)
            WelcomeQuickActions.Width = rightWidth;

        // Sur une fenêtre très étroite, l'avatar disparaît avant de comprimer
        // le nom de l'utilisateur ; les boutons demeurent à droite.
        WelcomeAvatar.IsVisible = available >= 350;
        var compact = available < 520;
        WelcomeAvatar.Width = compact ? 34 : 48;
        WelcomeAvatar.Height = compact ? 34 : 48;
        WelcomeAvatar.CornerRadius = new CornerRadius(compact ? 17 : 24);
        WelcomeGreeting.FontSize = compact ? 16 : 20;
    }
}
