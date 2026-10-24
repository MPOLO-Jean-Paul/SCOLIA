namespace Scolia.Domain.School;

/// <summary>
/// En-tête officiel des cartes d'accès, tel qu'il figure dans la version 79. Utilisé tant que l'établissement
/// n'a pas saisi ses propres mentions dans Paramètres : chaque valeur reste modifiable.
/// </summary>
public static class CardHeaderDefaults
{
    public const string Country = "République démocratique du Congo";
    public const string Ministry = "Ministère de l'Enseignement public, Hygiène et Prévoyance sociale";
    public const string Direction = "Direction de l'Enseignement des Sciences de santé";
    public const string Division = "Division provinciale du Haut-Katanga";
    public const string CityZone = "Ville de Lubumbashi / Zone de santé de Lubumbashi";
    public const string Institute = "Institut Technique Médical Cœur de Dieu";
    public const string Address = "N° 1, Av. EDGE | Q. Kamatete | C. Annexe";

    /// <summary>Valeur saisie si elle existe, sinon la valeur officielle.</summary>
    public static string Or(string? stored, string fallback) => string.IsNullOrWhiteSpace(stored) ? fallback : stored.Trim();
}
