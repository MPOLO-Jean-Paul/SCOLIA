namespace Scolia.Domain.School;

/// <summary>
/// Vocabulaire visible selon le profil d'établissement. Les noms techniques
/// historiques (Student, Apprenant, api/apprenants) restent inchangés.
/// </summary>
public sealed record InstitutionTerminology(string Singular, string Plural)
{
    public string SingularTitle => Capitalize(Singular);
    public string PluralTitle => Capitalize(Plural);
    public string Elided => "l'" + Singular;
    public string ElidedTitle => "L'" + Singular;
    public string Indefinite => "un " + Singular;
    public string IndefiniteTitle => "Un " + Singular;

    private static string Capitalize(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];

    public static InstitutionTerminology For(string? institutionType) =>
        institutionType switch
        {
            InstitutionProfiles.Primaire or InstitutionProfiles.Secondaire => new("élève", "élèves"),
            InstitutionProfiles.SuperieurLmd => new("étudiant", "étudiants"),
            _ => new("apprenant", "apprenants"),
        };
}
