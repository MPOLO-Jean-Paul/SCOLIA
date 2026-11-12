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

        var (pdfStatus, _) = await admin.GetAsync(
            $"/api/etablissement/cycles/evaluations/{enrollmentId}/apercu.pdf").Fail();
        Assert.Equal(HttpStatusCode.BadRequest, pdfStatus);

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

    [Theory]
    [InlineData(SchoolCycleKind.Primaire, "Trimestre", "T1", "T2")]
    [InlineData(SchoolCycleKind.Secondaire, "Semestre", "S1", "S2")]
    public async Task Primary_and_secondary_previews_keep_separate_terms_and_ungraded_periods(
        SchoolCycleKind kind, string prefix, string firstTerm, string secondTerm)
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
                SchoolId = school.Id, Kind = SchoolCycleKind.Maternelle, Name = "Maternelle",
            };
            var cycle = new SchoolCycle
            {
                SchoolId = school.Id, Kind = kind, Name = kind.ToString(),
            };
            var classroom = new Promotion
            {
                AcademicYearId = year.Id, SchoolCycleId = cycle.Id,
                Code = kind == SchoolCycleKind.Primaire ? "PRI1" : "SEC1",
                Name = "Première année", Level = 1,
            };
            var pupil = new Student
            {
                Matricule = kind == SchoolCycleKind.Primaire ? "PRV901" : "SEV901",
                LastName = "KALALA", MiddleName = "MULUMBA", FirstName = "Ada",
            };
            pupil.RefreshKeys();
            var enrollment = new Enrollment
            {
                StudentId = pupil.Id, AcademicYearId = year.Id,
                PromotionId = classroom.Id, EnrolledOn = year.StartDate,
            };
            enrollmentId = enrollment.Id;
            var period1 = new SchoolCyclePeriod
            {
                SchoolCycleId = cycle.Id, AcademicYearId = year.Id,
                Code = "P1", Name = "Première période", TermCode = firstTerm,
                Mode = SchoolCycleAssessmentMode.Evaluation, Order = 1,
                StartDate = year.StartDate, EndDate = year.StartDate.AddDays(20),
            };
            var period2 = new SchoolCyclePeriod
            {
                SchoolCycleId = cycle.Id, AcademicYearId = year.Id,
                Code = "P2", Name = "Deuxième période", TermCode = secondTerm,
                Mode = SchoolCycleAssessmentMode.Evaluation, Order = 2,
                StartDate = year.StartDate.AddDays(21), EndDate = year.StartDate.AddDays(41),
            };
            var subject = new CurriculumItem
            {
                AcademicYearId = year.Id, PromotionId = classroom.Id,
                Kind = CurriculumItemKind.AnnualSubject, Name = "Français",
                Code = "FRANC", Coefficient = 1,
            };
            var ungradedSubject = new CurriculumItem
            {
                AcademicYearId = year.Id, PromotionId = classroom.Id,
                Kind = CurriculumItemKind.AnnualSubject,
                Name = "Mathématiques", Code = "MATH", Coefficient = 1,
            };
            // Une ancienne branche archivée avec cote reste consultable.
            subject.IsActive = false;
            var grade = new SchoolCycleAssessment
            {
                AcademicYearId = year.Id, SchoolCycleId = cycle.Id,
                PromotionId = classroom.Id, EnrollmentId = enrollment.Id,
                SchoolCyclePeriodId = period1.Id, EvaluationCode = "FR01",
                CurriculumItemId = subject.Id, Title = "Français",
                Mode = SchoolCycleAssessmentMode.Evaluation,
                Score = 12m, MaxScore = 20m,
            };
            db.SchoolCycles.AddRange(nursery, cycle);
            db.Promotions.Add(classroom);
            db.Students.Add(pupil);
            db.Enrollments.Add(enrollment);
            db.SchoolCyclePeriods.AddRange(period1, period2);
            db.CurriculumItems.AddRange(subject, ungradedSubject);
            db.SchoolCycleAssessments.Add(grade);
            scope.ServiceProvider.GetRequiredService<IAuditLog>().Record(
                "tests.bulletins.cycles", "EvaluationCycle", grade.Id.ToString(),
                "Contrôle des termes scolaires dans une base isolée.");
            await db.SaveChangesAsync();
        }

        var json = await admin.GetAsync(
            $"/api/etablissement/cycles/evaluations/{enrollmentId}/apercu").Ok();
        Assert.False(json.GetProperty("isOfficial").GetBoolean());
        var presentation = SchoolBulletinLayouts.For(kind, 1);
        Assert.Equal(presentation.Code, json.GetProperty("templateCode").GetString());
        Assert.Equal(presentation.PreviewTitle, json.GetProperty("documentTitle").GetString());
        Assert.Equal(presentation.EntryHeading, json.GetProperty("entryHeading").GetString());
        var terms = json.GetProperty("terms").EnumerateArray().ToArray();
        Assert.Equal(2, terms.Length);
        Assert.Equal(firstTerm, terms[0].GetProperty("code").GetString());
        Assert.Equal(secondTerm, terms[1].GetProperty("code").GetString());
        Assert.StartsWith(prefix, terms[0].GetProperty("label").GetString());
        var firstPeriod = Assert.Single(terms[0].GetProperty("periods").EnumerateArray());
        var score = Assert.Single(firstPeriod.GetProperty("entries").EnumerateArray());
        Assert.Equal(12m, score.GetProperty("score").GetDecimal());
        Assert.Equal(20m, score.GetProperty("maxScore").GetDecimal());
        var secondPeriod = Assert.Single(terms[1].GetProperty("periods").EnumerateArray());
        Assert.Equal("P2", secondPeriod.GetProperty("code").GetString());
        Assert.Empty(secondPeriod.GetProperty("entries").EnumerateArray());
        Assert.DoesNotContain("moyenne", json.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, json.GetProperty("entries").GetArrayLength());
        Assert.Equal(1, json.GetProperty("classLevel").GetInt32());
        Assert.Equal(presentation.GradeBand, json.GetProperty("gradeBand").GetString());
        var rows = json.GetProperty("subjectRows").EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        var subjectRow = Assert.Single(rows, item =>
            item.GetProperty("subjectName").GetString() == "Français");
        var ungraded = Assert.Single(rows, item =>
            item.GetProperty("subjectName").GetString() == "Mathématiques");
        Assert.False(subjectRow.GetProperty("isActive").GetBoolean());
        Assert.True(ungraded.GetProperty("isActive").GetBoolean());
        Assert.All(ungraded.GetProperty("periods").EnumerateArray().ToArray(), period =>
            Assert.Empty(period.GetProperty("entries").EnumerateArray()));
        var subjectPeriods = subjectRow.GetProperty("periods").EnumerateArray().ToArray();
        Assert.Equal(2, subjectPeriods.Length);
        var firstSubjectScore = Assert.Single(subjectPeriods[0].GetProperty("entries").EnumerateArray());
        Assert.Equal(12m, firstSubjectScore.GetProperty("score").GetDecimal());
        Assert.Empty(subjectPeriods[1].GetProperty("entries").EnumerateArray());

        // Test complet de la route réellement consommée par Avalonia :
        // succès PDF, pas de faux document officiel ni de note fabriquée.
        using var pdfResponse = await admin.GetAsync(
            $"/api/etablissement/cycles/evaluations/{enrollmentId}/apercu.pdf");
        Assert.Equal(HttpStatusCode.OK, pdfResponse.StatusCode);
        Assert.Equal("application/pdf", pdfResponse.Content.Headers.ContentType?.MediaType);
        var pdf = await pdfResponse.Content.ReadAsByteArrayAsync();
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.True(pdfResponse.Headers.CacheControl?.NoStore == true);

        // Une cote historique reliée à un élément non annuel ne doit pas
        // disparaître de la consultation, ni être attachée à une autre branche.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
            var enrollment = await db.Enrollments.SingleAsync(e => e.Id == enrollmentId);
            var classroom = await db.Promotions.SingleAsync(p => p.Id == enrollment.PromotionId);
            var period = await db.SchoolCyclePeriods.SingleAsync(p =>
                p.SchoolCycleId == classroom.SchoolCycleId && p.Code == "P1");
            var legacySubject = new CurriculumItem
            {
                AcademicYearId = enrollment.AcademicYearId,
                PromotionId = classroom.Id,
                Kind = CurriculumItemKind.TeachingUnit,
                Code = "HIST",
                Name = "Ancienne matière",
            };
            db.CurriculumItems.Add(legacySubject);
            db.SchoolCycleAssessments.Add(new SchoolCycleAssessment
            {
                AcademicYearId = enrollment.AcademicYearId,
                SchoolCycleId = period.SchoolCycleId,
                PromotionId = classroom.Id,
                EnrollmentId = enrollmentId,
                SchoolCyclePeriodId = period.Id,
                CurriculumItemId = legacySubject.Id,
                EvaluationCode = "HIST1",
                Title = "Ancienne évaluation",
                Mode = SchoolCycleAssessmentMode.Evaluation,
                Score = 8m,
                MaxScore = 20m,
            });
            scope.ServiceProvider.GetRequiredService<IAuditLog>().Record(
                "tests.bulletins.archives", "EvaluationCycle", enrollmentId.ToString(),
                "Contrôle des cotes historiques hors maquette scolaire.");
            await db.SaveChangesAsync();
        }
        var updated = await admin.GetAsync(
            $"/api/etablissement/cycles/evaluations/{enrollmentId}/apercu").Ok();
        var outside = Assert.Single(updated.GetProperty("unassignedEntries").EnumerateArray());
        Assert.Equal("Ancienne évaluation", outside.GetProperty("title").GetString());
        Assert.Equal(8m, outside.GetProperty("score").GetDecimal());
        Assert.Equal(2, updated.GetProperty("subjectRows").GetArrayLength());
        using var updatedPdf = await admin.GetAsync(
            $"/api/etablissement/cycles/evaluations/{enrollmentId}/apercu.pdf");
        Assert.Equal(HttpStatusCode.OK, updatedPdf.StatusCode);
        var bytes = await updatedPdf.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
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
            var secondPeriod = new SchoolCyclePeriod
            {
                SchoolCycleId = nursery.Id, AcademicYearId = year.Id,
                Code = "P2", Name = "Observation suivante", TermCode = "T1",
                Mode = SchoolCycleAssessmentMode.Observation, Order = 2,
                StartDate = year.StartDate.AddDays(13), EndDate = year.StartDate.AddDays(24),
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
            db.SchoolCyclePeriods.AddRange(period, secondPeriod);
            db.SchoolCycleAssessments.Add(record);
            scope.ServiceProvider.GetRequiredService<IAuditLog>().Record(
                "tests.evaluations.cycles", "EvaluationCycle", record.Id.ToString(),
                "Vérification de migration et aperçu pédagogique en base de test.");
            await db.SaveChangesAsync();
        }

        var json = await admin.GetAsync(
            $"/api/etablissement/cycles/evaluations/{enrollmentId}/apercu").Ok();
        Assert.False(json.GetProperty("isOfficial").GetBoolean());
        var presentation = SchoolBulletinLayouts.For(SchoolCycleKind.Maternelle, 1);
        Assert.Equal(presentation.Code, json.GetProperty("templateCode").GetString());
        Assert.Equal(presentation.PreviewTitle, json.GetProperty("documentTitle").GetString());
        Assert.Equal("Maternelle", json.GetProperty("cycle").GetString());
        Assert.Empty(json.GetProperty("subjectRows").EnumerateArray());
        Assert.Equal(1, json.GetProperty("classLevel").GetInt32());
        var row = Assert.Single(json.GetProperty("entries").EnumerateArray());
        Assert.Equal("Observation", row.GetProperty("mode").GetString());
        Assert.Equal("P1", row.GetProperty("periodCode").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, row.GetProperty("score").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, row.GetProperty("maxScore").ValueKind);
        Assert.Contains("Participe", row.GetProperty("observation").GetString() ?? string.Empty);
        var term = Assert.Single(json.GetProperty("terms").EnumerateArray());
        Assert.Equal("T1", term.GetProperty("code").GetString());
        Assert.Equal(2, term.GetProperty("periods").GetArrayLength());
        var tperiods = term.GetProperty("periods").EnumerateArray().ToArray();
        Assert.Equal("P1", tperiods[0].GetProperty("code").GetString());
        Assert.Equal("P2", tperiods[1].GetProperty("code").GetString());
        Assert.Equal(1, tperiods[0].GetProperty("entries").GetArrayLength());
        Assert.Empty(tperiods[1].GetProperty("entries").EnumerateArray());
        Assert.DoesNotContain("moyenne", json.GetRawText(), StringComparison.OrdinalIgnoreCase);

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
