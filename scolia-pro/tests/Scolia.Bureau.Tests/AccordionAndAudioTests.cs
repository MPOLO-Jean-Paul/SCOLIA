using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

public class AccordionAndAudioTests
{
    [Fact]
    public void Blocs_navigation_conservent_les_rubriques_sans_doublons()
    {
        var groups = new[]
        {
            new NavGroupVm("", Array.Empty<NavItemVm>()),
            new NavGroupVm("Pilotage", Array.Empty<NavItemVm>()),
            new NavGroupVm("Secrétariat", Array.Empty<NavItemVm>()),
            new NavGroupVm("Scolarité", Array.Empty<NavItemVm>()),
            new NavGroupVm("Finances", Array.Empty<NavItemVm>()),
            new NavGroupVm("Personnel", Array.Empty<NavItemVm>()),
            new NavGroupVm("Paramètres", Array.Empty<NavItemVm>()),
            new NavGroupVm("Données", Array.Empty<NavItemVm>()),
        };
        var blocks = Sidebar.Blocks(groups);
        Assert.Equal(groups.Length, blocks.SelectMany(x => x.Groups).Distinct().Count());
        Assert.Equal(["VUE D’ENSEMBLE", "GESTION SCOLAIRE", "GESTION FINANCIÈRE", "ORGANISATION", "RÉGLAGES ET DONNÉES"],
            blocks.Select(b => b.Title).ToArray());
        // Pilotage avec l'accueil ; sauvegardes et effacement avec les paramètres, jamais avec les chiffres.
        Assert.Contains(blocks[0].Groups, g => g.Title == "Pilotage");
        Assert.Contains(blocks[4].Groups, g => g.Title == "Données");
        Assert.DoesNotContain(blocks, b => b.Title == "AUTRES OUTILS");
    }

    [Fact]
    public void Gestion_des_deliberations_rassemble_enseignants_cours_et_jury()
    {
        var groups = new[]
        {
            new NavGroupVm("", Array.Empty<NavItemVm>()),
            new NavGroupVm("Secrétariat", Array.Empty<NavItemVm>()),
            new NavGroupVm("Scolarité", Array.Empty<NavItemVm>()),
            new NavGroupVm("Enseignants et cours", Array.Empty<NavItemVm>()),
            new NavGroupVm("Délibération", Array.Empty<NavItemVm>()),
            new NavGroupVm("Finances", Array.Empty<NavItemVm>()),
        };
        var blocks = Sidebar.Blocks(groups);
        Assert.Equal(["VUE D’ENSEMBLE", "GESTION SCOLAIRE", "GESTION DES DÉLIBÉRATIONS", "GESTION FINANCIÈRE"], blocks.Select(b => b.Title).ToArray());
        Assert.Equal(["Enseignants et cours", "Délibération"], blocks[2].Groups.Select(g => g.Title).ToArray());
        Assert.DoesNotContain(blocks[1].Groups, g => g.Title == "Délibération");

        // L'enseignant : son accueil puis son espace, dans leur propre bloc.
        var teacher = Sidebar.Blocks([new NavGroupVm("", Array.Empty<NavItemVm>()), new NavGroupVm("Espace enseignant", Array.Empty<NavItemVm>())]);
        Assert.Equal(["VUE D’ENSEMBLE", "ESPACE ENSEIGNANT"], teacher.Select(b => b.Title).ToArray());
    }

    [Fact]
    public void Un_accordeon_de_navigation_ferme_les_autres()
    {
        var groups = new List<NavGroupVm>();
        void Single(NavGroupVm chosen)
        {
            var expand = !chosen.IsExpanded;
            foreach (var group in groups) group.IsExpanded = false;
            chosen.IsExpanded = expand;
        }
        groups.Add(new NavGroupVm("Scolarité", Array.Empty<NavItemVm>(), toggleGroup: Single));
        groups.Add(new NavGroupVm("Finances", Array.Empty<NavItemVm>(), toggleGroup: Single));
        groups[0].Toggle.Execute(null);
        Assert.True(groups[0].IsExpanded);
        groups[1].Toggle.Execute(null);
        Assert.False(groups[0].IsExpanded);
        Assert.True(groups[1].IsExpanded);
        groups[1].Toggle.Execute(null);
        Assert.All(groups, g => Assert.False(g.IsExpanded));
    }

    [Fact]
    public void Commandes_mobiles_sont_regroupees_par_blocs()
    {
        var tools = new[]
        {
            new PhoneToolGroupVm("Scolarité", Array.Empty<RibbonCommandVm>()),
            new PhoneToolGroupVm("Paiements", Array.Empty<RibbonCommandVm>()),
            new PhoneToolGroupVm("Paramètres", Array.Empty<RibbonCommandVm>()),
        };
        var blocks = PhoneToolBlockVm.Organize(tools);
        Assert.Equal(3, blocks.Count);
        Assert.Equal(3, blocks.Sum(x => x.Groups.Count));
    }

    [Fact]
    public void Outils_mobiles_chaque_commande_est_affichee_une_seule_fois()
    {
        var cmd = new RelayCommand(() => { });
        var duplicate = new RibbonCommandVm("Afficher", Material.Icons.MaterialIconKind.EyeOutline,
            "", cmd, false, "ToneBlue");
        var unique = new RibbonCommandVm("Thème", Material.Icons.MaterialIconKind.Palette,
            "", cmd, false, "TonePurple");
        var groups = new[]
        {
            new PhoneToolGroupVm("Administration", [duplicate]),
            new PhoneToolGroupVm("Affichage", [duplicate, unique]),
        };
        var result = PhoneToolBlockVm.UniqueGroups(groups);
        Assert.Equal(2, result.SelectMany(g => g.Commands).Count());
        Assert.Equal(2, result.SelectMany(g => g.Commands).Select(c => c.Label).Distinct().Count());
    }

    [Fact]
    public void Affichage_apparait_dans_un_seul_bloc()
    {
        var blocks = PhoneToolBlockVm.Organize([
            new PhoneToolGroupVm("Personnel", []),
            new PhoneToolGroupVm("Affichage", []),
            new PhoneToolGroupVm("Aide", [])
        ]);
        Assert.Equal("RÉGLAGES ET DONNÉES", blocks.Single(b => b.Groups.Any(g => g.Title == "Affichage")).Title);
        Assert.DoesNotContain(blocks, b => b.Title == "AFFICHAGE");
        Assert.Equal(1, blocks.SelectMany(b => b.Groups).Count(g => g.Title == "Affichage"));
    }

    [Fact]
    public void Son_mobile_transmet_un_wav_valide_au_lecteur()
    {
        var original = Platform.PlayWave;
        var preference = new Preferences { Sounds = true, SoundVolume = 60 };
        byte[]? received = null;
        try
        {
            Platform.PlayWave = bytes => received = bytes;
            Sounds.Play(preference, SoundKind.Success, force: true);
            Assert.NotNull(received);
            Assert.True(received.Length > 44);
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(received, 0, 4));
            Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(received, 8, 4));
        }
        finally { Platform.PlayWave = original; }
    }
}
