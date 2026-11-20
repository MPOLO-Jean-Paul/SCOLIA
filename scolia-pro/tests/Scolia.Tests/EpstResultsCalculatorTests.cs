using Scolia.Domain.Common;
using Scolia.Domain.School;

namespace Scolia.Tests;

/// <summary>Résultats primaire / secondaire : maxima, totaux, pourcentage, échecs et place.</summary>
public sealed class EpstResultsCalculatorTests
{
    private static readonly Guid Year = Guid.NewGuid();
    private static readonly EpstSubjectSpec French = new(Guid.NewGuid(), "FR", "Français", 20);
    private static readonly EpstSubjectSpec Maths = new(Guid.NewGuid(), "MA", "Calcul", 10);

    private static AcademicRegulation Primary()
    {
        var r = AcademicRegulationRules.DefaultFor(RegulationScope.Primaire, Year);
        r.Status = RegulationStatus.Valide;
        return r;
    }

    /// <summary>T1 : P1 P2 EX1, T2 : P3 P4 EX2, T3 : P5 P6 EX3.</summary>
    private static List<EpstPeriodSpec> PrimaryCalendar()
    {
        var list = new List<EpstPeriodSpec>();
        var order = 1;
        for (var t = 1; t <= 3; t++)
        {
            list.Add(new(Guid.NewGuid(), $"P{2 * t - 1}", $"T{t}", order++, false));
            list.Add(new(Guid.NewGuid(), $"P{2 * t}", $"T{t}", order++, false));
            list.Add(new(Guid.NewGuid(), $"EX{t}", $"T{t}", order++, true));
        }
        return list;
    }

    /// <summary>Toutes les cases d'un élève à la fraction indiquée de leur maximum.</summary>
    private static IEnumerable<EpstScore> Everywhere(Guid pupil, IEnumerable<EpstPeriodSpec> periods, decimal fraction) =>
        from p in periods
        from s in new[] { French, Maths }
        let max = s.PeriodMax * (p.IsExam ? 2 : 1)
        select new EpstScore(pupil, s.SubjectId, p.PeriodId, max * fraction, max);

    [Fact]
    public void Exam_counts_double_and_grand_total_uses_maxima_as_weights()
    {
        var periods = PrimaryCalendar();
        var pupil = Guid.NewGuid();
        var result = EpstResultsCalculator.Compute(Primary(), periods, [French, Maths], [pupil],
            Everywhere(pupil, periods, 0.5m).ToList());
        var p = Assert.Single(result.Pupils);
        var french = p.Subjects.Single(s => s.Code == "FR");
        Assert.Equal(40m, french.Periods[2].Max);          // EX1 = 2 × 20
        Assert.Equal(80m, french.Terms[0].Max);            // 20 + 20 + 40
        Assert.Equal(240m, french.Max);                    // 3 trimestres
        Assert.Equal(360m, p.Max);                         // Français 240 + Calcul 120
        Assert.Equal(180m, p.Total);
        Assert.Equal(50m, p.Percent);
        Assert.Equal(0, p.FailedSubjects);                 // 50 % n'est pas un échec
        Assert.Equal(1, p.Rank);
        Assert.True(p.Complete);
    }

    [Fact]
    public void Several_tests_in_one_period_are_scaled_to_the_period_maximum()
    {
        var periods = PrimaryCalendar();
        var pupil = Guid.NewGuid();
        var p1 = periods[0];
        var scores = Everywhere(pupil, periods, 1m)
            .Where(s => !(s.PeriodId == p1.PeriodId && s.SubjectId == French.SubjectId)).ToList();
        scores.Add(new(pupil, French.SubjectId, p1.PeriodId, 8, 10));
        scores.Add(new(pupil, French.SubjectId, p1.PeriodId, 6, 10));
        var result = EpstResultsCalculator.Compute(Primary(), periods, [French, Maths], [pupil], scores);
        Assert.Equal(14m, result.Pupils[0].Subjects.Single(s => s.Code == "FR").Periods[0].Points);
    }

    [Fact]
    public void Missing_evaluation_is_never_zero_and_blocks_total_rank_and_failure()
    {
        var periods = PrimaryCalendar();
        var pupil = Guid.NewGuid();
        var scores = Everywhere(pupil, periods, 0.2m)
            .Where(s => !(s.PeriodId == periods[^1].PeriodId && s.SubjectId == Maths.SubjectId)).ToList();
        var p = EpstResultsCalculator.Compute(Primary(), periods, [French, Maths], [pupil], scores).Pupils[0];
        Assert.Equal(1, p.MissingCells);
        Assert.Null(p.Total);
        Assert.Null(p.Percent);
        Assert.Null(p.Rank);
        Assert.False(p.Complete);
        var maths = p.Subjects.Single(s => s.Code == "MA");
        Assert.False(maths.Failed);
        Assert.Null(maths.Terms[2].Points);
        Assert.NotNull(maths.Terms[0].Points);
        Assert.Equal(1, p.FailedSubjects);                 // Français complet à 20 % : échec
        Assert.NotNull(p.TermRanks[0]);
        Assert.Null(p.TermRanks[2]);
    }

    [Fact]
    public void Equal_totals_share_a_place_and_the_next_place_is_skipped()
    {
        var periods = PrimaryCalendar();
        var (a, b, c, d) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var scores = Everywhere(a, periods, 0.9m).Concat(Everywhere(b, periods, 0.7m))
            .Concat(Everywhere(c, periods, 0.7m)).Concat(Everywhere(d, periods, 0.4m)).ToList();
        var result = EpstResultsCalculator.Compute(Primary(), periods, [French, Maths], [a, b, c, d], scores);
        Assert.Equal([1, 2, 2, 4], new[] { a, b, c, d }
            .Select(id => result.Pupils.Single(p => p.EnrollmentId == id).Rank!.Value).ToArray());
        Assert.Equal(2, result.Pupils.Single(p => p.EnrollmentId == d).FailedSubjects);
    }

    [Fact]
    public void Calendar_must_follow_the_validated_regulation()
    {
        var periods = PrimaryCalendar();
        var draft = AcademicRegulationRules.DefaultFor(RegulationScope.Primaire, Year);
        Assert.Throws<DomainException>(() => EpstResultsCalculator.Layout(draft, periods));
        Assert.Throws<DomainException>(() => EpstResultsCalculator.Layout(Primary(), periods.Take(6).ToList()));
        var examFirst = periods.Select(p => p.TermCode == "T1" ? p with { IsExam = p.Code == "P1" } : p).ToList();
        Assert.Throws<DomainException>(() => EpstResultsCalculator.Layout(Primary(), examFirst));
        Assert.Equal(3, EpstResultsCalculator.Layout(Primary(), periods).Count);
    }

    [Fact]
    public void Secondary_uses_two_semesters()
    {
        var r = AcademicRegulationRules.DefaultFor(RegulationScope.Secondaire, Year);
        r.Status = RegulationStatus.Valide;
        var periods = PrimaryCalendar().Where(p => p.TermCode != "T3").ToList();
        var pupil = Guid.NewGuid();
        var result = EpstResultsCalculator.Compute(r, periods, [French], [pupil],
            Everywhere(pupil, periods, 1m).Where(s => s.SubjectId == French.SubjectId).ToList());
        Assert.Equal(2, result.Terms.Count);
        Assert.Equal(160m, result.Pupils[0].Total);
        Assert.Equal(100m, result.Pupils[0].Percent);
    }
}
