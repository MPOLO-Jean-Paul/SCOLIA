using Material.Icons;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Une commande mobile et une seule destination métier.</summary>
public sealed class PhoneMenuBlocksTests
{
    private static RibbonCommandVm Command(string label) =>
        new(label, MaterialIconKind.Cog, string.Empty, new RelayCommand(() => { }), false, "ToneBlue");

    [Fact]
    public void Rubriques_et_commandes_sont_distribuees_sans_bloc_redondant()
    {
        NavGroupVm[] navigation =
        [
            new("Données", []), new("Secrétariat", []), new("Scolarité", []),
            new("Finances", []), new("Administration", []), new("Paramètres", []),
        ];
        PhoneToolGroupVm[] commands =
        [
            new("Données", [Command("Outils données")]),
            new("Sauvegardes", [Command("Sauvegarder")]),
            new("Contrôle", [Command("Vérifier la base")]),
            new("Effacement", [Command("Effacer des dossiers")]),
            new("Exports", [Command("Situation (PDF)")]),
            new("Secrétariat", [Command("Nouvelle inscription")]),
            new("Scolarité", [Command("Nouveau cours")]),
            new("Finances", [Command("Nouvelle dépense")]),
            new("Administration", [Command("Nouvel utilisateur")]),
            new("Paramètres", [Command("Thème")]),
            new("Aide", [Command("À propos")]),
        ];
        var blocks = PhoneMenuBlockVm.Build(navigation, commands);
        Assert.Equal(navigation.Length, blocks.Sum(b => b.NavigationGroups.Count));
        Assert.All(navigation, nav => Assert.Single(blocks, b => b.NavigationGroups.Contains(nav)));

        Assert.DoesNotContain(blocks, b => b.Title is "AFFICHAGE" or "AUTRES OUTILS");
        var visible = blocks.SelectMany(b => b.ToolGroups.SelectMany(g => g.Commands))
            .Concat(navigation.SelectMany(g => g.SupplementalCommands)).ToArray();
        var original = commands.SelectMany(g => g.Commands).ToArray();
        Assert.Equal(original.Length, visible.Length);
        Assert.Equal(original.Length, visible.Select(c => c.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(original, cmd => Assert.Single(visible, rendered => ReferenceEquals(cmd, rendered)));
    }

    [Fact]
    public void Affichage_est_dans_parametres_et_pas_dans_un_bloc_separe()
    {
        var settings = new NavGroupVm("Paramètres", []);
        var appearance = Command("Thème");
        var color = Command("Couleur d'accent");
        var backups = Command("Sauvegarder");
        var blocks = PhoneMenuBlockVm.Build([settings],
        [
            new("Affichage", [appearance, color]),
            new("Paramètres", [Command("Nouvelle année")]),
            new("Sauvegardes", [backups])
        ]);

        Assert.DoesNotContain(blocks, b => b.Title == "AFFICHAGE");
        Assert.Equal(3, settings.SupplementalCommands.Count);
        Assert.DoesNotContain(blocks.SelectMany(b => b.ToolGroups),
            g => g.Title is "Affichage" or "Paramètres");
        Assert.Contains(blocks, b => b.Title == "DONNÉES ET SAUVEGARDES"
            && b.ToolGroups.Any(g => g.Commands.Contains(backups)));
    }
}
