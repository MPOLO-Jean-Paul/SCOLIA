using System.Reflection;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>
/// Régression signalée dans l'APK 1.2.68 : la déconnexion du compte ne doit
/// ni terminer l'activité Android ni révoquer l'autorisation de jumelage.
/// </summary>
public sealed class LogoutStabilityTests
{
    [Fact]
    public async Task Deconnexion_sans_serveur_revient_a_la_connexion_sans_planter()
    {
        // Un serveur inaccessible reproduit le cas où le clic était fatal
        // (l'ancienne méthode ne gérait que HttpRequestException).
        using var api = new ScoliaClient(new Uri("http://127.0.0.1:1/"));
        var shell = new ShellViewModel(api, new Preferences());
        shell.ApplyInstallationStatus(configured: true);

        var session = new SessionDto(Guid.NewGuid(), "enseignant", "Enseignant test",
            "Enseignant", "Enseignant", [], false, "Institut test",
            null, null, "FC", "243");
        typeof(ShellViewModel).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(shell, session);

        Assert.True(shell.IsLoggedIn);
        await shell.LogoutAsync();
        Assert.False(shell.IsLoggedIn);
        Assert.True(shell.ShowLogin);
        Assert.False(shell.ShowApp);
        // Deuxième clic ou retour depuis une vue supprimée : aucune exception.
        await shell.LogoutAsync();
        Assert.True(shell.ShowLogin);
    }

    [Fact]
    public void Deconnexion_individuelle_ne_supprime_pas_le_jumelage_du_telephone()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Scolia.sln")))
            folder = folder.Parent;
        var source = File.ReadAllText(Path.Combine(folder!.FullName,
            "src", "Scolia.Bureau", "ViewModels", "Shell.cs"));
        var start = source.IndexOf("public async Task LogoutAsync()", StringComparison.Ordinal);
        var end = source.IndexOf("private void CloseLocalSession()", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var logout = source[start..end];
        Assert.Contains("finally", logout, StringComparison.Ordinal);
        Assert.Contains("CloseLocalSession()", logout, StringComparison.Ordinal);
        Assert.Contains("Sync.Forget()", logout, StringComparison.Ordinal);
        Assert.DoesNotContain("DevicePairingStore.Forget", logout, StringComparison.Ordinal);
    }
}
