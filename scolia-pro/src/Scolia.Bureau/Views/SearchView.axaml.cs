using Avalonia.Controls;
using Avalonia.Input;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Views;

public partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();
        Loaded += (_, _) => QueryBox.Focus();
        QueryBox.KeyDown += (_, e) =>
        {
            if (DataContext is not SearchViewModel search) return;
            switch (e.Key)
            {
                case Key.Enter:
                    if (search.Open.CanExecute(null)) search.Open.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Down:
                    ResultList.Focus();
                    e.Handled = true;
                    break;
            }
        };
        ResultList.DoubleTapped += (_, _) =>
        {
            if (DataContext is SearchViewModel search && search.Open.CanExecute(null)) search.Open.Execute(null);
        };
    }
}
