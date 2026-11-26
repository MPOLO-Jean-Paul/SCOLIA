using System.Xml.Linq;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class NativeDesignPolishTests
{
    private static XDocument Read(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln")))
            dir = dir.Parent;
        return XDocument.Load(Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException(),
            "src", "Scolia.Bureau", relative));
    }

    [Fact]
    public void Les_champs_metier_ont_un_nom_accessible_et_un_etat_erreur()
    {
        var fields = Read("Views/FormView.axaml").Descendants()
            .Where(x => x.Name.LocalName is "TextBox" or "ComboBox" or "CalendarDatePicker")
            .Where(x => (string?)x.Attribute("Classes") == "w11").ToArray();
        Assert.NotEmpty(fields);
        Assert.All(fields, field =>
        {
            Assert.Equal("{Binding Caption}", (string?)field.Attribute("AutomationProperties.Name"));
            Assert.Equal("{Binding HasError}", (string?)field.Attribute("Classes.invalid"));
        });
    }

    [Fact]
    public void Les_entetes_de_tableau_tronques_revelent_le_libelle_complet()
    {
        var style = Assert.Single(Read("Themes/Controls.axaml").Descendants(),
            x => x.Name.LocalName == "Style" &&
                 (string?)x.Attribute("Selector") == "DataGridColumnHeader TextBlock");
        Assert.Contains(style.Elements(), x => (string?)x.Attribute("Property") == "TextTrimming" &&
            (string?)x.Attribute("Value") == "CharacterEllipsis");
        Assert.Contains(style.Elements(), x => (string?)x.Attribute("Property") == "ToolTip.Tip");
    }
}
