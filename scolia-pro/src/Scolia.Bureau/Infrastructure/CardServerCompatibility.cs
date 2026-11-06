namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Les cartes sont rendues dans Scolia.Api, pas dans l'interface mobile.
/// Ne pas promettre le nouveau modèle si l'APK est récent mais le serveur l'est moins.
/// </summary>
public static class CardServerCompatibility
{
    private static readonly Version RevisedLayout = new(1, 2, 31);

    public static bool HasRevisedLayout(string? serverVersion) =>
        Version.TryParse(serverVersion, out var installed) && installed >= RevisedLayout;

    public static string OutdatedServerMessage(string? serverVersion)
    {
        var actual = string.IsNullOrWhiteSpace(serverVersion) ? "inconnue" : serverVersion.Trim();
        return $"Le serveur SCOLIA (version {actual}) utilise un ancien générateur de cartes. " +
            "Mettez à jour l'appareil qui héberge les données de l'établissement vers la version 1.2.31 ou ultérieure, " +
            "redémarrez son service SCOLIA, puis exportez de nouvelles cartes PDF ou Word. " +
            "Mettre à jour uniquement ce téléphone ne suffit pas.";
    }
}
