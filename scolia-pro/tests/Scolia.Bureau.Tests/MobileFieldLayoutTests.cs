using Scolia.Bureau.Infrastructure;
using System.Xml.Linq;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class MobileFieldLayoutTests
{
    [Theory]
    [InlineData(280, 1, 24, 256)]
    [InlineData(560, 2, 12, 268)]
    [InlineData(900, 3, 12, 288)]
    [InlineData(10, 1, 24, 0)]
    [InlineData(320, 0, 0, 0)]
    public void Les_champs_restent_dans_la_largeur_utile(double width, int columns, double margins, double expected)
        => Assert.Equal(expected, PhoneFields.FieldWidth(width, columns, margins));

    [Fact]
    public void Les_cinq_champs_de_connexion_reservent_la_place_des_icones()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln"))) dir = dir.Parent;
        var view = XDocument.Load(Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException(), "src", "Scolia.Bureau", "Views", "LoginView.axaml"));
        var fields = view.Descendants().Where(n => n.Name.LocalName == "Grid" && (string?)n.Attribute("Classes") == "authinput").ToArray();
        Assert.Equal(5, fields.Length);
        Assert.All(fields, grid =>
        {
            Assert.Contains(grid.Elements(), n => n.Name.LocalName == "TextBox" && ((string?)n.Attribute("Classes"))?.Contains("withicon") == true);
            Assert.Contains(grid.Elements(), n => n.Name.LocalName == "MaterialIcon" && (string?)n.Attribute("Classes") == "authinputicon");
            var field = Assert.Single(grid.Elements(), n => n.Name.LocalName == "TextBox");
            Assert.Equal(((string?)field.Attribute("Classes"))?.Contains("witheye") == true ? "46,0,54,0" : "46,0,14,0",
                (string?)field.Attribute("Padding"));
        });
    }
}
