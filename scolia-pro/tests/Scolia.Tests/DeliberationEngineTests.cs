using Scolia.Domain.Common;
using Scolia.Domain.Deliberation;
using static Scolia.Domain.Deliberation.DeliberationEngine;

namespace Scolia.Tests;

/// <summary>Moteur de délibération : cas de référence du dossier ITMCD Pro (§ 5.2) et règles de config/db.php.</summary>
public sealed class DeliberationEngineTests
{
    private static CourseInput C(CourseSemester semester, decimal max, decimal? s1 = null, decimal? s2 = null, decimal? support = null) =>
        new(Guid.NewGuid(), semester, max, s1, s2, support);

    [Fact]
    public void Reference_cases_match_itmcd()
    {
        // Cours S1 sans appui : 14 / 20, réussi.
        var plain = Course(C(CourseSemester.S1, 20, s1: 14), DeliberationPeriod.S1);
        Assert.Equal(14m, plain.Points);
        Assert.False(plain.Failed);

        // Appui appliqué : 12 + 0,7 × 5 = 15,5.
        Assert.Equal(15.5m, Course(C(CourseSemester.S1, 20, s1: 12, support: 7), DeliberationPeriod.S1).Points);
        // Appui sans effet au-delà de 75 % du maxima.
        Assert.Equal(16m, Course(C(CourseSemester.S1, 20, s1: 16, support: 10), DeliberationPeriod.S1).Points);
        // Appui plafonné au maxima : 15 + 5 = 20.
        Assert.Equal(20m, Course(C(CourseSemester.S1, 20, s1: 15, support: 10), DeliberationPeriod.S1).Points);
        // Exemple d'ITMCD : maxima 40, cote 20, moyenne 8 → 28.
        Assert.Equal(28m, Course(C(CourseSemester.S1, 40, s1: 20, support: 8), DeliberationPeriod.S1).Points);
        // Cours annuel au final : (30 + 20) / 2 = 25 / 40.
        Assert.Equal(25m, Course(C(CourseSemester.Annuel, 40, s1: 30, s2: 20), DeliberationPeriod.Annuel).Points);
    }

    [Fact]
    public void Complete_student_is_admitted_with_mention_and_missing_grade_is_incomplete()
    {
        var complete = Student([C(CourseSemester.S1, 100, s1: 70), C(CourseSemester.S1, 50, s1: 30), C(CourseSemester.Annuel, 50, s1: 30, s2: 30)], DeliberationPeriod.Annuel);
        Assert.Equal(130m, complete.Points);
        Assert.Equal(200m, complete.Max);
        Assert.Equal(65m, complete.Percent);
        Assert.Equal(Outcome.Admis, complete.Outcome);
        Assert.Equal("D", complete.Mention);

        var missing = Student([C(CourseSemester.S1, 20, s1: 18), C(CourseSemester.S1, 20, s1: 18), C(CourseSemester.S1, 20)], DeliberationPeriod.S1);
        Assert.Equal(1, missing.Missing);
        Assert.Equal(Outcome.Incomplet, missing.Outcome);
        // À la clôture, sans décision du jury, un dossier incomplet est ajourné ; le jury peut décider autrement.
        Assert.Equal(Outcome.Ajourne, Final(missing.Outcome, null, closing: true));
        Assert.Equal(Outcome.Admis, Final(missing.Outcome, Outcome.Admis, closing: true));
    }

    [Fact]
    public void Periods_select_their_courses_and_annual_needs_both_semesters()
    {
        var s1 = C(CourseSemester.S1, 20, s1: 10);
        var s2 = C(CourseSemester.S2, 20, s2: 10);
        var annual = C(CourseSemester.Annuel, 20, s1: 16);
        Assert.Equal(2, Student([s1, s2, annual], DeliberationPeriod.S1).Courses.Count);
        Assert.Equal(2, Student([s1, s2, annual], DeliberationPeriod.S2).Courses.Count);
        Assert.Equal(3, Student([s1, s2, annual], DeliberationPeriod.Annuel).Courses.Count);

        // ITMCD comptait un cours annuel encodé seulement au S1 comme S1 / 2 : il est désormais « cote manquante ».
        var final = Course(annual, DeliberationPeriod.Annuel);
        Assert.False(final.Graded);
        Assert.Equal(Outcome.Incomplet, Student([annual], DeliberationPeriod.Annuel).Outcome);
    }

