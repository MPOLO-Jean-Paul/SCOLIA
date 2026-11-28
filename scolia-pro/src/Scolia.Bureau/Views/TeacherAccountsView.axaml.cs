using Avalonia;
using Avalonia.Controls;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Views;

public partial class TeacherAccountsView : UserControl
{
    public TeacherAccountsView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        ShowJuryColumn();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ShowJuryColumn();
    }

    /// <summary>
    /// La colonne « Jury » ne concerne que la délibération ITM/IM. Elle est retirée, pas masquée :
    /// la priorité des colonnes (<c>GridPriority</c>) réaffiche une colonne masquée quand la place le permet.
    /// </summary>
    private void ShowJuryColumn()
    {
        if (DataContext is not TeacherAccountsPage { IsItm: false } || this.FindControl<DataGrid>("Accounts") is not { } grid) return;
        foreach (var column in grid.Columns.Where(c => Equals(c.Header, "Jury")).ToList())
            grid.Columns.Remove(column);
    }
}
