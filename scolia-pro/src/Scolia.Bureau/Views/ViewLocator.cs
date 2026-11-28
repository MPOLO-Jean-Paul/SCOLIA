using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Scolia.Bureau.Views;

/// <summary>
/// Associe chaque écran à sa vue par le nom : DashboardPage devient DashboardView, FormViewModel devient FormView.
/// Sur le téléphone, une vue dessinée pour le petit écran passe d'abord : Views.Phone.DashboardView, si elle existe.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? param) => ViewType(param) is { } type ? (Control?)Activator.CreateInstance(type) : null;

    public bool Match(object? data) => ViewType(data) is not null;

    /// <summary>Vues déjà trouvées : la recherche par nom (réflexion) n'est faite qu'une fois par écran et par disposition.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type, bool), Type?> Found = new();

    private static Type? ViewType(object? data)
    {
        if (data is null) return null;
        return Found.GetOrAdd((data.GetType(), Infrastructure.PhoneLayout.Active), static key =>
        {
            if (ViewName(key.Item1) is not { } name) return null;
            var assembly = typeof(ViewLocator).Assembly;
            return (key.Item2 ? assembly.GetType($"Scolia.Bureau.Views.Phone.{name}") : null)
                ?? assembly.GetType($"Scolia.Bureau.Views.{name}");
        });
    }

    private static string? ViewName(Type type)
    {
        if (type.Namespace != "Scolia.Bureau.ViewModels") return null;
        var name = type.Name;
        if (name.EndsWith("ViewModel", StringComparison.Ordinal)) return name[..^"ViewModel".Length] + "View";
        if (name.EndsWith("Page", StringComparison.Ordinal)) return name[..^"Page".Length] + "View";
        return null;
    }
}
