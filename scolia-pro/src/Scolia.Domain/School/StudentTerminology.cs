namespace Scolia.Domain.School;

/// <summary>
/// Terminologie visible selon le profil de l'établissement. Ne renomme jamais
/// les identifiants techniques (Student, Apprenants, /api/apprenants) pour
/// préserver les données, la synchronisation et les anciennes applications.
/// </summary>
public sealed record StudentTerminology(string Singular, string Plural)
{
    public string SingularTitle => UpperFirst(Singular);
    public string PluralTitle => UpperFirst(Plural);
    private static string UpperFirst(string value) => char.ToUpperInvariant(value[0]) + value[1..];
}

public static class InstitutionTerminology
{
    public static StudentTerminology Students(string? institutionType) =>
        InstitutionProfiles.Canonicalize(institutionType) switch
        {
            InstitutionProfiles.Primaire or InstitutionProfiles.Secondaire =>
                new StudentTerminology("élève", "élèves"),
            InstitutionProfiles.SuperieurLmd =>
                new StudentTerminology("étudiant", "étudiants"),
            _ => new StudentTerminology("apprenant", "apprenants"),
        };
}
