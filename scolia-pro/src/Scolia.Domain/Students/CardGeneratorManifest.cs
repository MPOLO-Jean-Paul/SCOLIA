namespace Scolia.Domain.Students;

/// <summary>
/// Contrat du moteur de cartes d'accès PDF/Word réellement chargé sur le serveur.
/// Toute modification de la présentation, des dimensions, du QR ou du logo doit
/// incrémenter cet identifiant en même temps que les deux générateurs.
/// La version de SCOLIA seule n'est PAS une preuve de compatibilité.
/// </summary>
public static class CardGeneratorManifest
{
    public const string Id = "SCOLIA-CARTES-ID1-2026-11-V2";
    public const int Revision = 2;
    public const int CardsPerA4 = 8;
    public const string ExportFormats = "pdf,docx";
}
