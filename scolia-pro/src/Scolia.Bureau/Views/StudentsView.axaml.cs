using Avalonia.Controls;

namespace Scolia.Bureau.Views;

public partial class StudentsView : UserControl
{
    public StudentsView()
    {
        // Le double-clic sur une ligne ouvre son détail : il est géré pour tous les tableaux par RowMenu.
        InitializeComponent();
    }
}
