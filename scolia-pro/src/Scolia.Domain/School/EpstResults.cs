using Scolia.Domain.Common;

namespace Scolia.Domain.School;

/// <summary>Branche annuelle notée, avec son maximum par période de travail.</summary>
public sealed record EpstSubjectSpec(Guid SubjectId, string Code, string Name, decimal PeriodMax);

/// <summary>Période du calendrier du cycle : travail journalier ou examen.</summary>
public sealed record EpstPeriodSpec(Guid PeriodId, string Code, string TermCode, int Order, bool IsExam);

/// <summary>Une évaluation enregistrée (interrogation, devoir, examen) d'un élève.</summary>
public sealed record EpstScore(Guid EnrollmentId, Guid SubjectId, Guid PeriodId, decimal? Score, decimal MaxScore);

/// <summary>Trimestre ou semestre : ses périodes de travail suivies de l'examen.</summary>
public sealed record EpstTerm(string TermCode, IReadOnlyList<EpstPeriodSpec> Periods);

/// <summary>Points d'une case du bulletin ; <c>Points</c> absent = aucune évaluation saisie.</summary>
public sealed record EpstCell(decimal? Points, decimal Max);

public sealed record EpstSubjectResult(
    Guid SubjectId, string Code, string Name,
    IReadOnlyList<EpstCell> Periods, IReadOnlyList<EpstCell> Terms,
    decimal? Total, decimal Max, decimal? Percent, bool Complete, bool Failed);

public sealed record EpstPupilResult(
    Guid EnrollmentId,
    IReadOnlyList<EpstSubjectResult> Subjects,
    IReadOnlyList<EpstCell> PeriodTotals, IReadOnlyList<EpstCell> TermTotals,
    decimal? Total, decimal Max, decimal? Percent,
    int MissingCells, int FailedSubjects,
    int? Rank, IReadOnlyList<int?> TermRanks)
{
    public bool Complete => MissingCells == 0;
}

public sealed record EpstClassResult(
    IReadOnlyList<EpstTerm> Terms, IReadOnlyList<EpstPeriodSpec> Periods,
    IReadOnlyList<EpstSubjectSpec> Subjects, IReadOnlyList<EpstPupilResult> Pupils);

