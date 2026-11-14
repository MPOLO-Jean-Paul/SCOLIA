using Scolia.Domain.Common;
using Scolia.Domain.Deliberation;
using Scolia.Domain.School;

namespace Scolia.Tests;

/// <summary>Résultats LMD : UE, EC, crédits capitalisés, compensation, sessions et décisions.</summary>
public sealed class LmdResultsCalculatorTests
{
    private static AcademicRegulation Rule(Action<AcademicRegulation>? change = null)
    {
        var r = AcademicRegulationRules.DefaultFor(RegulationScope.SuperieurLmd, Guid.NewGuid());
        change?.Invoke(r);
        r.Status = RegulationStatus.Valide;
        return r;
    }

    // S1 : UE A (20 crédits, EC A1 12 + A2 8) et UE B (10 crédits, EC B1). S2 : UE C (30 crédits, EC C1).
    private static readonly LmdUeSpec A = new(Guid.NewGuid(), "UEA", "Mathématiques", CourseSemester.S1, 20);
    private static readonly LmdUeSpec B = new(Guid.NewGuid(), "UEB", "Physique", CourseSemester.S1, 10);
    private static readonly LmdUeSpec C = new(Guid.NewGuid(), "UEC", "Informatique", CourseSemester.S2, 30);
    private static readonly LmdEcSpec A1 = new(Guid.NewGuid(), A.Id, "A1", "Analyse", 12, 0);
    private static readonly LmdEcSpec A2 = new(Guid.NewGuid(), A.Id, "A2", "Algèbre", 8, 0);
    private static readonly LmdEcSpec B1 = new(Guid.NewGuid(), B.Id, "B1", "Mécanique", 10, 0);
    private static readonly LmdEcSpec C1 = new(Guid.NewGuid(), C.Id, "C1", "Programmation", 30, 0);

    private static LmdStudentResult One(AcademicRegulation r, Guid s, IEnumerable<(LmdEcSpec Ec, decimal? Mark, int Session)> marks,
        CourseSemester? semester = null, bool second = false) =>
        LmdResultsCalculator.Compute(r, [A, B, C], [A1, A2, B1, C1], [s],
            marks.Select(m => new LmdMarkEntry(s, m.Ec.Id, m.Session, m.Mark)).ToList(), semester, second).Single();

    [Fact]
    public void Ue_average_is_weighted_by_element_credits_and_compensates_inside_the_ue()
    {
        var s = Guid.NewGuid();
        var r = One(Rule(), s, [(A1, 12, 1), (A2, 7, 1), (B1, 14, 1), (C1, 11, 1)]);
        var a = r.Units.Single(u => u.Code == "UEA");
        Assert.Equal(10m, a.Average);                  // (12×12 + 7×8) / 20
        Assert.True(a.Acquired);                      // compensation entre EC de la même UE
        Assert.Equal(30m, r.S1.CreditsAcquired);
        Assert.Equal(60m, r.CreditsAcquired);
        Assert.Equal(LmdOutcome.Admis, LmdResultsCalculator.FirstSession(r));
    }

    [Fact]
    public void Without_compensation_every_element_must_reach_the_validation_mark()
    {
        var s = Guid.NewGuid();
        var r = One(Rule(x => x.CompensateWithinUe = false), s, [(A1, 12, 1), (A2, 7, 1), (B1, 14, 1), (C1, 11, 1)]);
        Assert.False(r.Units.Single(u => u.Code == "UEA").Acquired);
        Assert.Equal(40m, r.CreditsAcquired);
    }

    [Fact]
    public void Compensation_between_ue_validates_the_whole_semester_only_when_enabled()
    {
        var s = Guid.NewGuid();
        var marks = new[] { (A1, (decimal?)11m, 1), (A2, 11m, 1), (B1, 8m, 1), (C1, 12m, 1) };
        var strict = One(Rule(), s, marks);
        Assert.False(strict.Units.Single(u => u.Code == "UEB").Acquired);
        Assert.Equal(LmdOutcome.DeuxiemeSession, LmdResultsCalculator.FirstSession(strict));
        var lenient = One(Rule(x => x.CompensateBetweenUe = true), s, marks);
        var b = lenient.Units.Single(u => u.Code == "UEB");
        Assert.True(b.Acquired && b.ByCompensation);   // moyenne S1 = (11×20 + 8×10) / 30 = 10
        Assert.Equal(10m, lenient.S1.Average);
    }

