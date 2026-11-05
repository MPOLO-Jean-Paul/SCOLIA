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
        // Sous 620 px les boutons se rangent verticalement, toujours à droite
        // de la barre ; au-dessus, ils sont côte à côte dans le même panneau.
        // La largeur réservée laisse le reste à la salutation et au rôle.
        var rightWidth = Math.Min(286, Math.Max(102, available * 0.42));
        if (Math.Abs(WelcomeQuickActions.Width - rightWidth) < 0.5) return;
        WelcomeQuickActions.Width = rightWidth;
    }
}
