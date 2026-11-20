using Scolia.Domain.Common;
using Scolia.Domain.Deliberation;

namespace Scolia.Domain.School;

/// <summary>
/// Note d'un EC à la première session. Une UE n'entre jamais directement
/// dans la pondération : seuls ses éléments constitutifs comptent.
/// </summary>
public sealed record LmdElementMark(
    Guid ElementId,
    CourseSemester Semester,
    decimal Credits,
    decimal? MarkOutOf20);

/// <summary>
/// Relevé pédagogique intermédiaire du semestre : aucun verdict officiel
/// avant application des règles de jury propres à l'établissement et à l'année.
/// </summary>
public sealed record LmdSemesterSummary(
    CourseSemester Semester,
    decimal PlannedCredits,
    decimal EvaluatedCredits,
    decimal? AverageOutOf20,
    bool IsComplete);

/// <summary>
/// Consolidation de la première session de l'année LMD, sans inventer
/// une décision de réussite ou de passage ; les règles de compensation,
/// rattrapage et jury sont une phase métier séparée.
/// </summary>
public sealed record LmdFirstSessionSummary(
    LmdSemesterSummary S1,
    LmdSemesterSummary S2,
    decimal? AnnualAverageOutOf20,
    bool IsComplete);

public static class LmdFirstSessionCalculator
{
    public static LmdSemesterSummary Semester(
        CourseSemester semester, IReadOnlyCollection<LmdElementMark> marks)
    {
        if (semester is not (CourseSemester.S1 or CourseSemester.S2))
            throw new DomainException("semestreLmd", "Le supérieur LMD accepte exclusivement S1 ou S2.");

        if (marks.Any(m => m.Semester != semester || m.Credits <= 0 ||
                           m.MarkOutOf20 is < 0 or > 20))
            throw new DomainException("semestreLmd", "Une note LMD doit appartenir au semestre, avoir des crédits positifs et être comprise entre 0 et 20.");

        if (marks.Select(m => m.ElementId).Distinct().Count() != marks.Count)
            throw new DomainException("semestreLmd", "Un élément constitutif ne peut pas apparaître deux fois dans un relevé.");

        var plannedCredits = marks.Sum(m => m.Credits);
        var evaluated = marks.Where(m => m.MarkOutOf20 is not null).ToArray();
        var evaluatedCredits = evaluated.Sum(m => m.Credits);
        decimal? average = evaluatedCredits == 0
            ? null
            : decimal.Round(evaluated.Sum(m => m.MarkOutOf20!.Value * m.Credits) / evaluatedCredits,
                2, MidpointRounding.AwayFromZero);

        // Un semestre sans EC ne peut pas être réputé achevé.
        return new LmdSemesterSummary(semester, plannedCredits, evaluatedCredits,
            average, marks.Count > 0 && evaluatedCredits == plannedCredits);
    }

    public static LmdFirstSessionSummary FirstSession(IReadOnlyCollection<LmdElementMark> marks)
    {
        if (marks.Any(m => m.Semester is not (CourseSemester.S1 or CourseSemester.S2)))
            throw new DomainException("semestreLmd", "Aucun cours annuel ni épreuve de jury ITM n'entre dans un relevé LMD.");

        if (marks.Select(m => m.ElementId).Distinct().Count() != marks.Count)
            throw new DomainException("semestreLmd", "Un élément constitutif ne peut pas être comptabilisé dans deux semestres.");

        var s1 = Semester(CourseSemester.S1, marks.Where(m => m.Semester == CourseSemester.S1).ToArray());
        var s2 = Semester(CourseSemester.S2, marks.Where(m => m.Semester == CourseSemester.S2).ToArray());
        var complete = s1.IsComplete && s2.IsComplete;
        decimal? annual = !complete || s1.AverageOutOf20 is null || s2.AverageOutOf20 is null
            ? null
            : decimal.Round((s1.AverageOutOf20.Value * s1.PlannedCredits +
                            s2.AverageOutOf20.Value * s2.PlannedCredits) /
                           (s1.PlannedCredits + s2.PlannedCredits),
                2, MidpointRounding.AwayFromZero);

        return new LmdFirstSessionSummary(s1, s2, annual, complete);
    }
}