    [Fact]
    public void Course_fails_under_half_and_decision_needs_sixty_percent()
    {
        Assert.True(Course(C(CourseSemester.S1, 20, s1: 9.99m), DeliberationPeriod.S1).Failed);
        Assert.False(Course(C(CourseSemester.S1, 20, s1: 10), DeliberationPeriod.S1).Failed);
        Assert.Equal(Outcome.Ajourne, Student([C(CourseSemester.S1, 100, s1: 59.99m)], DeliberationPeriod.S1).Outcome);
        Assert.Equal(Outcome.Admis, Student([C(CourseSemester.S1, 100, s1: 60)], DeliberationPeriod.S1).Outcome);
        Assert.Equal(["A", "B", "C", "D", "E"], new[] { 90m, 80m, 70m, 60m, 59.99m }.Select(Mention));
        // L'appui désactivé n'apporte rien.
        Assert.Equal(12m, Course(C(CourseSemester.S1, 20, s1: 12, support: 10), DeliberationPeriod.S1, supportEnabled: false).Points);
    }

    [Fact]
    public void Ranks_share_ties_and_skip_incomplete_files()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid(); var d = Guid.NewGuid();
        var ranks = Ranks([(a, 80m, true), (b, 80m, true), (c, 70m, true), (d, 95m, false)]);
        Assert.Equal(1, ranks[a]);
        Assert.Equal(1, ranks[b]);
        Assert.Equal(3, ranks[c]);
        Assert.False(ranks.ContainsKey(d));
    }

    [Fact]
    public void Marks_are_validated_against_course_organisation_and_limits()
    {
        Assert.Throws<DomainException>(() => ValidateMark(MarkKind.CoteS2, CourseSemester.S1, 10, 20));
        Assert.Throws<DomainException>(() => ValidateMark(MarkKind.CoteS1, CourseSemester.S1, 20.5m, 20));
        Assert.Throws<DomainException>(() => ValidateMark(MarkKind.Appui, CourseSemester.S1, 11, 20));
        Assert.Throws<DomainException>(() => ValidateMark(MarkKind.CoteS1, CourseSemester.Annuel, 1.234m, 20));
        Assert.Equal(19.5m, ValidateMark(MarkKind.CoteS1, CourseSemester.Annuel, 19.5m, 20));

        Assert.True(Locks(DeliberationPeriod.Annuel, MarkKind.CoteS2, CourseSemester.Annuel));
        Assert.False(Locks(DeliberationPeriod.S1, MarkKind.CoteS2, CourseSemester.Annuel));
        Assert.True(Locks(DeliberationPeriod.S1, MarkKind.Appui, CourseSemester.Annuel));
        Assert.False(Locks(DeliberationPeriod.S1, MarkKind.Appui, CourseSemester.S2));
    }

    [Fact]
    public void Anonymous_codes_follow_the_itmcd_format_and_are_unique()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 500; i++)
        {
            var code = AnonymousCodeRules.Generate(taken);
            Assert.Matches("^[A-HJ-NP-Z][0-9]{2}[A-HJ-NP-Z][0-9][A-HJ-NP-Z]$", code);
            Assert.True(taken.Add(code));
        }
        Assert.Equal("ITM-26-AB12", AnonymousCodeRules.Official(" itm-26-ab12 "));
        Assert.Equal("PJ13WR", AnonymousCodeRules.Official("pj 13 wr"));
        Assert.Throws<DomainException>(() => AnonymousCodeRules.Official("AB"));
        Assert.Throws<DomainException>(() => AnonymousCodeRules.Official("AB_12"));
        Assert.Throws<DomainException>(() => AnonymousCodeRules.Official("-AB12"));
    }
}
