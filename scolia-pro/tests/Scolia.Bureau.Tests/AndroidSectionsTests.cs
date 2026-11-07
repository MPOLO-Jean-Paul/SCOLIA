using System.Xml.Linq;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>La gestion des sections reste directement accessible et lisible sur téléphone.</summary>
public sealed class AndroidSectionsTests
{
    private static XDocument SettingsView()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln")))
            dir = dir.Parent;
        var root = dir?.FullName ?? throw new DirectoryNotFoundException("Scolia.sln introuvable");
        return XDocument.Load(Path.Combine(root, "src", "Scolia.Bureau", "Views", "SettingsView.axaml"));
    }

    [Fact]
    public void Android_offre_ajout_et_edition_des_sections_sans_menu_cache()
    {
        var document = SettingsView();
        var buttons = document.Descendants().Where(node => node.Name.LocalName == "Button").ToArray();
        Assert.Contains(buttons, node => node.Attributes().Any(attr =>
            attr.Name.LocalName == "Command" && attr.Value.Contains("CreateSection", StringComparison.Ordinal)));
        Assert.Contains(buttons, node => node.Attributes().Any(attr =>
            attr.Name.LocalName == "Command" && attr.Value.Contains("EditSectionOnPhone", StringComparison.Ordinal))
            && node.Attributes().Any(attr => attr.Name.LocalName == "CommandParameter"));
        Assert.Contains(document.Descendants(), node => node.Name.LocalName == "ItemsControl"
            && node.Attributes().Any(attr => attr.Name.LocalName == "ItemsSource"
                && attr.Value.Contains("SectionRows", StringComparison.Ordinal)));
        Assert.Contains(document.Descendants(), node => node.Name.LocalName == "DataGrid"
            && node.Attributes().Any(attr => attr.Name.LocalName == "IsVisible"
                && attr.Value.Contains("NotPhone", StringComparison.Ordinal))
            && node.Attributes().Any(attr => attr.Name.LocalName == "ItemsSource"
                && attr.Value.Contains("SectionRows", StringComparison.Ordinal)));
    }

    [Fact]
    public void Formulaire_section_refuse_code_et_designation_trop_longs()
    {
        var form = new FormViewModel("Nouvelle section");
        form.Add("code", "Code", value: new string('A', 13), required: true);
        form.Add("nom", "Désignation", value: "Sciences infirmières", required: true);

        Assert.False(SettingsPage.ValidateSectionForm(form));
        Assert.NotNull(form.Find("code").Error);

        form.Find("code").Text = "INF";
        form.Find("nom").Text = new string('B', 121);
        Assert.False(SettingsPage.ValidateSectionForm(form));
        Assert.NotNull(form.Find("nom").Error);

        form.Find("nom").Text = "Sciences infirmières";
        Assert.True(SettingsPage.ValidateSectionForm(form));
    }

    [Fact]
    public void Les_commandes_tactiles_sont_instanciees_sans_charger_les_donnees()
    {
        using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
        var shell = new ShellViewModel(api, new Preferences()) { Phone = true };
        var page = new SettingsPage(shell);
        Assert.NotNull(page.CreateSection);
        Assert.NotNull(page.EditSectionOnPhone);
        Assert.True(page.HasNoSections);
        Assert.False(page.HasSectionRows);
    }
}
