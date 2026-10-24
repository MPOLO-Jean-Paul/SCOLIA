using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Scolia.Bureau.Views;

/// <summary>Associe chaque écran à sa vue par le nom : DashboardPage devient DashboardView, FormViewModel devient FormView.</summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        var name = ViewName(param);
        if (name is null) return null;
        var type = typeof(ViewLocator).Assembly.GetType($"Scolia.Bureau.Views.{name}");
        return type is null ? null : (Control?)Activator.CreateInstance(type);
    }

    public bool Match(object? data) => ViewName(data) is { } name && typeof(ViewLocator).Assembly.GetType($"Scolia.Bureau.Views.{name}") is not null;

    private static string? ViewName(object? data)
    {
        if (data is null || data.GetType().Namespace != "Scolia.Bureau.ViewModels") return null;
        var name = data.GetType().Name;
        if (name.EndsWith("ViewModel", StringComparison.Ordinal)) return name[..^"ViewModel".Length] + "View";
        if (name.EndsWith("Page", StringComparison.Ordinal)) return name[..^"Page".Length] + "View";
        return null;
    }
}
