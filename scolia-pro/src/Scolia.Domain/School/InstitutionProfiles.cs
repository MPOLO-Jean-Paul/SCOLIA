namespace Scolia.Domain.School;

/// <summary>
/// Valeurs persistées des profils. ItmIm est la valeur historique : ne jamais
/// changer sa chaîne en base. ItmIem est un alias accepté à l'entrée uniquement.
/// </summary>
public static class InstitutionProfiles
{
    public const string ItmIm = "ItmIm";
    public const string ItmIem = "ItmIem";
    public const string Maternelle = "Maternelle";
    public const string Primaire = "Primaire";
    public const string Secondaire = "Secondaire";
    public const string ComplexeScolaire = "ComplexeScolaire";
    public const string SuperieurLmd = "SuperieurLmd";

    public static bool IsKnown(string? value) =>
        value is ItmIm or ItmIem or Maternelle or Primaire or Secondaire or ComplexeScolaire or SuperieurLmd;

    /// <summary>Normalise les anciennes appellations vers la valeur persistée historique.</summary>
    public static string Canonicalize(string? value) =>
        string.IsNullOrWhiteSpace(value) || value is ItmIm or ItmIem ? ItmIm : value;

    // Les profils connus sont activables. Les tarifs publics et les documents
    // officiels restent soumis à des validations indépendantes du profil.
    public static bool IsEnabled(string? value) => IsKnown(value);

    public static bool UsesSchoolCycles(string? value) =>
        Canonicalize(value) is Maternelle or Primaire or Secondaire or ComplexeScolaire;
}
