using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace JPServices.Owner;

public sealed class OwnerApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new OwnerWindow();
        else if (ApplicationLifetime is IActivityApplicationLifetime android)
            android.MainViewFactory = () => new OwnerShell();
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
            single.MainView = new OwnerShell();
        base.OnFrameworkInitializationCompleted();
    }
}
