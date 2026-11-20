using Scolia.Domain.Common;
using Scolia.Domain.Deliberation;

namespace Scolia.Domain.School;

public sealed record LmdUeSpec(Guid Id, string Code, string Name, CourseSemester Semester, decimal Credits);
public sealed record LmdEcSpec(Guid Id, Guid UeId, string Code, string Name, decimal Credits, decimal Hours);
/// <summary>Note d'un EC à une session (1 ou 2) sur l'échelle du règlement.</summary>
public sealed record LmdMarkEntry(Guid EnrollmentId, Guid ElementId, int Session, decimal? Score);

public sealed record LmdEcResult(Guid Id, string Code, string Name, decimal Weight, decimal? Mark, int? Session);

public sealed record LmdUeResult(
    Guid Id, string Code, string Name, CourseSemester Semester, decimal Credits,
    IReadOnlyList<LmdEcResult> Elements, decimal? Average, bool Acquired, bool ByCompensation);

public sealed record LmdSemesterResult(
    CourseSemester Semester, decimal? Average, decimal CreditsAcquired, decimal CreditsPlanned, bool Complete);

public sealed record LmdStudentResult(
    Guid EnrollmentId, IReadOnlyList<LmdUeResult> Units,
    LmdSemesterResult S1, LmdSemesterResult S2,
    decimal? Average, decimal CreditsAcquired, decimal CreditsPlanned, bool Complete, int? Rank)
{
    public IReadOnlyList<LmdUeResult> MissingUnits => Units.Where(u => !u.Acquired).ToList();
}

/// <summary>Décision LMD. Valeurs persistées : ne pas renuméroter.</summary>
public enum LmdOutcome
{
    Incomplet = 0,
    Admis = 1,
    DeuxiemeSession = 2,
    AdmisAvecDettes = 3,
    Ajourne = 4,
}

