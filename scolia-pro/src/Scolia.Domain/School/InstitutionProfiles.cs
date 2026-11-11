namespace Scolia.Domain.School;

/// <summary>
/// Profils pédagogiques stables. L'ancien code interne ItmIm est accepté pour
/// permettre la lecture des bases de démonstration créées avant la correction
/// du libellé ITM/IEM ; toutes les nouvelles écritures utilisent ItmIem.
/// </summary>
public static class InstitutionProfiles
{
    public const string ItmIem = "ItmIem";
    public const string LegacyItmIm = "ItmIm";
    public const string Primaire = "Primaire";
    public const string Secondaire = "Secondaire";
    public const string SuperieurLmd = "SuperieurLmd";

    public static bool IsKnown(string? value) =>
        value is ItmIem or LegacyItmIm or Primaire or Secondaire or SuperieurLmd;

    public static string Canonicalize(string? value) =>
        value is null or LegacyItmIm ? ItmIem : value;

    // Aucun moteur hors ITM/IEM ne sera activé avant ses tests de conformité.
    public static bool IsEnabled(string? value) =>
        value is ItmIem or LegacyItmIm;
}
