using System.Text;
using Scolia.Application.Dtos;
using Scolia.Infrastructure.Documents;

namespace Scolia.Tests;

/// <summary>Le PDF reste un relevé de consultation, jamais un bulletin certifié.</summary>
public sealed class SchoolCycleBulletinPdfTests
{
    private static SchoolCycleBulletinPrintDto BaseDocument(
        IReadOnlyList<SchoolCycleBulletinTermDto> terms,
        IReadOnlyList<SchoolCycleBulletinSubjectDto>? subjects = null)
    {
        var preview = new SchoolCycleBulletinPreviewDto(
            Guid.NewGuid(), Guid.NewGuid(),
            subjects is null ? "Maternelle" : "Primaire",
            subjects is null ? "Première maternelle" : "Première primaire",
            "2026-2027", [],
            Terms: terms,
            DocumentTitle: subjects is null ? "Carnet d'observations" : "Relevé des évaluations",
            SubjectRows: subjects);
        return new SchoolCycleBulletinPrintDto(
            "Complexe scolaire de démonstration", "Avenue de l'école", "Lubumbashi",
            "KALALA MUKENDI Ada", "ELA001", preview);
    }

    [Fact]
    public void Nursery_observations_produce_a_readable_pdf_without_scores()
    {
        var periodId = Guid.NewGuid();
        var observations = new[]
        {
            new SchoolCycleAssessmentItemDto(
                Guid.NewGuid(), periodId, "P1", "COM", "Communication",
                "Observation", null, "Participe aux échanges.", null, null, null, 1),
        };
        var terms = new[]
        {
            new SchoolCycleBulletinTermDto("T1", "Période d'observation T1",
            [
                new SchoolCycleBulletinPeriodDto(
                    periodId, "P1", "Première observation", "T1",
                    new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 30),
                    "Observation", observations),
            ]),
        };
        var bytes = SchoolCycleBulletinPdf.Build(BaseDocument(terms));
        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public void Primary_branch_grid_with_an_empty_period_produces_a_pdf()
    {
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var grade = new SchoolCycleAssessmentItemDto(
            Guid.NewGuid(), p1, "P1", "FR1", "Français",
            "Evaluation", subjectId, null, 14m, 20m, null, 1);
        var terms = new[]
        {
            new SchoolCycleBulletinTermDto("T1", "Trimestre T1",
            [
                new SchoolCycleBulletinPeriodDto(
                    p1, "P1", "Première période", "T1",
                    new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 30),
                    "Evaluation", [grade]),
                new SchoolCycleBulletinPeriodDto(
                    p2, "P2", "Deuxième période", "T1",
                    new DateOnly(2026, 10, 31), new DateOnly(2026, 12, 20),
                    "Evaluation", []),
            ]),
        };
        var subjects = new[]
        {
            new SchoolCycleBulletinSubjectDto(
                subjectId, "FR", "Français", 1, true,
                [
                    new SchoolCycleBulletinSubjectPeriodDto(p1, "P1", "Première période", [grade]),
                    new SchoolCycleBulletinSubjectPeriodDto(p2, "P2", "Deuxième période", []),
                ]),
            new SchoolCycleBulletinSubjectDto(
                Guid.NewGuid(), "MATH", "Mathématiques", 1, true,
                [
                    new SchoolCycleBulletinSubjectPeriodDto(p1, "P1", "Première période", []),
                    new SchoolCycleBulletinSubjectPeriodDto(p2, "P2", "Deuxième période", []),
                ]),
        };
        var bytes = SchoolCycleBulletinPdf.Build(BaseDocument(terms, subjects));
        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public void The_generator_refuses_a_preview_marked_official()
    {
        var document = BaseDocument([]);
        Assert.Throws<InvalidOperationException>(() =>
            SchoolCycleBulletinPdf.Build(document with
            {
                Preview = document.Preview with { IsOfficial = true },
            }));
    }
}
