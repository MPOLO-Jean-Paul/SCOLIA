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
            new NavGroupVm("Données", Array.Empty<NavItemVm>()),
            new NavGroupVm("Scolarité", Array.Empty<NavItemVm>()),
            new NavGroupVm("Finances", Array.Empty<NavItemVm>()),
            new NavGroupVm("Paramètres", Array.Empty<NavItemVm>()),
        };
        var blocks = Sidebar.Blocks(groups);
        Assert.Equal(groups.Length, blocks.SelectMany(x => x.Groups).Distinct().Count());
        Assert.Equal("VUE D’ENSEMBLE", blocks[0].Title);
        Assert.Equal("GESTION SCOLAIRE", blocks[1].Title);
        Assert.Equal("GESTION FINANCIÈRE", blocks[2].Title);
        Assert.Equal("ORGANISATION ET RÉGLAGES", blocks[3].Title);
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
