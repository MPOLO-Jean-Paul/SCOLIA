using Avalonia.Controls;
using Avalonia.Input;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Views;

/// <summary>Recherche de l'ordinateur : le curseur est dans le champ, Entrée ouvre le premier résultat.</summary>
public partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();
        Loaded += (_, _) => QueryBox.Focus();
        QueryBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || DataContext is not SearchViewModel search) return;
            if (search.Open.CanExecute(null)) search.Open.Execute(null);
            e.Handled = true;
        };
    }
}
