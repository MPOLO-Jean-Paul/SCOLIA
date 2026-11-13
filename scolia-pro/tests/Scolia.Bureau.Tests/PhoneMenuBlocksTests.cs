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
            new("Pilotage", []), new("Secrétariat", []), new("Scolarité", []),
            new("Finances", []), new("Personnel", []), new("Paramètres", []), new("Données", []),
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
            new("Personnel", [Command("Nouveau membre")]),
            new("Paramètres", [Command("Thème")]),
            new("Aide", [Command("À propos")]),
        ];
        var blocks = PhoneMenuBlockVm.Build(navigation, commands);
        Assert.Equal(navigation.Length, blocks.Sum(b => b.NavigationGroups.Count));
        Assert.All(navigation, nav => Assert.Single(blocks, b => b.NavigationGroups.Contains(nav)));

        Assert.DoesNotContain(blocks, b => b.Title is "AFFICHAGE" or "AUTRES OUTILS");
        // Sauvegardes, contrôle et effacement s'ajoutent à la rubrique Données : plus cinq entrées séparées.
        Assert.DoesNotContain(blocks.SelectMany(b => b.ToolGroups), g => g.Title is "Sauvegardes" or "Contrôle" or "Effacement" or "Exports");
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
        var data = new NavGroupVm("Données", []);
        var blocks = PhoneMenuBlockVm.Build([settings, data],
        [
            new("Affichage", [appearance, color]),
            new("Paramètres", [Command("Nouvelle année")]),
            new("Sauvegardes", [backups])
        ]);

        Assert.DoesNotContain(blocks, b => b.Title == "AFFICHAGE");
        Assert.Equal(3, settings.SupplementalCommands.Count);
        Assert.DoesNotContain(blocks.SelectMany(b => b.ToolGroups),
            g => g.Title is "Affichage" or "Paramètres" or "Sauvegardes");
        Assert.Contains(backups, data.SupplementalCommands);
        Assert.Single(blocks, b => b.Title == "RÉGLAGES ET DONNÉES");
    }
    [Fact]
    public void Organisation_pedagogique_rejoint_la_scolarite_sans_doublonner_les_reglages()
    {
        NavGroupVm[] navigation =
        [
            new("Secrétariat", []), new("Scolarité", []),
            new("Organisation pédagogique", []),
            new("Enseignants et cours", []), new("Délibération", []),
            new("Finances", []), new("Paramètres", []), new("Données", [])
        ];
        var blocks = Sidebar.Blocks(navigation);
        var education = Assert.Single(blocks, b => b.Title == "GESTION SCOLAIRE");
        Assert.Equal(new[] { "Secrétariat", "Scolarité", "Organisation pédagogique" },
            education.Groups.Select(group => group.Title));
        Assert.All(navigation, item =>
            Assert.Single(blocks.SelectMany(block => block.Groups), group => ReferenceEquals(item, group)));
        var settings = Assert.Single(blocks, b => b.Title == "RÉGLAGES ET DONNÉES");
        Assert.Equal(new[] { "Paramètres", "Données" }, settings.Groups.Select(group => group.Title));
        Assert.Equal(MaterialIconKind.SchoolOutline, FeatureVisuals.Icon("Organisation pédagogique"));
        Assert.Equal(MaterialIconKind.LanConnect, FeatureVisuals.Icon("Réseau et postes"));
        // Le regroupement doit être identique sur téléphone : la rubrique ne
        // doit pas se retrouver isolée sous « AUTRES OUTILS ».
        var phoneBlocks = PhoneMenuBlockVm.Build(navigation, []);
        Assert.DoesNotContain(phoneBlocks, block => block.Title == "AUTRES OUTILS");
        var mobileSchool = Assert.Single(phoneBlocks, block => block.Title == "GESTION SCOLAIRE");
        Assert.Equal(education.Groups, mobileSchool.NavigationGroups);
    }

}
