using Scolia.Domain.Common;
using Scolia.Domain.Deliberation;

namespace Scolia.Domain.School;

/// <summary>Nature d'une période pédagogique, indépendante des échéances financières.</summary>
public enum PedagogicalPeriodKind
{
    Evaluation = 1, // primaire et secondaire : périodes de notes, nombre fixé par le programme
    Semester = 2,   // supérieur LMD : S1 et S2
}

/// <summary>
/// Période d'évaluation ou semestre du programme scolaire. Ne remplace jamais
/// AcademicPeriod (mois et échéances de paiement), ni les sessions ITM existantes.
/// </summary>
public sealed class PedagogicalPeriod : Entity
{
    public Guid AcademicYearId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public PedagogicalPeriodKind Kind { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}

/// <summary>
/// Règles de configuration des nouveaux profils : aucun changement des calculs ITM.
/// Les calendriers officiels peuvent évoluer, les dates ne sont jamais imposées
/// par une valeur codée en dur.
/// </summary>
public static class PedagogicalCalendarRules
{
    public static bool AllowsCourse(string institutionType, CourseSemester semester) =>
        institutionType switch
        {
            InstitutionProfiles.ItmIm or InstitutionProfiles.ItmIem => semester is CourseSemester.Annuel or CourseSemester.S1 or CourseSemester.S2
                or CourseSemester.JuryEpreuve or CourseSemester.JuryBloc,
            InstitutionProfiles.Maternelle or InstitutionProfiles.Primaire or InstitutionProfiles.Secondaire
                or InstitutionProfiles.ComplexeScolaire => semester == CourseSemester.Annuel,
            InstitutionProfiles.SuperieurLmd => semester is CourseSemester.S1 or CourseSemester.S2,
            _ => false,
        };

    public static void RequireCourse(string institutionType, CourseSemester semester)
    {
        if (!AllowsCourse(institutionType, semester))
            throw new DomainException("profilPedagogique", "La période du cours n'est pas autorisée pour ce type d'établissement.");
    }

    /// <summary>
    /// Validation avant toute activation d'un calendrier pédagogique.
    /// Les mois de paiement n'entrent pas dans le calcul, même lorsqu'ils
    /// couvrent exactement les mêmes dates.
    /// </summary>
    public static void Validate(string institutionType, AcademicYear year, IReadOnlyCollection<PedagogicalPeriod> periods)
    {
        if (!InstitutionProfiles.IsKnown(institutionType))
            throw new DomainException("profilPedagogique", "Type d'établissement inconnu.");
        if (InstitutionProfiles.Canonicalize(institutionType) == InstitutionProfiles.ItmIm)
        {
            if (periods.Count != 0)
                throw new DomainException("profilPedagogique", "Le calendrier ITM existant ne doit pas être remplacé.");
            return;
        }

        // La maternelle et les complexes multi-cycles sont modélisés avec
        // SchoolCyclePeriod (cycle + année) : aucune collision P1/P2 entre niveaux.
        if (institutionType is InstitutionProfiles.Maternelle or InstitutionProfiles.ComplexeScolaire)
            throw new DomainException("periodePedagogique",
                "Définissez les périodes sur chaque cycle scolaire, avec le mode observation pour la maternelle.");

        if (periods.Count == 0)
            throw new DomainException("periodePedagogique", "Définissez les périodes pédagogiques de l'année.");
        if (year.StartDate > year.EndDate)
            throw new DomainException("periodePedagogique", "Dates de l'année scolaire invalides.");

        var sorted = periods.OrderBy(p => p.Order).ThenBy(p => p.StartDate).ToArray();
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < sorted.Length; i++)
        {
            var current = sorted[i];
            if (current.AcademicYearId != year.Id)
                throw new DomainException("periodePedagogique", "Cette période appartient à une autre année.");
            if (current.Order != i + 1)
                throw new DomainException("periodePedagogique", "Les périodes doivent être numérotées dans l'ordre, à partir de 1.");
            if (current.Code.Length is < 1 or > 16 || !current.Code.All(char.IsAsciiLetterOrDigit)
                || !seenCodes.Add(current.Code))
                throw new DomainException("periodePedagogique", "Code de période invalide ou déjà utilisé.");
            if (string.IsNullOrWhiteSpace(current.Name) || current.Name.Length > 80)
                throw new DomainException("periodePedagogique", "La période doit avoir une désignation de 80 caractères maximum.");
            if (current.StartDate < year.StartDate || current.EndDate > year.EndDate || current.StartDate > current.EndDate)
                throw new DomainException("periodePedagogique", "Dates de période en dehors de l'année ou inversées.");
            if (i > 0 && sorted[i - 1].EndDate >= current.StartDate)
                throw new DomainException("periodePedagogique", "Deux périodes pédagogiques ne peuvent pas se chevaucher.");
        }

        if (institutionType == InstitutionProfiles.SuperieurLmd)
        {
            if (sorted.Length != 2 || sorted.Any(p => p.Kind != PedagogicalPeriodKind.Semester)
                || !string.Equals(sorted[0].Code, "S1", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(sorted[1].Code, "S2", StringComparison.OrdinalIgnoreCase))
                throw new DomainException("periodePedagogique", "Le calendrier LMD doit contenir un premier et un deuxième semestre (S1 et S2).");
        }
        else
        {
            // Nombre et bornes à renseigner selon le calendrier officiel de l'année.
            // En 2026-2027 : primaire = 6 périodes / 3 trimestres ;
            // secondaire = 4 périodes / 2 semestres. Les cours restent annuels.
            if (sorted.Length > 12 || sorted.Any(p => p.Kind != PedagogicalPeriodKind.Evaluation))
                throw new DomainException("periodePedagogique", "Le primaire et le secondaire utilisent des périodes d'évaluation, sans cours semestriels.");
        }
    }
}
