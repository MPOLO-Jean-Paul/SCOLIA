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

    [Fact]
    public void Jury_evaluations_count_only_at_the_annual_jury_with_official_totals()
    {
        var stage = new CourseInput(Guid.NewGuid(), CourseSemester.JuryEpreuve, 150, null, null, null, Jury: 120);
        var bloc = new CourseInput(Guid.NewGuid(), CourseSemester.JuryBloc, 60, null, null, 10, Jury: 20);
        var course = C(CourseSemester.S1, 100, s1: 70);

        // Au S1, les épreuves du jury ne comptent pas ; au jury annuel, elles comptent, sans appui.
        Assert.Equal(100m, Student([stage, bloc, course], DeliberationPeriod.S1).Max);
        var final = Student([stage, bloc, course], DeliberationPeriod.Annuel);
        Assert.Equal(310m, final.Max);
        Assert.Equal(210m, final.Points);
        Assert.Equal(1, final.Failed); // le bloc : 20 < 30, l'appui ne s'applique pas aux épreuves du jury
        Assert.Equal(Outcome.Admis, final.Outcome);
        Assert.Equal("A", Grade(final.Outcome, final.Percent, final.Failed));

        var semesters = new Dictionary<Guid, CourseSemester> { [stage.CourseId] = stage.Semester, [bloc.CourseId] = bloc.Semester, [course.CourseId] = course.Semester };
        var totals = Totals(final.Courses.Select(r => (semesters[r.CourseId], r)));
        Assert.Equal((120m, 150m), (totals.Total1, totals.Total1Max));
        Assert.Equal((70m, 100m), (totals.Subtotal1, totals.Subtotal1Max));
        Assert.Equal((20m, 60m), (totals.Subtotal2, totals.Subtotal2Max));
        Assert.Equal((90m, 160m), (totals.Total2, totals.Total2Max));
        Assert.Equal((210m, 310m), (totals.General, totals.GeneralMax));

        // Cotes : une épreuve ne reçoit que la cote du jury, un cours jamais.
        Assert.True(Accepts(CourseSemester.JuryEpreuve, MarkKind.Jury));
        Assert.False(Accepts(CourseSemester.JuryEpreuve, MarkKind.CoteS1));
        Assert.False(Accepts(CourseSemester.JuryBloc, MarkKind.Appui));
        Assert.False(Accepts(CourseSemester.Annuel, MarkKind.Jury));
        Assert.Throws<DomainException>(() => ValidateMark(MarkKind.Jury, CourseSemester.JuryEpreuve, 151, 150));
        Assert.True(Locks(DeliberationPeriod.Annuel, MarkKind.Jury, CourseSemester.JuryEpreuve));
        Assert.False(Locks(DeliberationPeriod.S1, MarkKind.Jury, CourseSemester.JuryEpreuve));
    }

    [Fact]
    public void Official_codes_follow_the_deliberation_project()
    {
        Assert.Equal("P", Grade(Outcome.Admis, 75, 0));
        Assert.Equal("A", Grade(Outcome.Admis, 62, 2));
        Assert.Equal("E", Grade(Outcome.Ajourne, 59.99m, 0));
        Assert.Equal("E", Grade(Outcome.Incomplet, 40, 1));
        Assert.Equal(string.Empty, Grade(Outcome.Incomplet, 70, 0));
    }
}
