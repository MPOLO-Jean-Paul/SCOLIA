using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scolia.Application.Abstractions;
using Scolia.Domain.School;
using Scolia.Domain.Students;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Tests;

/// <summary>Résultats d'une classe primaire : règlement validé, maxima, totaux, place.</summary>
public sealed class EpstResultsApiTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    private async Task<HttpClient> InstallPrimary()
    {
        var admin = app.Client();
        await admin.Post("/api/installation", new
        {
            institutionType = "Primaire", governance = "Prive",
            schoolName = "EP Les Flamboyants", shortName = "EPF", phone = "0810000000",
            city = "Kinshasa", currencyCode = "CDF", currencySymbol = "FC",
            yearLabel = Http.Year(DateOnly.FromDateTime(DateTime.Today)),
            startMonth = 9, monthCount = 10,
            adminUserName = "admin", adminDisplayName = "Secrétariat",
            adminPassword = "Scolia2026!", loadDemoData = false,
        }).Ok();
        return admin;
    }

    [Fact]
    public async Task Primary_class_results_need_validated_regulation_and_maxima_then_rank_pupils()
    {
        var admin = await InstallPrimary();
        Guid cycleId, classId;
        Guid[] pupils = new Guid[3];
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
            var year = await db.AcademicYears.SingleAsync(y => y.IsCurrent);
            var cycle = await db.SchoolCycles.SingleAsync();
            cycleId = cycle.Id;
            var classroom = new Promotion
            {
                AcademicYearId = year.Id, SchoolCycleId = cycle.Id, Code = "P5A", Name = "5e primaire A", Level = 5,
            };
            classId = classroom.Id;
            db.Promotions.Add(classroom);
            var names = new[] { ("MUKENDI", "Grâce"), ("ILUNGA", "Daniel"), ("KABAMBA", "Ruth") };
            for (var i = 0; i < names.Length; i++)
            {
                var s = new Student { Matricule = $"EP{i + 1:00}", LastName = names[i].Item1, FirstName = names[i].Item2 };
                s.RefreshKeys();
                var e = new Enrollment
                {
                    StudentId = s.Id, AcademicYearId = year.Id, PromotionId = classroom.Id, EnrolledOn = year.StartDate,
                };
                pupils[i] = e.Id;
                db.Students.Add(s);
                db.Enrollments.Add(e);
            }
            scope.ServiceProvider.GetRequiredService<IAuditLog>().Record(
                "tests.classe", "Promotion", classroom.Id.ToString(), "Classe de test et ses élèves.");
            await db.SaveChangesAsync();
        }

        // Calendrier réel du cycle : 3 trimestres de 2 périodes et un examen.
        var start = (await admin.GetAsync("/api/etablissement/annees").Ok()).EnumerateArray()
            .Select(y => DateOnly.Parse(y.GetProperty("startDate").GetString()!)).First();
        var day = start;
        for (var t = 1; t <= 3; t++)
            foreach (var (code, exam) in new[] { ($"P{2 * t - 1}", false), ($"P{2 * t}", false), ($"EX{t}", true) })
            {
                await admin.Post($"/api/etablissement/cycles/{cycleId}/periodes", new
                {
                    code, name = exam ? $"Examen T{t}" : $"Période {code}", termCode = $"T{t}",
                    startDate = day, endDate = day.AddDays(15), isExam = exam,
                }).Ok();
                day = day.AddDays(20);
            }
        var french = await admin.Post($"/api/etablissement/cycles/promotions/{classId}/matieres", new
        {
            code = "FR", name = "Français", coefficient = 1m, hours = 6m, periodMaxScore = 20m,
        }).Ok();
        Assert.Equal(20m, french.GetProperty("periodMaxScore").GetDecimal());
        var maths = await admin.Post($"/api/etablissement/cycles/promotions/{classId}/matieres", new
        {
            code = "CALC", name = "Calcul", coefficient = 1m, hours = 5m,
        }).Ok();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
            var year = await db.AcademicYears.SingleAsync(y => y.IsCurrent);
            var periods = await db.SchoolCyclePeriods.ToListAsync();
            Assert.Equal(3, periods.Count(p => p.IsExam));
            var subjectIds = new[] { french.GetProperty("id").GetGuid(), maths.GetProperty("id").GetGuid() };
            var fractions = new[] { 0.8m, 0.6m, 0.4m };
            for (var i = 0; i < pupils.Length; i++)
                foreach (var p in periods)
                    foreach (var subjectId in subjectIds)
                        db.SchoolCycleAssessments.Add(new SchoolCycleAssessment
                        {
                            AcademicYearId = year.Id, SchoolCycleId = cycleId, PromotionId = classId,
                            EnrollmentId = pupils[i], SchoolCyclePeriodId = p.Id, CurriculumItemId = subjectId,
                            EvaluationCode = p.IsExam ? "EXAMEN" : "INTERRO1", Title = "Évaluation",
                            Mode = SchoolCycleAssessmentMode.Evaluation,
                            Score = 10 * fractions[i], MaxScore = 10,
                        });
            scope.ServiceProvider.GetRequiredService<IAuditLog>().Record(
                "tests.cotes", "EvaluationCycle", classId.ToString(), "Cotes de test.");
            await db.SaveChangesAsync();
        }

        var route = $"/api/etablissement/cycles/promotions/{classId}/resultats";
        var (noRule, noRuleBody) = await admin.GetAsync(route).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, noRule);
        Assert.Equal("reglement", noRuleBody.GetProperty("code").GetString());

        await admin.Post("/api/etablissement/reglements/Primaire/validation", new { }).Ok();
        var (noMax, noMaxBody) = await admin.GetAsync(route).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, noMax);
        Assert.Contains("Calcul", noMaxBody.GetProperty("title").GetString());

        await admin.Put($"/api/etablissement/cycles/matieres/{maths.GetProperty("id").GetGuid()}/maximum",
            new { periodMaxScore = 10m }).Ok();
        var results = await admin.GetAsync(route).Ok();
        Assert.Equal(3, results.GetProperty("headcount").GetInt32());
        Assert.Equal(3, results.GetProperty("terms").GetArrayLength());
        var rows = results.GetProperty("pupils").EnumerateArray().ToArray();
        Assert.Equal(new[] { "MUKENDI Grâce", "ILUNGA Daniel", "KABAMBA Ruth" },
            rows.Select(r => r.GetProperty("fullName").GetString()!).ToArray());
        Assert.Equal(new[] { 1, 2, 3 }, rows.Select(r => r.GetProperty("rank").GetInt32()).ToArray());
        Assert.Equal(360m, rows[0].GetProperty("max").GetDecimal());
        Assert.Equal(288m, rows[0].GetProperty("total").GetDecimal());
        Assert.Equal(80m, rows[0].GetProperty("percent").GetDecimal());
        Assert.Equal(2, rows[2].GetProperty("failedSubjects").GetInt32());

        // Application et conduite : périodes de travail seulement, échelle E à MA.
        var p1 = results.GetProperty("periods").EnumerateArray().First().GetProperty("id").GetGuid();
        var ex1 = results.GetProperty("periods").EnumerateArray().First(p => p.GetProperty("isExam").GetBoolean())
            .GetProperty("id").GetGuid();
        await admin.Put($"/api/etablissement/cycles/promotions/{classId}/conduite", new
        {
            periodId = p1,
            lines = pupils.Select(id => new { enrollmentId = id, application = "tb", conduct = "E" }).ToArray(),
        }).Ok();
        var (examConduct, _) = await admin.Put($"/api/etablissement/cycles/promotions/{classId}/conduite", new
        {
            periodId = ex1, lines = new[] { new { enrollmentId = pupils[0], application = "B", conduct = "B" } },
        }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, examConduct);
        var (badCode, _) = await admin.Put($"/api/etablissement/cycles/promotions/{classId}/conduite", new
        {
            periodId = p1, lines = new[] { new { enrollmentId = pupils[0], application = "XX", conduct = "B" } },
        }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, badCode);
        var withConduct = await admin.GetAsync(route).Ok();
        var first = withConduct.GetProperty("pupils")[0].GetProperty("conduct")[0];
        Assert.Equal(("TB", "E"), (first.GetProperty("application").GetString(), first.GetProperty("conduct").GetString()));
        Assert.Equal(6, withConduct.GetProperty("pupils")[0].GetProperty("conduct").GetArrayLength());

        var classPdf = await admin.GetAsync($"/api/etablissement/cycles/promotions/{classId}/bulletins.pdf");
        Assert.Equal(HttpStatusCode.OK, classPdf.StatusCode);
        Assert.Equal("application/pdf", classPdf.Content.Headers.ContentType?.MediaType);
        var bytes = await classPdf.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        var onePdf = await admin.GetAsync(
            $"/api/etablissement/cycles/promotions/{classId}/bulletins.pdf?inscription={pupils[1]}");
        Assert.Equal(HttpStatusCode.OK, onePdf.StatusCode);
        Assert.True((await onePdf.Content.ReadAsByteArrayAsync()).Length < bytes.Length);
        var dump = Environment.GetEnvironmentVariable("SCOLIA_BULLETIN_DUMP");
        if (!string.IsNullOrEmpty(dump)) await File.WriteAllBytesAsync(dump, bytes);
    }
}
