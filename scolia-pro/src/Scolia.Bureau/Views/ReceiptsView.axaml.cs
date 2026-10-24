using Avalonia.Controls;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Views;

public partial class ReceiptsView : UserControl
{
    public ReceiptsView()
    {
        InitializeComponent();
        ReceiptGrid.DoubleTapped += async (_, _) =>
        {
            if (DataContext is ReceiptsPage page) await page.OpenSelectedAsync();
        };
    }
}
