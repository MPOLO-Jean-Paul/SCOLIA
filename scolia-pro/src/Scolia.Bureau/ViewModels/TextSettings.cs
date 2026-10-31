using Avalonia.Media;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Police proposée : son nom écrit dans sa propre police, cochée quand elle est en cours.</summary>
public sealed class FontOptionVm(string key, string label, TextSettingsViewModel owner) : Observable
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    public FontFamily Family { get; } = Preferences.FamilyOf(key);
    public bool IsSelected => owner.Font == Key;
    public RelayCommand Choose { get; } = new(() => owner.Font = key);
    public void Refresh() => Raise(nameof(IsSelected));
}

/// <summary>Taille prête à l'emploi : Petit, Normal, Grand, Très grand.</summary>
public sealed class SizePresetVm(string label, double value, TextSettingsViewModel owner) : Observable
{
    public string Label { get; } = label;
    public double Value { get; } = value;
    public bool IsSelected => Math.Abs(owner.Scale - Value) < 0.001;
    public RelayCommand Choose { get; } = new(() => owner.Scale = value);
    public void Refresh() => Raise(nameof(IsSelected));
}

/// <summary>
/// Police et taille du texte : on choisit la police dans une liste où chaque nom est écrit dans sa police,
/// puis on règle la taille au curseur ou d'un toucher sur une taille prête. Tout s'applique en direct à l'application,
/// l'aperçu montre le résultat, et le réglage est gardé sur l'appareil.
/// </summary>
public sealed class TextSettingsViewModel : Observable
{
    /// <summary>Bornes du curseur : en dessous, le texte devient illisible ; au-delà, les écrans débordent.</summary>
    public const double MinScale = 0.85;
    public const double MaxScale = 1.4;
    private const double Step = 0.05;

    private readonly Preferences prefs;

    public TextSettingsViewModel(ShellViewModel shell)
    {
        prefs = shell.Prefs;
        Fonts = Preferences.Fonts.Select(f => new FontOptionVm(f.Value, f.Label, this)).ToList();
        Presets = Preferences.TextSizes.Select(s => new SizePresetVm(s.Label, double.Parse(s.Value, System.Globalization.CultureInfo.InvariantCulture), this)).ToList();
        Close = new RelayCommand(shell.CloseDialog);
        Smaller = new RelayCommand(() => Scale -= Step);
        Larger = new RelayCommand(() => Scale += Step);
        Reset = new RelayCommand(() =>
        {
            Font = Preferences.Fonts[0].Value == "systeme" ? "systeme" : "inter";
            Scale = 1.0;
        });
    }

    public IReadOnlyList<FontOptionVm> Fonts { get; }
    public IReadOnlyList<SizePresetVm> Presets { get; }
    public RelayCommand Close { get; }
    public RelayCommand Smaller { get; }
    public RelayCommand Larger { get; }
    public RelayCommand Reset { get; }

    public double Minimum => MinScale;
    public double Maximum => MaxScale;

    public string Font
    {
        get => prefs.Font;
        set
        {
            if (prefs.Font == value) return;
            prefs.Font = value;
            Raise(nameof(Font));
            Raise(nameof(FontLabel));
            foreach (var f in Fonts) f.Refresh();
        }
    }

    public string FontLabel => Fonts.FirstOrDefault(f => f.IsSelected)?.Label ?? string.Empty;

    /// <summary>Taille du texte, de 85 % à 140 %, par pas de 5 % : le curseur ne s'arrête que sur ces valeurs.</summary>
    public double Scale
    {
        get => prefs.Scale;
        set
        {
            var next = Math.Round(Math.Clamp(value, MinScale, MaxScale) / Step) * Step;
            next = Math.Round(next, 2);
            if (Math.Abs(prefs.Scale - next) < 0.001) return;
            prefs.Scale = next;
            Raise(nameof(Scale));
            Raise(nameof(ScaleLabel));
            Raise(nameof(PreviewSize));
            Raise(nameof(PreviewTitleSize));
            foreach (var p in Presets) p.Refresh();
        }
    }

    public string ScaleLabel => $"{Math.Round(prefs.Scale * 100):0} %";

    /// <summary>Tailles de l'aperçu, celles des textes de l'application à l'échelle choisie.</summary>
    public double PreviewSize => 13.5 * prefs.Scale;
    public double PreviewTitleSize => 20 * prefs.Scale;
}
