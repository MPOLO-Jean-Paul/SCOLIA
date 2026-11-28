using Avalonia.Controls;

namespace Scolia.Bureau.Views;

/// <summary>
/// Accueil bureautique Fluent : le dimensionnement Power BI s'adapte aux largeurs
/// par les grilles et panneaux de la vue ; aucun recalcul de pixels en code-behind.
/// L'accueil mobile utilise toujours sa vue dédiée.
/// </summary>
public partial class HomeView : UserControl
{
    public HomeView() => InitializeComponent();
}
