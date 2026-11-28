using Avalonia.Controls;

namespace Scolia.Bureau.Views;

public partial class StaffView : UserControl
{
    public StaffView()
    {
        InitializeComponent();
        var salary = this.FindControl<DataGrid>("StaffTable")!.Columns.Single(column => Equals(column.Header, "Salaire de base"));
        DataContextChanged += (_, _) => Infrastructure.GridPriority.SetAllowed(salary, DataContext is ViewModels.StaffPage { CanManagePayroll: true });
    }
}