/// <summary>
/// Calcul des résultats du primaire et du secondaire selon le règlement validé de l'année.
/// Les branches sont pondérées par leurs maxima (usage RDC), jamais par un coefficient.
/// Une case sans évaluation reste vide : elle n'est jamais comptée comme zéro, et l'élève
/// concerné n'est ni classé ni déclaré en échec tant que ses points ne sont pas complets.
/// </summary>
public static class EpstResultsCalculator
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new DomainException("resultatsEpst", message);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Contrôle que le calendrier du cycle suit la structure déclarée dans le règlement.</summary>
    public static IReadOnlyList<EpstTerm> Layout(AcademicRegulation regulation, IReadOnlyCollection<EpstPeriodSpec> periods)
    {
        Require(regulation.IsValidated, "Le règlement pédagogique de l'année doit être validé.");
        Require(AcademicRegulationRules.IsEpst(regulation.Scope), "Ce calcul concerne le primaire et le secondaire.");
        var terms = periods.OrderBy(p => p.Order)
            .GroupBy(p => p.TermCode.Trim().ToUpperInvariant())
            .Select(g => new EpstTerm(g.Key, g.OrderBy(p => p.Order).ToList()))
            .ToList();
        var name = regulation.TermsCount == 3 ? "trimestre" : "semestre";
        Require(terms.Count == regulation.TermsCount,
            $"Le calendrier doit compter {regulation.TermsCount} {name}s ; il en compte {terms.Count}.");
        foreach (var term in terms)
        {
            var work = term.Periods.Count(p => !p.IsExam);
            var exams = term.Periods.Count(p => p.IsExam);
            Require(work == regulation.PeriodsPerTerm && exams == 1 && term.Periods[^1].IsExam,
                $"Le {name} {term.TermCode} doit compter {regulation.PeriodsPerTerm} période(s) de travail " +
                "suivie(s) d'un examen.");
        }
        return terms;
    }

    public static EpstClassResult Compute(
        AcademicRegulation regulation,
        IReadOnlyCollection<EpstPeriodSpec> periods,
        IReadOnlyCollection<EpstSubjectSpec> subjects,
        IReadOnlyCollection<Guid> enrollments,
        IReadOnlyCollection<EpstScore> scores)
    {
        var terms = Layout(regulation, periods);
        Require(subjects.Count > 0, "La classe n'a aucune branche notée.");
        Require(subjects.All(s => s.PeriodMax > 0), "Chaque branche doit avoir un maximum par période.");
        var ordered = terms.SelectMany(t => t.Periods).ToList();
        var subjectList = subjects.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        var cells = scores
            .Where(s => s.Score is not null && s.MaxScore > 0)
            .GroupBy(s => (s.EnrollmentId, s.SubjectId, s.PeriodId))
            .ToDictionary(g => g.Key, g =>
            {
                Require(g.All(x => x.Score >= 0 && x.Score <= x.MaxScore),
                    "Une cote dépasse le maximum de son évaluation.");
                return (Score: g.Sum(x => x.Score!.Value), Max: g.Sum(x => x.MaxScore));
            });

        decimal MaxOf(EpstSubjectSpec s, EpstPeriodSpec p) =>
            p.IsExam ? s.PeriodMax * regulation.ExamMaxFactor : s.PeriodMax;

        var pupils = new List<EpstPupilResult>();
        foreach (var enrollment in enrollments.Distinct())
        {
            var subjectResults = new List<EpstSubjectResult>();
            foreach (var subject in subjectList)
            {
                // Plusieurs interrogations d'une période sont ramenées au maximum de la période.
                var periodCells = ordered.Select(p =>
                {
                    var max = MaxOf(subject, p);
                    return cells.TryGetValue((enrollment, subject.SubjectId, p.PeriodId), out var c)
                        ? new EpstCell(Round(c.Score / c.Max * max), max)
                        : new EpstCell(null, max);
                }).ToList();
                var termCells = terms.Select(t =>
                {
                    var inTerm = t.Periods.Select(p => periodCells[ordered.IndexOf(p)]).ToList();
                    return new EpstCell(inTerm.All(c => c.Points is not null) ? inTerm.Sum(c => c.Points!.Value) : null,
                        inTerm.Sum(c => c.Max));
                }).ToList();
                var complete = periodCells.All(c => c.Points is not null);
                var max = periodCells.Sum(c => c.Max);
                decimal? total = complete ? periodCells.Sum(c => c.Points!.Value) : null;
                decimal? percent = total is { } t2 ? Round(t2 / max * 100) : null;
                subjectResults.Add(new(subject.SubjectId, subject.Code, subject.Name, periodCells, termCells,
                    total, max, percent, complete, complete && percent < regulation.SubjectPassPercent));
            }

            EpstCell Column(Func<EpstSubjectResult, EpstCell> pick)
            {
                var column = subjectResults.Select(pick).ToList();
                return new(column.All(c => c.Points is not null) ? column.Sum(c => c.Points!.Value) : null,
                    column.Sum(c => c.Max));
            }
            var periodTotals = ordered.Select((_, i) => Column(s => s.Periods[i])).ToList();
            var termTotals = terms.Select((_, i) => Column(s => s.Terms[i])).ToList();
            var missing = subjectResults.Sum(s => s.Periods.Count(c => c.Points is null));
            var grandMax = subjectResults.Sum(s => s.Max);
            decimal? grand = missing == 0 ? subjectResults.Sum(s => s.Total!.Value) : null;
            pupils.Add(new(enrollment, subjectResults, periodTotals, termTotals,
                grand, grandMax, grand is { } g ? Round(g / grandMax * 100) : null,
                missing, subjectResults.Count(s => s.Failed), null, terms.Select(_ => (int?)null).ToList()));
        }

        // Place : classement « 1, 2, 2, 4 » parmi les élèves dont les points sont complets.
        static Dictionary<Guid, int> Ranks(IEnumerable<(Guid Id, decimal? Points)> values)
        {
            var ranked = values.Where(v => v.Points is not null).OrderByDescending(v => v.Points).ToList();
            var result = new Dictionary<Guid, int>();
            for (var i = 0; i < ranked.Count; i++)
                result[ranked[i].Id] = i > 0 && ranked[i].Points == ranked[i - 1].Points
                    ? result[ranked[i - 1].Id] : i + 1;
            return result;
        }
        var overall = Ranks(pupils.Select(p => (p.EnrollmentId, p.Total)));
        var termRanks = terms.Select((_, i) => Ranks(pupils.Select(p => (p.EnrollmentId, p.TermTotals[i].Points)))).ToList();
        pupils = pupils.Select(p => p with
        {
            Rank = overall.TryGetValue(p.EnrollmentId, out var r) ? r : null,
            TermRanks = termRanks.Select(t => t.TryGetValue(p.EnrollmentId, out var tr) ? (int?)tr : null).ToList(),
        }).ToList();
        return new(terms, ordered, subjectList, pupils);
    }
}

/// <summary>
/// Appréciation de l'application et de la conduite d'un élève pour une période
/// de travail, telle qu'elle figure au bulletin du primaire et du secondaire.
/// </summary>
public sealed class EpstConductMark : Entity
{
    public Guid AcademicYearId { get; set; }
    public Guid PromotionId { get; set; }
    public Guid EnrollmentId { get; set; }
    public Guid SchoolCyclePeriodId { get; set; }
    public string? Application { get; set; }
    public string? Conduct { get; set; }
}

public static class EpstConductScale
{
    /// <summary>Échelle usuelle des bulletins : Élite, Très bien, Bien, Assez bien, Médiocre, Mauvais.</summary>
    public static readonly IReadOnlyList<(string Code, string Label)> Levels =
    [
        ("E", "Élite"), ("TB", "Très bien"), ("B", "Bien"),
        ("AB", "Assez bien"), ("M", "Médiocre"), ("MA", "Mauvais"),
    ];

    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var value = code.Trim().ToUpperInvariant();
        if (!Levels.Any(l => l.Code == value))
            throw new DomainException("conduite",
                "Appréciation inconnue : utilisez E, TB, B, AB, M ou MA.");
        return value;
    }
}
