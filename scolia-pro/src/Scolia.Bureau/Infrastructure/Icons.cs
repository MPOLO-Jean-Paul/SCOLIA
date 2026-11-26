using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Material.Icons;
using Material.Icons.Avalonia;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Règles d'icônes, comme les systèmes récents (iOS, Android) : pictogrammes pleins et blancs sur les tuiles de couleur
/// (navigation, réglages), pictogrammes au trait dans les barres d'outils, les menus et les boutons d'action.
/// Une seule taille par usage : 16 dans un bouton, 18 dans une ligne de liste.
/// </summary>
public static class Icons
{
    public const double ButtonSize = 16;
    public const double RowSize = 18;

    private static readonly ConcurrentDictionary<MaterialIconKind, MaterialIconKind> LineCache = new();
    // Comparés par valeur : plusieurs noms désignent le même pictogramme (Check = Done, Close = Multiply).
    private static readonly HashSet<MaterialIconKind> Strokes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Close", "Check", "Plus", "Minus", "ChevronLeft", "ChevronRight", "ChevronUp", "ChevronDown", "ArrowLeft", "ArrowRight",
        "ArrowUp", "ArrowDown", "Menu", "DotsHorizontal", "DotsVertical", "Magnify", "Refresh", "Sync", "Logout", "Login",
    }.Select(n => Enum.TryParse<MaterialIconKind>(n, out var k) ? (MaterialIconKind?)k : null).OfType<MaterialIconKind>().ToHashSet();

    /// <summary>Variante au trait d'un pictogramme quand elle existe (« Pencil » → « PencilOutline »), sinon le pictogramme lui-même.</summary>
    public static MaterialIconKind Line(MaterialIconKind kind) => LineCache.GetOrAdd(kind, static k =>
    {
        // Pictogrammes déjà faits d'un trait : leur variante « Outline » est un symbole évidé, moins lisible.
        if (Strokes.Contains(k)) return k;
        // Plusieurs noms peuvent désigner le même pictogramme : un nom « Outline » suffit à le garder tel quel.
        var names = Names.Value[k];
        if (names.Any(n => n.EndsWith("Outline", StringComparison.Ordinal))) return k;
        foreach (var name in names)
            if (Enum.TryParse<MaterialIconKind>(name + "Outline", out var outline)) return outline;
        return k;
    });

    private static readonly Lazy<ILookup<MaterialIconKind, string>> Names =
        new(() => Enum.GetNames<MaterialIconKind>().ToLookup(Enum.Parse<MaterialIconKind>));

    /// <summary>Convertisseur pour les liaisons : <c>Kind="{Binding Icon, Converter={x:Static infra:Icons.LineConverter}}"</c>.</summary>
    public static readonly IValueConverter LineConverter = new FuncValueConverter<MaterialIconKind, MaterialIconKind>(Line);

    // ---------- Icône de tête d'un bouton d'action : infra:Icons.Lead="Refresh" ----------
    public static readonly AttachedProperty<MaterialIconKind?> LeadProperty =
        AvaloniaProperty.RegisterAttached<Button, MaterialIconKind?>("Lead", typeof(Icons));

    public static MaterialIconKind? GetLead(Button button) => button.GetValue(LeadProperty);
    public static void SetLead(Button button, MaterialIconKind? value) => button.SetValue(LeadProperty, value);

    private static readonly ConcurrentDictionary<MaterialIconKind, FuncDataTemplate<object?>> LeadTemplates = new();

    // Seul le modèle est partagé : chaque bouton reçoit ses propres contrôles.
    public static FuncDataTemplate<object?> LeadTemplate(MaterialIconKind kind) =>
        LeadTemplates.GetOrAdd(Line(kind), static glyph => new FuncDataTemplate<object?>((content, _) =>
        {
            var text = new TextBlock
            {
                Text = content?.ToString(), VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                MinWidth = 0,
            };
            var icon = new MaterialIcon
            {
                Kind = glyph, Width = ButtonSize, Height = ButtonSize,
                VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
            };
            // Une colonne fixe pour le pictogramme et le reste pour le texte :
            // contrairement à une pile horizontale, le libellé reçoit une largeur finie.
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 7, HorizontalAlignment = HorizontalAlignment.Center,
            };
            Grid.SetColumn(text, 1);
            grid.Children.Add(icon);
            grid.Children.Add(text);
            return grid;
        }, supportsRecycling: false));

    static Icons()
    {
        LeadProperty.Changed.AddClassHandler<Button>((button, e) =>
        {
            if (e.NewValue is not MaterialIconKind kind) { button.ContentTemplate = null; return; }
            var template = LeadTemplate(kind);
            if (!ReferenceEquals(button.ContentTemplate, template)) button.ContentTemplate = template;
        });
    }

    /// <summary>Convertit un nom de pictogramme pour les tests et le code (« Refresh » → variante au trait).</summary>
    public static MaterialIconKind Parse(string name) =>
        Line(Enum.Parse<MaterialIconKind>(name, ignoreCase: false));
}
