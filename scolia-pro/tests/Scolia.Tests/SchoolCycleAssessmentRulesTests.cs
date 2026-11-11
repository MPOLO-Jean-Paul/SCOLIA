using Scolia.Domain.Common;
using Scolia.Domain.Deliberation;
using Scolia.Domain.School;
using Scolia.Domain.Students;

namespace Scolia.Tests;

public sealed class SchoolCycleAssessmentRulesTests
{
    private static (SchoolCycle Cycle, SchoolCyclePeriod Period, Promotion Class,
        Enrollment Student, CurriculumItem Subject, SchoolCycleAssessment Entry)
        Build(SchoolCycleKind kind)
    {
        var cycle = new SchoolCycle { Kind = kind, Name = kind.ToString(), SchoolId = Guid.NewGuid() };
        var yearId = Guid.NewGuid();
        var promotion = new Promotion
        {
            SchoolCycleId = cycle.Id, AcademicYearId = yearId,
            Code = "P001", Name = "Première année",
        };
        var enrollment = new Enrollment
        {
            AcademicYearId = yearId, PromotionId = promotion.Id, StudentId = Guid.NewGuid(),
        };
        var mode = kind == SchoolCycleKind.Maternelle
            ? SchoolCycleAssessmentMode.Observation : SchoolCycleAssessmentMode.Evaluation;
        var period = new SchoolCyclePeriod
        {
            SchoolCycleId = cycle.Id, AcademicYearId = yearId,
            Code = "P1", Name = "Période 1", TermCode = "T1", Order = 1,
            Mode = mode, StartDate = new(2026, 9, 1), EndDate = new(2026, 10, 15),
        };
        var subject = new CurriculumItem
        {
            PromotionId = promotion.Id, AcademicYearId = yearId,
            Kind = CurriculumItemKind.AnnualSubject,
            Semester = CourseSemester.Annuel,
            Name = "Lecture", Code = "LECT",
        };
        var entry = new SchoolCycleAssessment
        {
            SchoolCycleId = cycle.Id, SchoolCyclePeriodId = period.Id,
            PromotionId = promotion.Id, AcademicYearId = yearId,
            EnrollmentId = enrollment.Id, CurriculumItemId = subject.Id,
            Mode = mode, EvaluationCode = "LECT_1", Title = "Lecture",
            Score = kind == SchoolCycleKind.Maternelle ? null : 12m,
            MaxScore = kind == SchoolCycleKind.Maternelle ? null : 20m,
            Observation = kind == SchoolCycleKind.Maternelle
                ? "Reconnaît les images et communique ses observations." : null,
        };
        return (cycle, period, promotion, enrollment, subject, entry);
    }

    [Fact]
    public void Nursery_accepts_descriptive_observation_without_a_grade()
    {
        var x = Build(SchoolCycleKind.Maternelle);
        SchoolCycleAssessmentRules.Validate(x.Cycle, x.Period, x.Class, x.Student, x.Subject, x.Entry);
        Assert.Null(x.Entry.Score);
        Assert.NotNull(x.Entry.Observation);
    }

    [Fact]
    public void Nursery_allows_a_free_activity_without_curriculum_item()
    {
        var x = Build(SchoolCycleKind.Maternelle);
        x.Entry.CurriculumItemId = null;
        SchoolCycleAssessmentRules.Validate(x.Cycle, x.Period, x.Class, x.Student, null, x.Entry);
    }

    [Fact]
    public void Nursery_never_converts_an_observation_into_a_grade()
    {
        var x = Build(SchoolCycleKind.Maternelle);
        x.Entry.Score = 15;
        x.Entry.MaxScore = 20;
        Assert.Throws<DomainException>(() =>
            SchoolCycleAssessmentRules.Validate(x.Cycle, x.Period, x.Class, x.Student, x.Subject, x.Entry));
    }

    [Fact]
    public void Nursery_requires_an_observation()
    {
        var x = Build(SchoolCycleKind.Maternelle);
        x.Entry.Observation = " ";
        Assert.Throws<DomainException>(() =>
            SchoolCycleAssessmentRules.Validate(x.Cycle, x.Period, x.Class, x.Student, x.Subject, x.Entry));
    }

