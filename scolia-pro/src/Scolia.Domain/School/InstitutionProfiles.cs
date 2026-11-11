namespace Scolia.Domain.School;

/// <summary>
/// Identifiants stables des profils pédagogiques. Ne pas renuméroter ni traduire
/// ces valeurs persistées : les versions anciennes restent sur ItmIm.
/// </summary>
public static class InstitutionProfiles
{
    public const string ItmIm = "ItmIm";
    public const string Primaire = "Primaire";
    public const string Secondaire = "Secondaire";
    public const string SuperieurLmd = "SuperieurLmd";

    public static bool IsKnown(string? value) =>
        value is ItmIm or Primaire or Secondaire or SuperieurLmd;

    // Déploiement progressif : seul le moteur ITM/IM dispose actuellement
    // de ses règles pédagogiques complètes. Les autres profils sont bloqués
    // à l'installation jusqu'à validation de leurs propres moteurs.
    public static bool IsEnabled(string? value) => value == ItmIm;
}