    [Fact]
    public void S1_partial_results_ignore_s2_and_missing_marks_block_completion()
    {
        var s = Guid.NewGuid();
        var partial = One(Rule(), s, [(A1, 15, 1), (A2, 15, 1), (B1, 9, 1)], CourseSemester.S1);
        Assert.Equal(30m, partial.CreditsPlanned);
        Assert.Equal(20m, partial.CreditsAcquired);
        Assert.True(partial.Complete);
        Assert.DoesNotContain(partial.Units, u => u.Semester == CourseSemester.S2);
        var year = One(Rule(), s, [(A1, 15, 1), (A2, 15, 1), (B1, 9, 1)]);
        Assert.False(year.Complete);
        Assert.Equal(LmdOutcome.Incomplet, LmdResultsCalculator.FirstSession(year));
    }

    [Fact]
    public void Second_session_keeps_the_best_mark_and_debts_are_tolerated_up_to_the_regulation()
    {
        var s = Guid.NewGuid();
        var first = new (LmdEcSpec, decimal?, int)[] { (A1, 15, 1), (A2, 15, 1), (B1, 6, 1), (C1, 6, 1) };
        Assert.Equal(LmdOutcome.DeuxiemeSession, LmdResultsCalculator.FirstSession(One(Rule(), s, first)));

        // B1 réussie en 2e session ; C1 plus faible en 2e session : la 1re note est gardée, l'UE reste non acquise.
        var after = One(Rule(), s, [.. first, (B1, 12, 2), (C1, 4, 2)], second: true);
        Assert.Equal(12m, after.Units.Single(u => u.Code == "UEB").Elements[0].Mark);
        Assert.Equal(2, after.Units.Single(u => u.Code == "UEB").Elements[0].Session);
        Assert.Equal(6m, after.Units.Single(u => u.Code == "UEC").Elements[0].Mark);
        Assert.Equal(30m, after.CreditsAcquired);
        Assert.Equal(LmdOutcome.Ajourne, LmdResultsCalculator.Final(Rule(), after));        // 30 crédits manquants > 15

        var debts = One(Rule(x => x.MaxDebtCredits = 30), s, [.. first, (B1, 12, 2), (C1, 4, 2)], second: true);
        Assert.Equal(LmdOutcome.AdmisAvecDettes, LmdResultsCalculator.Final(Rule(x => x.MaxDebtCredits = 30), debts));
        Assert.Equal(["UEC"], debts.MissingUnits.Select(u => u.Code).ToArray());
    }

    [Fact]
    public void Ranks_and_scale_are_checked()
    {
        var (s1, s2) = (Guid.NewGuid(), Guid.NewGuid());
        var marks = new[] { (s1, 14m), (s2, 12m) }.SelectMany(x => new[] { A1, A2, B1, C1 }
            .Select(ec => new LmdMarkEntry(x.Item1, ec.Id, 1, x.Item2))).ToList();
        var results = LmdResultsCalculator.Compute(Rule(), [A, B, C], [A1, A2, B1, C1], [s1, s2], marks);
        Assert.Equal(1, results.Single(r => r.EnrollmentId == s1).Rank);
        Assert.Equal(2, results.Single(r => r.EnrollmentId == s2).Rank);
        Assert.Throws<DomainException>(() => LmdResultsCalculator.Compute(Rule(), [A, B, C], [A1, A2, B1, C1], [s1],
            [new LmdMarkEntry(s1, A1.Id, 1, 21)]));
        var draft = AcademicRegulationRules.DefaultFor(RegulationScope.SuperieurLmd, Guid.NewGuid());
        Assert.Throws<DomainException>(() => LmdResultsCalculator.Compute(draft, [A], [A1], [s1], []));
    }
}
