using Scolia.Domain.Common;

namespace Scolia.Domain.School;

/// <summary>
/// Regroupement scolaire officiel : trimestre au primaire, semestre au
/// secondaire. Ce n'est PAS le semestre UE/EC du LMD ni le mois financier.
/// </summary>
public enum SchoolReportingTermKind
{
    Trimester = 1,
    Semester = 2,
}

/// <summary>Période de travaux journaliers et évaluations du cycle scolaire.</summary>
public sealed record SchoolReportingPeriod(
    string Code, DateOnly StartDate, DateOnly EndDate);

/// <summary>Trimestre/semestre, avec ses périodes pédagogiques non chevauchantes.</summary>
public sealed record SchoolReportingTerm(
    string Code,
    SchoolReportingTermKind Kind,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<SchoolReportingPeriod> Periods);

/// <summary>
/// Référence datée extraite du calendrier MINEDU-NC 2026-2027.
/// https://edu-nc.gouv.cd/calendrier-scolaire
/// Reproduire un calendrier ne vaut pas approbation de ses grilles de notes
/// et ne modifie aucune année ni mensualité déjà enregistrée.
/// </summary>
public static class OfficialSchoolCalendar2026
{
    public const string YearLabel = "2026-2027";
    public static readonly DateOnly Start = new(2026, 9, 1);
    public static readonly DateOnly End = new(2027, 7, 2);

    private static readonly IReadOnlyList<SchoolReportingTerm> PrimaryTerms =
    [
        new("T1", SchoolReportingTermKind.Trimester, new(2026, 9, 1), new(2026, 12, 22),
        [
            new("P1", new(2026, 9, 1), new(2026, 11, 4)),
            new("P2", new(2026, 11, 9), new(2026, 12, 22)),
        ]),
        new("T2", SchoolReportingTermKind.Trimester, new(2027, 1, 11), new(2027, 3, 20),
        [
            new("P3", new(2027, 1, 11), new(2027, 2, 17)),
            new("P4", new(2027, 2, 22), new(2027, 3, 20)),
        ]),
        new("T3", SchoolReportingTermKind.Trimester, new(2027, 4, 5), new(2027, 7, 2),
        [
            new("P5", new(2027, 4, 5), new(2027, 5, 22)),
            new("P6", new(2027, 5, 24), new(2027, 7, 2)),
        ]),
    ];

    private static readonly IReadOnlyList<SchoolReportingTerm> SecondaryTerms =
    [
        new("S1", SchoolReportingTermKind.Semester, new(2026, 9, 1), new(2027, 2, 17),
        [
            new("P1", new(2026, 9, 1), new(2026, 11, 4)),
            new("P2", new(2026, 11, 9), new(2027, 2, 17)),
        ]),
        new("S2", SchoolReportingTermKind.Semester, new(2027, 2, 22), new(2027, 7, 2),
        [
            new("P3", new(2027, 2, 22), new(2027, 4, 24)),
            new("P4", new(2027, 4, 26), new(2027, 7, 2)),
        ]),
    ];

    public static IReadOnlyList<SchoolReportingTerm> TermsFor(string institutionType) =>
        institutionType switch
        {
            InstitutionProfiles.Primaire => PrimaryTerms,
            InstitutionProfiles.Secondaire => SecondaryTerms,
            _ => throw new DomainException("calendrierOfficiel", "Le calendrier 2026-2027 proposé concerne uniquement les écoles primaires et secondaires."),
        };

    /// <summary>
    /// Vérifie l'année concernée et retourne des copies pour configuration
    /// manuelle. Pas d'inscription en base, pas de mise à jour automatique.
    /// </summary>
    public static IReadOnlyList<PedagogicalPeriod> Prepare(string institutionType, AcademicYear year)
    {
        if (year.Label != YearLabel || year.StartDate > Start || year.EndDate < End)
            throw new DomainException("calendrierOfficiel",
                "Le modèle 2026-2027 exige l'année 2026-2027 couvrant du 01/09/2026 au 02/07/2027.");

        var terms = TermsFor(institutionType);
        var periods = new List<PedagogicalPeriod>();
        foreach (var term in terms)
        {
            if (term.Periods.Count == 0 || term.Periods.Any(p =>
                    p.StartDate < term.StartDate || p.EndDate > term.EndDate ||
                    p.StartDate > p.EndDate))
                throw new DomainException("calendrierOfficiel", "Périodes en dehors du trimestre ou semestre.");
            foreach (var p in term.Periods)
                periods.Add(new PedagogicalPeriod
                {
                    AcademicYearId = year.Id,
                    Code = p.Code,
                    Name = $"Période {periods.Count + 1}",
                    Order = periods.Count + 1,
                    Kind = PedagogicalPeriodKind.Evaluation,
                    StartDate = p.StartDate,
                    EndDate = p.EndDate,
                });
        }
        PedagogicalCalendarRules.Validate(institutionType, year, periods);
        return periods;
    }
}
