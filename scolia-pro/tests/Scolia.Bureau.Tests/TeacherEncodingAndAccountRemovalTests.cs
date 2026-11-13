using System.Xml.Linq;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Contrats natifs des nouveaux parcours enseignant, sur Android et Windows.</summary>
public sealed class TeacherEncodingAndAccountRemovalTests
{
    private static string Source(string relative)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Scolia.sln")))
            folder = folder.Parent;
        return Path.Combine(folder?.FullName ?? throw new DirectoryNotFoundException(),
            "src", "Scolia.Bureau", relative);
    }

    private static bool Bound(XElement element, string attribute, string fragment) =>
        element.Attributes().Any(a => a.Name.LocalName == attribute &&
            a.Value.Contains(fragment, StringComparison.Ordinal));

    [Fact]
    public void La_cote_enseignant_reste_verrouillee_jusqu_a_validation_du_code()
    {
        var xaml = XDocument.Load(Source("Views/MarksView.axaml"));
        Assert.Contains(xaml.Descendants(), e => e.Name.LocalName == "TextBox" &&
            Bound(e, "Text", "CodeSearch"));
        Assert.Contains(xaml.Descendants(), e => e.Name.LocalName == "ItemsControl" &&
            Bound(e, "ItemsSource", "CodeSuggestions"));
        Assert.Contains(xaml.Descendants(), e => e.Name.LocalName == "TextBox" &&
            Bound(e, "Text", "ScoreInput") && Bound(e, "IsEnabled", "CanEnterScore"));
        Assert.Contains(xaml.Descendants(), e => e.Name.LocalName == "Button" &&
            Bound(e, "Command", "SaveAnonymousScore") && Bound(e, "IsEnabled", "CanSaveAnonymousScore"));
        Assert.Contains(xaml.Descendants(), e => e.Name.LocalName == "TextBlock" &&
            Bound(e, "Text", "CodeError"));
        Assert.Contains(xaml.Descendants(), e => e.Name.LocalName == "TextBlock" &&
            Bound(e, "Text", "MaxScoreLabel"));
        var implementation = File.ReadAllText(Source("ViewModels/Deliberation.cs"));
        Assert.Contains("AnonymousCodeRules.Normalize(CodeSearch)", implementation, StringComparison.Ordinal);
        Assert.Contains("api/deliberation/codes/verification", implementation, StringComparison.Ordinal);
        Assert.Contains("SelectedMaximum", implementation, StringComparison.Ordinal);
    }

    [Fact]
    public void Les_retraits_restent_visibles_sur_la_connexion_native_et_dans_la_gestion_des_comptes()
    {
        var login = XDocument.Load(Source("Views/LoginView.axaml"));
        Assert.True(login.Descendants().Count(e => e.Name.LocalName == "Button" &&
            Bound(e, "Command", "ForgetRememberedAccount")) >= 2);
        Assert.True(login.Descendants().Count(e => e.Name.LocalName == "Button" &&
            Bound(e, "Command", "ForgetSavedServer") && Bound(e, "IsVisible", "CanForgetSavedServer")) >= 2);
        var settings = File.ReadAllText(Source("ViewModels/Sidebar.cs"));
        Assert.Contains("Item(\"Encodage\"", settings, StringComparison.Ordinal);
        var accountManagement = File.ReadAllText(Source("ViewModels/Administration.cs"));
        Assert.Contains("RevokeServerAccountAsync", accountManagement, StringComparison.Ordinal);
        Assert.Contains("Shell.IsServerComputer", accountManagement, StringComparison.Ordinal);
    }
}
