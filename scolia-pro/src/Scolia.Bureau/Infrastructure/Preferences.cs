using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Préférences d'affichage, enregistrées par poste dans le dossier de l'utilisateur.</summary>
public sealed class Preferences : Observable
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCOLIA", "bureau.json");

    /// <summary>Facteurs de zoom proposés par la taille du texte.</summary>
    public static readonly IReadOnlyList<ChoiceItem> TextSizes =
    [
        new("0.9", "Petit"), new("1", "Normal"), new("1.15", "Grand"), new("1.3", "Très grand"),
    ];

    /// <summary>Fenêtre de référence et bornes de l'adaptation automatique de l'interface.</summary>
    private const double ReferenceWidth = 1280;
    private const double ReferenceHeight = 720;
    private const double MinFit = 0.8;
    private const double MaxFit = 1.3;

    public static readonly IReadOnlyList<ChoiceItem> Themes =
    [
        new("system", "Système"), new("light", "Clair"), new("dark", "Sombre"),
    ];

    /// <summary>Couleurs d'accent : boutons principaux, sélection, liens. Une teinte pour le clair, une pour le sombre.</summary>
    private static readonly (string Key, string Label, string Light, string Dark)[] AccentTable =
    [
        ("bleu", "Bleu", "#007AFF", "#0A84FF"), ("indigo", "Indigo", "#5856D6", "#5E5CE6"), ("violet", "Violet", "#8E44AD", "#BF5AF2"),
        ("vert", "Vert", "#248A3D", "#30D158"), ("orange", "Orange", "#C25E00", "#FF9F0A"), ("graphite", "Graphite", "#55555A", "#98989D"),
    ];

    public static readonly IReadOnlyList<ChoiceItem> Accents = AccentTable.Select(a => new ChoiceItem(a.Key, a.Label)).ToList();

    private string theme = "light";
    private string accent = "bleu";
    private double scale = 1.0;
    private double fit = 1.0;
    private bool compact;
    private bool statusBar = true;
    private bool tips = true;
    private bool grid = true;
    private bool animations = true;
    private bool ribbonCollapsed;
    private bool sidebar = true;

    public string Theme { get => theme; set { if (Set(ref theme, value)) Raise(nameof(IsDark)); } }
    public bool IsDark { get => theme == "dark"; set => Theme = value ? "dark" : "light"; }
    public string Accent { get => accent; set => Set(ref accent, AccentTable.Any(a => a.Key == value) ? value : "bleu"); }
    public double Scale { get => scale; set { if (Set(ref scale, Math.Clamp(Math.Round(value, 2), 0.8, 1.6))) Raise(nameof(ZoomLabel)); } }
    public string ZoomLabel => $"{Math.Round(scale * 100):0} %";
    /// <summary>Taille de texte choisie par l'utilisateur. L'adaptation à la fenêtre agit à part, sur toute l'interface.</summary>
    public double Factor => scale;

    /// <summary>
    /// Échelle de toute l'interface selon la fenêtre : texte, marges, icônes, champs et volet grandissent ou rapetissent
    /// ensemble, si bien qu'un petit écran affiche la même mise en page qu'un écran de 1280 × 720, en plus petit.
    /// </summary>
    public double LayoutScale => fit;

    /// <summary>Signalé quand l'échelle de l'interface change, sans enregistrer les préférences.</summary>
    public event Action<double>? LayoutScaleChanged;
    public bool Compact { get => compact; set => Set(ref compact, value); }
    public bool StatusBar { get => statusBar; set => Set(ref statusBar, value); }
    public bool Tips { get => tips; set => Set(ref tips, value); }
    public bool Grid { get => grid; set => Set(ref grid, value); }
    public bool Animations { get => animations; set => Set(ref animations, value); }
    public bool RibbonCollapsed { get => ribbonCollapsed; set => Set(ref ribbonCollapsed, value); }

    /// <summary>Barre latérale de navigation affichée à gauche.</summary>
    public bool Sidebar { get => sidebar; set => Set(ref sidebar, value); }

    public static Preferences Load()
    {
        var prefs = new Preferences();
        try
        {
            if (File.Exists(FilePath))
            {
                var data = JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath));
                if (data is not null) data.ApplyTo(prefs);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return prefs;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Stored.From(this)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Adapte toute l'interface à la taille de la fenêtre : 1 à 1280 × 720, plus grand sur un grand écran,
    /// plus petit sur un écran réduit. Les valeurs sont arrondies au vingtième pour éviter les recalculs continus pendant un redimensionnement.
    /// </summary>
    public void Fit(double width, double height)
    {
        var next = Math.Round(Math.Clamp(Math.Min(width / ReferenceWidth, height / ReferenceHeight), MinFit, MaxFit) * 20) / 20;
        if (next == fit) return;
        fit = next;
        LayoutScaleChanged?.Invoke(fit);
    }

    /// <summary>Applique les préférences à toute l'interface : thème, taille, densité.</summary>
    public void Apply()
    {
        var app = Application.Current;
        if (app is null) return;
        app.RequestedThemeVariant = theme switch
        {
            "dark" => ThemeVariant.Dark,
            "light" => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };
        var r = app.Resources;
        ApplyAccent(r);
        var k = Factor;
        GridPriority.SetTextFactor(k);
        r["FsBase"] = 13.5 * k;
        r["FsSmall"] = 12.0 * k;
        r["FsCaption"] = 11.0 * k;
        r["FsHeading"] = 15.0 * k;
        r["FsTitle"] = 22.0 * k;
        r["FsDisplay"] = 28.0 * k;
        r["RowHeight"] = (compact ? 28.0 : 36.0) * k;
        r["CtlHeight"] = (compact ? 26.0 : 30.0) * k;
        r["GridLines"] = grid ? Avalonia.Controls.DataGridGridLinesVisibility.Horizontal : Avalonia.Controls.DataGridGridLinesVisibility.None;
    }

    /// <summary>
    /// Couleur d'accent : elle remplace celle de la palette dans le clair et le sombre, avec sa teinte de survol
    /// et sa teinte douce pour les sélections. Les contrôles standard la reprennent aussi.
    /// </summary>
    private void ApplyAccent(Avalonia.Controls.IResourceDictionary resources)
    {
        var a = AccentTable.First(x => x.Key == accent);
        resources.ThemeDictionaries[ThemeVariant.Light] = AccentDictionary(Color.Parse(a.Light), Colors.White, dark: false);
        resources.ThemeDictionaries[ThemeVariant.Dark] = AccentDictionary(Color.Parse(a.Dark), Color.Parse("#1C1C1E"), dark: true);
    }

    private static Avalonia.Controls.ResourceDictionary AccentDictionary(Color color, Color background, bool dark)
    {
        static Color Mix(Color a, Color b, double t) => Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        var hover = dark ? Mix(color, Colors.White, 0.2) : Mix(color, Colors.Black, 0.15);
        var soft = Mix(background, color, dark ? 0.3 : 0.15);
        return new Avalonia.Controls.ResourceDictionary
        {
            ["AccentBrush"] = new SolidColorBrush(color),
            ["AccentHoverBrush"] = new SolidColorBrush(hover),
            ["AccentSoftBrush"] = new SolidColorBrush(soft),
            ["AccentColor"] = color,
            ["SystemAccentColor"] = color,
            ["SystemAccentColorDark1"] = hover,
            ["SystemAccentColorLight1"] = hover,
        };
    }

    /// <summary>Forme enregistrée : seules les valeurs simples sont conservées.</summary>
    private sealed class Stored
    {
        public string? Theme { get; set; }
        public string? Accent { get; set; }
        public double Scale { get; set; } = 1.0;
        public bool Compact { get; set; }
        public bool StatusBar { get; set; } = true;
        public bool Tips { get; set; } = true;
        public bool Grid { get; set; } = true;
        public bool Animations { get; set; } = true;
        public bool RibbonCollapsed { get; set; }
        public bool Sidebar { get; set; } = true;

        public static Stored From(Preferences p) => new()
        {
            Theme = p.theme, Accent = p.accent, Scale = p.scale, Compact = p.compact, StatusBar = p.statusBar, Tips = p.tips,
            Grid = p.grid, Animations = p.animations, RibbonCollapsed = p.ribbonCollapsed, Sidebar = p.sidebar,
        };

        public void ApplyTo(Preferences p)
        {
            p.Theme = Theme is "light" or "dark" or "system" ? Theme : "light";
            p.Accent = Accent ?? "bleu";
            p.Scale = Scale;
            p.Compact = Compact;
            p.StatusBar = StatusBar;
            p.Tips = Tips;
            p.Grid = Grid;
            p.Animations = Animations;
            p.RibbonCollapsed = RibbonCollapsed;
            p.Sidebar = Sidebar;
        }
    }
}
