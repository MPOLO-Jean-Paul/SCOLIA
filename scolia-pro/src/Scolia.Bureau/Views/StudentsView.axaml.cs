using Avalonia.Controls;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Views;

public partial class StudentsView : UserControl
{
    public StudentsView()
    {
        InitializeComponent();
        StudentGrid.DoubleTapped += async (_, _) =>
        {
            if (DataContext is StudentsPage page) await page.OpenSelectedAsync();
        };
    }
}
