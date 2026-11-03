using Scolia.Bureau;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>
/// Un processus serveur lancé par SCOLIA ne doit pas disparaître parce que la
/// fenêtre est réduite ou verrouillée. Seul un arrêt voulu / un changement
/// de mode désactive la reprise ; les tests sont indépendants de Windows.
/// </summary>
public sealed class ServerBackgroundTests
{
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, true, false)]
    public void Le_surveilleur_relance_seulement_un_serveur_attendu_mais_arrete(
        bool configuredAsServer, bool intentionalStop, bool processAlive, bool expected)
    {
        Assert.Equal(expected, ServerProcess.ShouldRestore(configuredAsServer, intentionalStop, processAlive));
    }

    [Fact]
    public void Mode_serveur_et_fenetre_reduite_n_impliquent_jamais_un_arret()
    {
        // Le watchdog ne lit aucun état de fenêtre ; l'hébergement continue
        // sous le contrôle du processus (ou du service Windows) tant qu'il est attendu.
        Assert.False(ServerProcess.ShouldRestore(configuredAsServer: true,
            intentionalStop: false, processAlive: true));
        Assert.True(ServerProcess.ShouldRestore(configuredAsServer: true,
            intentionalStop: false, processAlive: false));
    }
}