/// <summary>
/// Résultats d'une promotion LMD selon le règlement validé :
/// moyenne d'UE pondérée par les crédits des EC (à défaut par leurs heures, à défaut à parts égales),
/// UE acquise à la note de validation (avec ou sans compensation entre EC selon le règlement),
/// compensation facultative entre UE d'un même semestre, crédits capitalisés,
/// et, après la deuxième session, meilleure des deux notes de chaque EC.
/// </summary>
public static class LmdResultsCalculator
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new DomainException("resultatsLmd", message);
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <param name="semester">S1 seul pour les résultats partiels ; null pour toute l'année.</param>
    /// <param name="includeSecondSession">Prend la meilleure note des deux sessions.</param>
    public static IReadOnlyList<LmdStudentResult> Compute(
        AcademicRegulation regulation,
        IReadOnlyCollection<LmdUeSpec> units, IReadOnlyCollection<LmdEcSpec> elements,
        IReadOnlyCollection<Guid> enrollments, IReadOnlyCollection<LmdMarkEntry> marks,
        CourseSemester? semester = null, bool includeSecondSession = false)
    {
        Require(regulation.IsValidated && regulation.Scope == RegulationScope.SuperieurLmd,
            "Le règlement LMD de l'année doit être validé.");
        Require(semester is null or CourseSemester.S1 or CourseSemester.S2, "Semestre LMD inconnu.");
        Require(units.All(u => u.Semester is CourseSemester.S1 or CourseSemester.S2 && u.Credits > 0),
            "Chaque UE appartient au S1 ou au S2 et porte des crédits.");
        var scale = regulation.MarkScale;
        Require(marks.All(m => m.Session is 1 or 2 && m.Score is null or >= 0 && m.Score <= scale),
            $"Une note LMD est comprise entre 0 et {scale}.");

        var selected = units.Where(u => semester is null || u.Semester == semester)
            .OrderBy(u => u.Semester).ThenBy(u => u.Code, StringComparer.OrdinalIgnoreCase).ToList();
        var byUe = elements.GroupBy(e => e.UeId).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Code).ToList());
        Require(selected.All(u => byUe.ContainsKey(u.Id)), "Chaque UE doit comporter au moins un EC.");
        var markIndex = marks.Where(m => m.Score is not null)
            .GroupBy(m => (m.EnrollmentId, m.ElementId))
            .ToDictionary(g => g.Key, g => g.ToList());

        static IReadOnlyList<decimal> Weights(IReadOnlyList<LmdEcSpec> ecs) =>
            ecs.All(e => e.Credits > 0) ? ecs.Select(e => e.Credits).ToList()
            : ecs.All(e => e.Hours > 0) ? ecs.Select(e => e.Hours).ToList()
            : ecs.Select(_ => 1m).ToList();

        var students = new List<LmdStudentResult>();
        foreach (var enrollment in enrollments.Distinct())
        {
            var ueResults = new List<LmdUeResult>();
            foreach (var ue in selected)
            {
                var ecs = byUe[ue.Id];
                var weights = Weights(ecs);
                var ecResults = ecs.Select((ec, i) =>
                {
                    var entries = markIndex.TryGetValue((enrollment, ec.Id), out var list) ? list : [];
                    var first = entries.FirstOrDefault(m => m.Session == 1)?.Score;
                    var second = includeSecondSession ? entries.FirstOrDefault(m => m.Session == 2)?.Score : null;
                    // Deuxième session : la meilleure des deux notes est retenue.
                    var (mark, session) = second is { } s2 && (first is null || s2 > first) ? (s2, 2)
                        : first is { } s1 ? (s1, 1) : ((decimal?)null, (int?)null);
                    return new LmdEcResult(ec.Id, ec.Code, ec.Name, weights[i], mark, session);
                }).ToList();
                var complete = ecResults.All(e => e.Mark is not null);
                decimal? average = complete
                    ? Round(ecResults.Sum(e => e.Mark!.Value * e.Weight) / ecResults.Sum(e => e.Weight)) : null;
                var acquired = complete && (regulation.CompensateWithinUe
                    ? average >= regulation.UeValidationMark
                    : ecResults.All(e => e.Mark >= regulation.UeValidationMark));
                ueResults.Add(new(ue.Id, ue.Code, ue.Name, ue.Semester, ue.Credits, ecResults, average, acquired, false));
            }

            LmdSemesterResult Semester(CourseSemester s)
            {
                var list = ueResults.Where(u => u.Semester == s).ToList();
                var planned = list.Sum(u => u.Credits);
                var complete = list.Count > 0 && list.All(u => u.Average is not null);
                decimal? avg = complete ? Round(list.Sum(u => u.Average!.Value * u.Credits) / planned) : null;
                // Compensation entre UE : une moyenne semestrielle suffisante valide toutes les UE du semestre.
                if (regulation.CompensateBetweenUe && avg >= regulation.UeValidationMark)
                    for (var i = 0; i < ueResults.Count; i++)
                        if (ueResults[i].Semester == s && !ueResults[i].Acquired)
                            ueResults[i] = ueResults[i] with { Acquired = true, ByCompensation = true };
                var acquired = ueResults.Where(u => u.Semester == s && u.Acquired).Sum(u => u.Credits);
                return new(s, avg, acquired, planned, complete);
            }
            var r1 = Semester(CourseSemester.S1);
            var r2 = Semester(CourseSemester.S2);
            var semesters = new[] { r1, r2 }.Where(r => r.CreditsPlanned > 0).ToList();
            var allComplete = semesters.Count > 0 && semesters.All(r => r.Complete);
            var plannedTotal = semesters.Sum(r => r.CreditsPlanned);
            decimal? yearAverage = allComplete
                ? Round(semesters.Sum(r => r.Average!.Value * r.CreditsPlanned) / plannedTotal) : null;
            students.Add(new(enrollment, ueResults, r1, r2, yearAverage,
                semesters.Sum(r => r.CreditsAcquired), plannedTotal, allComplete, null));
        }

        var ranked = students.Where(s => s.Average is not null).OrderByDescending(s => s.Average).ToList();
        var ranks = new Dictionary<Guid, int>();
        for (var i = 0; i < ranked.Count; i++)
            ranks[ranked[i].EnrollmentId] = i > 0 && ranked[i].Average == ranked[i - 1].Average
                ? ranks[ranked[i - 1].EnrollmentId] : i + 1;
        return students.Select(s => s with { Rank = ranks.TryGetValue(s.EnrollmentId, out var r) ? r : null }).ToList();
    }

    /// <summary>Première session : admis avec tous les crédits, sinon deuxième session.</summary>
    public static LmdOutcome FirstSession(LmdStudentResult r) =>
        !r.Complete ? LmdOutcome.Incomplet
        : r.CreditsAcquired >= r.CreditsPlanned ? LmdOutcome.Admis
        : LmdOutcome.DeuxiemeSession;

    /// <summary>Après la deuxième session : admis, admis avec dettes dans la limite du règlement, ou ajourné.</summary>
    public static LmdOutcome Final(AcademicRegulation regulation, LmdStudentResult r) =>
        !r.Complete ? LmdOutcome.Incomplet
        : r.CreditsAcquired >= r.CreditsPlanned ? LmdOutcome.Admis
        : r.CreditsPlanned - r.CreditsAcquired <= regulation.MaxDebtCredits ? LmdOutcome.AdmisAvecDettes
        : LmdOutcome.Ajourne;

    public static string Label(LmdOutcome outcome) => outcome switch
    {
        LmdOutcome.Admis => "Admis",
        LmdOutcome.DeuxiemeSession => "Deuxième session",
        LmdOutcome.AdmisAvecDettes => "Admis avec dettes",
        LmdOutcome.Ajourne => "Ajourné",
        _ => "Incomplet",
    };

    public static bool Promotes(LmdOutcome outcome) => outcome is LmdOutcome.Admis or LmdOutcome.AdmisAvecDettes;
}
