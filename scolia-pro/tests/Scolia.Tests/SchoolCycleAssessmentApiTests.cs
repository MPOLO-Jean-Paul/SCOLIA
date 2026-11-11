using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scolia.Application.Abstractions;
using Scolia.Domain.School;
using Scolia.Domain.Students;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Tests;

/// <summary>
/// Non-régression : jamais d'écriture pédagogique sur une ancienne base ITM.
/// Les aperçus restent non officiels et liés à la bonne inscription.
/// </summary>
public sealed class SchoolCycleAssessmentApiTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    [Fact]
    public async Task Itm_school_has_an_empty_separate_assessment_table_and_cannot_open_cycle_bulletins()
    {
        var admin = await app.InstalledAdmin();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        Assert.Empty(await db.SchoolCycleAssessments.ToListAsync());
        Assert.Equal(10, await db.Periods.CountAsync());

        var enrollmentId = Guid.NewGuid();
        var (status, problem) = await admin.GetAsync(
            $"/api/etablissement/cycles/evaluations/{enrollmentId}/apercu").Fail();
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("evaluationCycle", problem.GetProperty("code").GetString());

        var (writeStatus, _) = await admin.Post(
            $"/api/etablissement/cycles/evaluations/{enrollmentId}", new
            {
                schoolCyclePeriodId = Guid.NewGuid(), evaluationCode = "MAT1",
                title = "Communication", curriculumItemId = (Guid?)null,
                observation = "Participe aux échanges.", score = (decimal?)null,
                maxScore = (decimal?)null, comment = (string?)null,
            }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, writeStatus);
        Assert.Empty(await db.SchoolCycleAssessments.ToListAsync());
    }

    [Fact]
    public async Task Nursery_preview_keeps_observations_without_a_grade_or_pass_mark()
    {
        var admin = await app.InstalledAdmin();
        Guid enrollmentId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
            var school = await db.Schools.SingleAsync();
            var year = await db.AcademicYears.SingleAsync(y => y.IsCurrent);
            school.InstitutionType = InstitutionProfiles.ComplexeScolaire;
            var nursery = new SchoolCycle
            {
                SchoolId = school.Id, Kind = SchoolCycleKind.Maternelle,
                Name = "Maternelle",
            };
            var primary = new SchoolCycle
            {
                SchoolId = school.Id, Kind = SchoolCycleKind.Primaire,
                Name = "Primaire",
            };
            var classroom = new Promotion
            {
                AcademicYearId = year.Id, SchoolCycleId = nursery.Id,
                Code = "MAT1", Name = "Première maternelle", Level = 1,
            };
            var pupil = new Student
            {
                Matricule = "MAT901", LastName = "KALALA",
                MiddleName = "MULUMBA", FirstName = "Ada",
            };
            pupil.RefreshKeys();
            var enrollment = new Enrollment
            {
                StudentId = pupil.Id, AcademicYearId = year.Id,
                PromotionId = classroom.Id, EnrolledOn = year.StartDate,
            };
            enrollmentId = enrollment.Id;
            var period = new SchoolCyclePeriod
            {
                SchoolCycleId = nursery.Id, AcademicYearId = year.Id,
                Code = "P1", Name = "Observation initiale", TermCode = "T1",
                Mode = SchoolCycleAssessmentMode.Observation, Order = 1,
                StartDate = year.StartDate, EndDate = year.StartDate.AddDays(12),
            };
            var record = new SchoolCycleAssessment
            {
                AcademicYearId = year.Id, SchoolCycleId = nursery.Id,
                PromotionId = classroom.Id, EnrollmentId = enrollment.Id,
                SchoolCyclePeriodId = period.Id, EvaluationCode = "COMM",
                Title = "Communication", Mode = SchoolCycleAssessmentMode.Observation,
                Observation = "Participe aux échanges et suit les consignes.",
            };
            db.SchoolCycles.AddRange(nursery, primary);
            db.Promotions.Add(classroom);
            db.Students.Add(pupil);
            db.Enrollments.Add(enrollment);
            db.SchoolCyclePeriods.Add(period);
            db.SchoolCycleAssessments.Add(record);
            scope.ServiceProvider.GetRequiredService<IAuditLog>().Record(
                "tests.evaluations.cycles", "EvaluationCycle", record.Id.ToString(),
                "Vérification de migration et aperçu pédagogique en base de test.");
            await db.SaveChangesAsync();
        }

        var json = await admin.GetAsync(
            $"/api/etablissement/cycles/evaluations/{enrollmentId}/apercu").Ok();
        Assert.False(json.GetProperty("isOfficial").GetBoolean());
        Assert.Equal("Maternelle", json.GetProperty("cycle").GetString());
        var row = Assert.Single(json.GetProperty("entries").EnumerateArray());
        Assert.Equal("Observation", row.GetProperty("mode").GetString());
        Assert.Equal("P1", row.GetProperty("periodCode").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, row.GetProperty("score").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, row.GetProperty("maxScore").ValueKind);
        Assert.Contains("Participe", row.GetProperty("observation").GetString() ?? string.Empty);

        // Même si un profil de test est préparé, aucune écriture API n'est
        // possible avant l'activation officielle des nouveaux moteurs.
        var (status, _) = await admin.Post(
            $"/api/etablissement/cycles/evaluations/{enrollmentId}", new
            {
                schoolCyclePeriodId = row.GetProperty("schoolCyclePeriodId").GetGuid(),
                evaluationCode = "COMM2", title = "Communication",
                curriculumItemId = (Guid?)null, observation = "En progrès.",
                score = (decimal?)null, maxScore = (decimal?)null,
                comment = (string?)null,
            }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }
}