    [Theory]
    [InlineData(SchoolCycleKind.Primaire)]
    [InlineData(SchoolCycleKind.Secondaire)]
    public void School_grade_accepts_a_value_inside_its_custom_maximum(SchoolCycleKind kind)
    {
        var x = Build(kind);
        SchoolCycleAssessmentRules.Validate(x.Cycle, x.Period, x.Class, x.Student, x.Subject, x.Entry);
    }

    [Theory]
    [InlineData(SchoolCycleKind.Primaire)]
    [InlineData(SchoolCycleKind.Secondaire)]
    public void School_grade_rejects_scores_beyond_declared_maximum(SchoolCycleKind kind)
    {
        var x = Build(kind);
        x.Entry.Score = 21;
        Assert.Throws<DomainException>(() =>
            SchoolCycleAssessmentRules.Validate(x.Cycle, x.Period, x.Class, x.Student, x.Subject, x.Entry));
    }

    [Fact]
    public void School_grade_can_be_not_scored_yet_without_forcing_zero()
    {
        var x = Build(SchoolCycleKind.Secondaire);
        x.Entry.Score = null;
        SchoolCycleAssessmentRules.Validate(x.Cycle, x.Period, x.Class, x.Student, x.Subject, x.Entry);
    }

    [Fact]
    public void School_grade_cannot_reference_a_subject_of_another_class()
    {
        var x = Build(SchoolCycleKind.Primaire);
        x.Subject.PromotionId = Guid.NewGuid();
        Assert.Throws<DomainException>(() =>
            SchoolCycleAssessmentRules.Validate(x.Cycle, x.Period, x.Class, x.Student, x.Subject, x.Entry));
    }

    [Fact]
    public void An_evaluation_cannot_cross_school_cycles_even_inside_one_complex()
    {
        var x = Build(SchoolCycleKind.Secondaire);
        x.Period.SchoolCycleId = Guid.NewGuid();
        Assert.Throws<DomainException>(() =>
            SchoolCycleAssessmentRules.Validate(x.Cycle, x.Period, x.Class, x.Student, x.Subject, x.Entry));
    }

    [Fact]
    public void An_evaluation_cannot_cross_school_years()
    {
        var x = Build(SchoolCycleKind.Primaire);
        x.Student.AcademicYearId = Guid.NewGuid();
        Assert.Throws<DomainException>(() =>
            SchoolCycleAssessmentRules.Validate(x.Cycle, x.Period, x.Class, x.Student, x.Subject, x.Entry));
    }

    [Fact]
    public void A_bulletin_preview_has_no_automatic_average_rank_or_pass_fail_decision()
    {
        var x = Build(SchoolCycleKind.Primaire);
        var report = SchoolCycleAssessmentRules.Preview(x.Cycle, x.Student.Id, new[] { x.Period },
            new[] { x.Entry });
        var row = Assert.Single(report);
        Assert.Equal("P1", row.PeriodCode);
        Assert.Equal(12m, row.Score);
        Assert.Equal(20m, row.MaxScore);
        Assert.DoesNotContain("moyenne", string.Join(" ", typeof(SchoolCycleAssessmentPreviewLine)
            .GetProperties().Select(p => p.Name)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_bulletin_preview_rejects_another_student_and_duplicate_activity()
    {
        var x = Build(SchoolCycleKind.Maternelle);
        Assert.Throws<DomainException>(() => SchoolCycleAssessmentRules.Preview(
            x.Cycle, Guid.NewGuid(), new[] { x.Period }, new[] { x.Entry }));
        var duplicate = new SchoolCycleAssessment
        {
            SchoolCycleId = x.Cycle.Id, AcademicYearId = x.Entry.AcademicYearId,
            EnrollmentId = x.Student.Id, SchoolCyclePeriodId = x.Period.Id,
            EvaluationCode = x.Entry.EvaluationCode.ToLowerInvariant(),
            Mode = SchoolCycleAssessmentMode.Observation,
        };
        Assert.Throws<DomainException>(() => SchoolCycleAssessmentRules.Preview(
            x.Cycle, x.Student.Id, new[] { x.Period }, new[] { x.Entry, duplicate }));
    }
}
