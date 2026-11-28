using Scolia.Domain.Students;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Les cartes sont construites sur le serveur scolaire. Seul l'identifiant
/// du moteur en service permet d'assurer que PDF et Word ont le bon modèle.
/// Une version applicative seule ne garantit plus le bon générateur.
/// </summary>
public static class CardServerCompatibility
{
    public static bool SupportsCurrentGenerator(string? reportedGenerator) =>
        string.Equals(reportedGenerator, CardGeneratorManifest.Id, StringComparison.Ordinal);

    /// <summary>Compatibilité historique pour les anciens contrôles de version.</summary>
    public static bool HasRevisedLayout(string? serverVersion) =>
        Version.TryParse(serverVersion, out var installed) && installed >= new Version(1, 2, 31);

    public static string OutdatedServerMessage(string? serverVersion, string? reportedGenerator = null)
    {
        var actual = string.IsNullOrWhiteSpace(serverVersion) ? "inconnue" : serverVersion.Trim();
        var generator = string.IsNullOrWhiteSpace(reportedGenerator) ? "ancien ou non déclaré" : reportedGenerator.Trim();
        return $"Serveur SCOLIA version {actual} : générateur de cartes {generator}, " +
            $"attendu : {CardGeneratorManifest.Id}. La version de l'interface ne met pas à jour automatiquement " +
            "un service SCOLIA qui s'exécute sur un autre appareil. " +
            "Mettez à jour l'ordinateur ou le téléphone qui héberge les données, " +
            "redémarrez son service SCOLIA puis créez un nouveau PDF ou document Word. " +
            "Aucune carte ne sera produite avec l'ancien modèle.";
    }
}
