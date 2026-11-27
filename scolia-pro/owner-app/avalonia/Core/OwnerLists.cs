using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace JPServices.Owner;

public sealed partial class OwnerShell
{
    /// <summary>Recherche locale sur tout le catalogue ; 40 cartes à la fois pour garder le téléphone réactif.</summary>
    private Control SearchableList(IReadOnlyList<JsonElement> records, string key, string label,
        Func<JsonElement, string> terms, Func<JsonElement, Control> render, string empty,
        Func<JsonElement, string>? group = null)
    {
        var pane = new StackPanel { Spacing = 12 };
        pane.Children.Add(Text(label, 12, true));
        var input = Field(key, label, hint: "Nom, état ou mot-clé…");
        pane.Children.Add(input);
        var count = Text("", 13, color: OwnerPalette.Muted);
        pane.Children.Add(count);
        var rows = new StackPanel { Spacing = 12 };
        pane.Children.Add(rows);
        var shown = 40;
        var more = Button("Afficher la suite", () => { }, subtle: true);
        more.HorizontalAlignment = HorizontalAlignment.Stretch;
        pane.Children.Add(more);
        void Fill()
        {
            rows.Children.Clear();
            var matches = records.Where(r => OwnerText.Matches(terms(r), input.Text)).ToArray();
            count.Text = $"{matches.Length} résultat{(matches.Length == 1 ? "" : "s")}";
            string? lastGroup = null;
            foreach (var record in matches.Take(shown))
            {
                if (group is not null && group(record) is { } heading && heading != lastGroup)
                {
                    lastGroup = heading;
                    rows.Children.Add(Text(heading, 17, true));
                }
                rows.Children.Add(render(record));
            }
            if (matches.Length == 0 && !_loading)
                rows.Children.Add(Empty(records.Count == 0 ? empty : "Aucun résultat pour cette recherche."));
            more.IsVisible = matches.Length > shown;
            more.Content = ButtonLabel($"Afficher {Math.Min(40, Math.Max(0, matches.Length - shown))} de plus");
        }
        input.TextChanged += (_, _) => { shown = 40; Fill(); };
        more.Click += (_, _) => { shown += 40; Fill(); };
        Fill();
        return pane;
    }
}
