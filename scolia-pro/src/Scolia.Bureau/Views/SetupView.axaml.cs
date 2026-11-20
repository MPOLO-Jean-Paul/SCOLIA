using Avalonia.Controls;

namespace Scolia.Bureau.Views;

public partial class SetupView : UserControl
{
    /// <summary>Largeur sous laquelle le bandeau des étapes se resserre pour laisser la place au formulaire.</summary>
    private const double MediumBelow = 1100;

    public SetupView()
    {
        InitializeComponent();
        SetupRoot.SizeChanged += (_, e) => SetupRoot.Classes.Set("medium", e.NewSize.Width < MediumBelow);
    }
}
