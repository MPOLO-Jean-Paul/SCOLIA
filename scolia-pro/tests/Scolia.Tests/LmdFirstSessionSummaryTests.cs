using Scolia.Domain.Common;
using Scolia.Domain.Deliberation;
using Scolia.Domain.School;

namespace Scolia.Tests;

public sealed class LmdFirstSessionSummaryTests
{
    [Fact]
    public void S1_results_are_available_before_S2_but_annual_result_is_not_published()
    {
        var marks = new[]
        {
            Ec(CourseSemester.S1, 4, 12),
            Ec(CourseSemester.S1, 2, 16),
        };
        var result = LmdFirstSessionCalculator.FirstSession(marks);
        Assert.True(result.S1.IsComplete);
        Assert.Equal(6, result.S1.PlannedCredits);
        Assert.Equal(13.33m, result.S1.AverageOutOf20);
        Assert.False(result.S2.IsComplete);
        Assert.False(result.IsComplete);
        Assert.Null(result.AnnualAverageOutOf20);
    }

    [Fact]
    public void First_session_consolidates_both_semesters_without_admission_decision()
    {
        var marks = new[]
        {
            Ec(CourseSemester.S1, 4, 12),
            Ec(CourseSemester.S1, 2, 16),
            Ec(CourseSemester.S2, 3, 10),
            Ec(CourseSemester.S2, 3, 14),
        };
        var result = LmdFirstSessionCalculator.FirstSession(marks);
        Assert.True(result.IsComplete);
        Assert.Equal(13.33m, result.S1.AverageOutOf20);
        Assert.Equal(12m, result.S2.AverageOutOf20);
        Assert.Equal(12.67m, result.AnnualAverageOutOf20);
    }

    [Fact]
    public void Missing_grade_keeps_semester_and_annual_results_incomplete()
    {
        var marks = new[]
        {
            Ec(CourseSemester.S1, 3, 13),
            Ec(CourseSemester.S1, 3, null),
            Ec(CourseSemester.S2, 6, 15),
        };
        var result = LmdFirstSessionCalculator.FirstSession(marks);
        Assert.False(result.S1.IsComplete);
        Assert.Equal(3, result.S1.EvaluatedCredits);
        Assert.Equal(13m, result.S1.AverageOutOf20);
        Assert.False(result.IsComplete);
        Assert.Null(result.AnnualAverageOutOf20);
    }

    [Fact]
    public void Annual_or_duplicate_elements_are_rejected()
    {
        var sameId = Guid.NewGuid();
        Assert.Throws<DomainException>(() => LmdFirstSessionCalculator.FirstSession(
            new[] { new LmdElementMark(Guid.NewGuid(), CourseSemester.Annuel, 3, 14) }));
        Assert.Throws<DomainException>(() => LmdFirstSessionCalculator.FirstSession(
            new[]
            {
                new LmdElementMark(sameId, CourseSemester.S1, 3, 12),
                new LmdElementMark(sameId, CourseSemester.S2, 3, 14),
            }));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public void Invalid_marks_are_rejected(decimal mark)
    {
        Assert.Throws<DomainException>(() => LmdFirstSessionCalculator.Semester(
            CourseSemester.S1, new[] { Ec(CourseSemester.S1, 3, mark) }));
    }

    private static LmdElementMark Ec(CourseSemester semester, decimal credits, decimal? mark) =>
        new(Guid.NewGuid(), semester, credits, mark);
}
