using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Markup.Xaml.Styling;

namespace JPServices.Owner;

public sealed class OwnerApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Resources["FontFamilyDefault"] = new Avalonia.Media.FontFamily("avares://Scolia.Owner.Core/Assets/Fonts#Inter");
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://Scolia.Owner.Core/"))
            { Source = new Uri("avares://Scolia.Owner.Core/Themes/FluentArtwork.axaml") });
        Styles.Add(new StyleInclude(new Uri("avares://Scolia.Owner.Core/"))
            { Source = new Uri("avares://Scolia.Owner.Core/Themes/OwnerStyles.axaml") });
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ActualThemeVariantProperty)
                OwnerPalette.Apply(ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark);
        };
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
