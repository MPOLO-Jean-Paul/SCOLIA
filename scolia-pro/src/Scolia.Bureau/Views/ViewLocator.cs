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

    private static Type? ViewType(object? data)
    {
        if (ViewName(data) is not { } name) return null;
        var assembly = typeof(ViewLocator).Assembly;
        return (Infrastructure.PhoneLayout.Active ? assembly.GetType($"Scolia.Bureau.Views.Phone.{name}") : null)
            ?? assembly.GetType($"Scolia.Bureau.Views.{name}");
    }

    private static string? ViewName(object? data)
    {
        if (data is null || data.GetType().Namespace != "Scolia.Bureau.ViewModels") return null;
        var name = data.GetType().Name;
        if (name.EndsWith("ViewModel", StringComparison.Ordinal)) return name[..^"ViewModel".Length] + "View";
        if (name.EndsWith("Page", StringComparison.Ordinal)) return name[..^"Page".Length] + "View";
        return null;
    }
}
