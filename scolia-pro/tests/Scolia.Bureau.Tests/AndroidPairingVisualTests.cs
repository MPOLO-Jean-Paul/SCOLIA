using System.Xml.Linq;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Le parcours QR vers l'accueil doit rester lisible sur Android.</summary>
public sealed class AndroidPairingVisualTests
{
    private static XDocument View(string view)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Scolia.sln")))
            directory = directory.Parent;
        var root = directory?.FullName ?? throw new DirectoryNotFoundException("Scolia.sln introuvable");
        return XDocument.Load(Path.Combine(root, "src", "Scolia.Bureau", "Views", view));
    }

    [Fact]
    public void Le_parcours_qr_emploie_un_ecran_de_verification_isole()
    {
        var view = View("ConnectionView.axaml");
        Assert.Contains(view.Descendants(), element => element.Name.LocalName == "ScrollViewer"
            && element.Attributes().Any(a => a.Name.LocalName == "IsVisible"
                && a.Value.Contains("PairingInProgress", StringComparison.Ordinal)
                && !a.Value.Contains('!')));
        Assert.Contains(view.Descendants(), element => element.Name.LocalName == "TextBlock"
            && element.Attributes().Any(a => a.Name.LocalName == "Text" && a.Value == "Vérification de l’appareil"));
        Assert.Contains(view.Descendants(), element => element.Name.LocalName == "ProgressBar"
            && element.Attributes().Any(a => a.Name.LocalName == "IsIndeterminate" && a.Value == "True"));
    }

    [Fact]
    public void Ouverture_apres_qr_affiche_un_contexte_et_une_progression_dans_un_conteneur_adaptatif()
    {
        var view = View("MainView.axaml");
        var host = Assert.Single(view.Descendants(), element => element.Name.LocalName == "Grid"
            && element.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "OpeningHost"));
        Assert.Contains(host.Descendants(), element => element.Name.LocalName == "TextBlock"
            && element.Attributes().Any(a => a.Name.LocalName == "Text"
                && a.Value.Contains("OpeningContext", StringComparison.Ordinal)));
        Assert.Contains(host.Descendants(), element => element.Name.LocalName == "ProgressBar"
            && element.Attributes().Any(a => a.Name.LocalName == "Value"
                && a.Value.Contains("OpeningProgress", StringComparison.Ordinal)));
        Assert.Contains(host.Descendants(), element => element.Name.LocalName == "ScrollViewer");
        Assert.Contains(view.Descendants(), element => element.Name.LocalName == "ContentControl"
            && element.Attributes().Any(a => a.Name.LocalName == "IsVisible"
                && a.Value.Contains("ShowConnection", StringComparison.Ordinal)));
        Assert.Contains(host.Descendants(), element => element.Name.LocalName == "Border"
            && element.Attributes().Any(a => a.Name.LocalName == "Classes"
                && a.Value.Contains("openingcard", StringComparison.Ordinal)));
    }

    [Fact]
    public void Formulaires_longs_sont_adaptes_aux_puces_et_aux_champs_tactiles()
    {
        var view = View("FormView.axaml");
        Assert.Contains(view.Descendants(), element => element.Name.LocalName == "Style"
            && element.Attributes().Any(a => a.Name.LocalName == "Selector"
                && a.Value.Contains("LayoutTransformControl.phone Border.sheet TextBox.w11", StringComparison.Ordinal)));
        Assert.Contains(view.Descendants(), element => element.Name.LocalName == "Panel"
            && element.Attributes().Any(a => a.Name.LocalName == "Classes" && a.Value.Contains("formfooter", StringComparison.Ordinal)));
    }
}
