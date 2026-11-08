using System.Xml.Linq;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>
/// Anti-régression : l'écran AVANT la connexion sur Android n'est pas un
/// ScrollViewer masqué derrière des accordéons. Les méthodes sont des pages.
/// </summary>
public sealed class AndroidPreloginNoScrollTests
{
    private static string Root()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Scolia.sln")))
            folder = folder.Parent;
        return Path.Combine(folder?.FullName ?? throw new DirectoryNotFoundException(),
            "src", "Scolia.Bureau");
    }

    private static XDocument View(string path) =>
        XDocument.Load(Path.Combine(Root(), "Views", path));

    private static bool Attr(XElement el, string name, string value) =>
        el.Attributes().Any(a => a.Name.LocalName == name && a.Value == value);

    [Fact]
    public void Avant_connexion_android_n_emploie_aucun_defilement_de_page()
    {
        var view = View("Phone/ConnectionCompactView.axaml");
        Assert.DoesNotContain(view.Descendants(), e => e.Name.LocalName == "ScrollViewer");
        Assert.Contains(view.Descendants(), e => Attr(e, "Name", "CompactRoot") &&
            Attr(e, "RowDefinitions", "Auto,*,Auto"));
        foreach (var name in new[] { "QrPage", "CodePage", "WifiPage", "ServerPage",
            "AddressPage", "DetailsPage" })
            Assert.Contains(view.Descendants(), e => Attr(e, "Name", name));
    }

    [Fact]
    public void Navigation_automatique_seulement_sur_petit_ecran()
    {
        var code = File.ReadAllText(Path.Combine(Root(), "Views", "Phone", "ConnectionCompactView.axaml.cs"));
        Assert.Contains("size.Width < 390 || size.Height < 700", code);
        Assert.Contains("CompactFooter.IsVisible = compactNavigation", code);
        Assert.Contains("InlineMethods.IsVisible = !compactNavigation", code);
        var view = View("Phone/ConnectionCompactView.axaml");
        foreach (var name in new[] { "CompactNavbar", "InlineMethods",
            "InlineCodeButton", "InlineWifiButton", "ReturnToQrFromCode", "ReturnToQrFromWifi" })
            Assert.Contains(view.Descendants(), e => Attr(e, "Name", name));
    }

    [Fact]
    public void Toutes_les_methodes_de_jumelage_restent_accessibles()
    {
        var view = View("Phone/ConnectionCompactView.axaml");
        foreach (var command in new[] { "ScanCode", "ImportQrImage", "JoinInternet",
            "Search", "ConnectAddress", "UseThisComputer" })
            Assert.Contains(view.Descendants(), e => e.Name.LocalName == "Button" &&
                Attr(e, "Command", "{Binding " + command + "}"));
        foreach (var name in new[] { "TabQr", "TabCode", "TabWifi",
            "ManualAddressButton", "ChooseServerButton" })
            Assert.Contains(view.Descendants(), e => e.Name.LocalName == "Button" && Attr(e, "Name", name));
    }

    [Fact]
    public void Le_parcours_mobile_n_utilise_pas_l_ancienne_page_deroulante()
    {
        var code = File.ReadAllText(Path.Combine(Root(), "Views", "ConnectionView.axaml.cs"));
        Assert.Contains("IosConnectionScreen.IsVisible = false", code);
        Assert.Contains("CompactConnectionThemeScope.IsVisible = true", code);
        var view = View("ConnectionView.axaml");
        Assert.Contains(view.Descendants(), e => e.Name.LocalName == "ConnectionCompactView");
    }

    [Fact]
    public void La_liste_de_serveurs_est_une_selection_pas_une_liste_verticale_sans_limite()
    {
        var view = View("Phone/ConnectionCompactView.axaml");
        Assert.Contains(view.Descendants(), e => e.Name.LocalName == "ComboBox"
            && Attr(e, "ItemsSource", "{Binding Servers}"));
        Assert.DoesNotContain(view.Descendants(), e => e.Name.LocalName == "ItemsControl"
            && Attr(e, "ItemsSource", "{Binding Servers}"));
    }

    [Fact]
    public void Les_etats_de_liaison_gardent_leurs_verifications_de_securite()
    {
        var view = View("Phone/ConnectionCompactView.axaml");
        Assert.Contains(view.Descendants(), e => e.Name.LocalName == "ProgressBar" &&
            Attr(e, "IsIndeterminate", "True"));
        Assert.Contains(view.Descendants(), e => Attr(e, "Text", "{Binding Message}"));
        var model = File.ReadAllText(Path.Combine(Root(), "ViewModels", "Connection.cs"));
        Assert.Contains("JoinCodeAsync", model);
        Assert.Contains("PairingFlow.JoinAsync", model);
    }
}
