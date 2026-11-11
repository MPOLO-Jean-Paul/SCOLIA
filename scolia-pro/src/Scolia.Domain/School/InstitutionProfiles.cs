namespace Scolia.Domain.School;

/// <summary>
/// Valeurs persistées des profils. ItmIm est la valeur historique : ne jamais
/// changer sa chaîne en base. ItmIem est un alias accepté à l'entrée uniquement.
/// </summary>
public static class InstitutionProfiles
{
    public const string ItmIm = "ItmIm";
    public const string ItmIem = "ItmIem";
    public const string Primaire = "Primaire";
    public const string Secondaire = "Secondaire";
    public const string SuperieurLmd = "SuperieurLmd";

    public static bool IsKnown(string? value) =>
        value is ItmIm or ItmIem or Primaire or Secondaire or SuperieurLmd;

    /// <summary>Normalise les anciennes appellations vers la valeur persistée historique.</summary>
    public static string Canonicalize(string? value) =>
        string.IsNullOrWhiteSpace(value) || value is ItmIm or ItmIem ? ItmIm : value;

    // Phase 1/2 : moteur ITM opérationnel. Autres moteurs non encore activés.
    public static bool IsEnabled(string? value) =>
        IsKnown(value) && Canonicalize(value) == ItmIm;
}
