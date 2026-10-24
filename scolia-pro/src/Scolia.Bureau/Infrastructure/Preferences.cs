using System.Text.Json;
using Avalonia;
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

    /// <summary>Fenêtre de référence et bornes de l'adaptation automatique du texte.</summary>
    private const double ReferenceWidth = 1280;
    private const double ReferenceHeight = 800;
    private const double MinFit = 0.85;
    private const double MaxFit = 1.25;

    public static readonly IReadOnlyList<ChoiceItem> Themes =
    [
        new("system", "Système"), new("light", "Clair"), new("dark", "Sombre"),
    ];

    private string theme = "light";
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
    public double Scale { get => scale; set { if (Set(ref scale, Math.Clamp(Math.Round(value, 2), 0.8, 1.6))) Raise(nameof(ZoomLabel)); } }
    public string ZoomLabel => $"{Math.Round(scale * 100):0} %";
    /// <summary>Taille de texte réellement appliquée : le choix de l'utilisateur, adapté à la taille de la fenêtre.</summary>
    public double Factor => scale * fit;
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
    /// Adapte le texte et les contrôles à la taille de la fenêtre : 1 à 1280 × 800, plus grand sur un grand écran,
    /// plus petit sur un écran réduit. Les valeurs sont arrondies au vingtième pour éviter les recalculs continus pendant un redimensionnement.
    /// </summary>
    public void Fit(double width, double height)
    {
        var next = Math.Round(Math.Clamp(Math.Min(width / ReferenceWidth, height / ReferenceHeight), MinFit, MaxFit) * 20) / 20;
        if (next == fit) return;
        fit = next;
        Apply();
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
        var k = Factor;
        GridPriority.SetTextFactor(k);
        r["FsBase"] = 14.0 * k;
        r["FsSmall"] = 12.0 * k;
        r["FsCaption"] = 12.0 * k;
        r["FsHeading"] = 16.0 * k;
        r["FsTitle"] = 24.0 * k;
        r["FsDisplay"] = 28.0 * k;
        r["RowHeight"] = (compact ? 28.0 : 34.0) * k;
        r["CtlHeight"] = (compact ? 28.0 : 32.0) * k;
        r["GridLines"] = grid ? Avalonia.Controls.DataGridGridLinesVisibility.Horizontal : Avalonia.Controls.DataGridGridLinesVisibility.None;
    }

    /// <summary>Forme enregistrée : seules les valeurs simples sont conservées.</summary>
    private sealed class Stored
    {
        public string? Theme { get; set; }
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
            Theme = p.theme, Scale = p.scale, Compact = p.compact, StatusBar = p.statusBar, Tips = p.tips,
            Grid = p.grid, Animations = p.animations, RibbonCollapsed = p.ribbonCollapsed, Sidebar = p.sidebar,
        };

        public void ApplyTo(Preferences p)
        {
            p.Theme = Theme is "light" or "dark" or "system" ? Theme : "light";
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
