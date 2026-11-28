using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scolia.Application.Abstractions;
using Scolia.Domain.School;
using Scolia.Domain.Students;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Tests;

/// <summary>
/// Classe primaire complète pour les tests : 3 trimestres (2 périodes et un examen),
/// Français (max 20) et Calcul (max 10), élèves notés à une fraction donnée par branche.
/// </summary>
public sealed record EpstClass(HttpClient Admin, Guid CycleId, Guid ClassId, Guid FrenchId, Guid MathsId,
    Guid[] Pupils, Guid[] Students);

public static class EpstClassBuilder
{
    public static async Task<HttpClient> InstallAsync(ScoliaApp app, string type = "Primaire")
    {
        var admin = app.Client();
        await admin.Post("/api/installation", new
        {
            institutionType = type, governance = "Prive",
            schoolName = "EP Les Flamboyants", shortName = "EPF", phone = "0810000000",
            city = "Kinshasa", currencyCode = "CDF", currencySymbol = "FC",
            yearLabel = Http.Year(DateOnly.FromDateTime(DateTime.Today)),
            startMonth = 9, monthCount = 10,
            adminUserName = "admin", adminDisplayName = "Secrétariat",
            adminPassword = "Scolia2026!", loadDemoData = false,
        }).Ok();
        return admin;
    }

    /// <summary>
    /// <paramref name="fractions"/>[élève] = (Français, Calcul). <paramref name="skip"/> retire
    /// la cote de l'examen final de Calcul pour les élèves indiqués.
    /// </summary>
    public static async Task<EpstClass> BuildAsync(ScoliaApp app, (decimal French, decimal Maths)[] fractions,
        int[]? skip = null, string? nextCode = "P6A")
    {
        var admin = await InstallAsync(app);
        Guid cycleId, classId;
        var pupils = new Guid[fractions.Length];
        var students = new Guid[fractions.Length];
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
            var year = await db.AcademicYears.SingleAsync(y => y.IsCurrent);
            var cycle = await db.SchoolCycles.SingleAsync();
            cycleId = cycle.Id;
            var classroom = new Promotion
            {
                AcademicYearId = year.Id, SchoolCycleId = cycle.Id, Code = "P5A", Name = "5e primaire A", Level = 5,
                NextPromotionCode = nextCode,
            };
            classId = classroom.Id;
            db.Promotions.Add(classroom);
            for (var i = 0; i < fractions.Length; i++)
            {
                var s = new Student { Matricule = $"EP{i + 1:00}", LastName = $"ELEVE{(char)('A' + i)}", FirstName = "Test" };
                s.RefreshKeys();
                var e = new Enrollment
                {
                    StudentId = s.Id, AcademicYearId = year.Id, PromotionId = classroom.Id, EnrolledOn = year.StartDate,
                };
                pupils[i] = e.Id;
                students[i] = s.Id;
                db.Students.Add(s);
                db.Enrollments.Add(e);
            }
            scope.ServiceProvider.GetRequiredService<IAuditLog>().Record(
                "tests.classe", "Promotion", classroom.Id.ToString(), "Classe de test et ses élèves.");
            await db.SaveChangesAsync();
        }

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
        var french = (await admin.Post($"/api/etablissement/cycles/promotions/{classId}/matieres", new
        {
            code = "FR", name = "Français", coefficient = 1m, hours = 6m, periodMaxScore = 20m,
        }).Ok()).GetProperty("id").GetGuid();
        var maths = (await admin.Post($"/api/etablissement/cycles/promotions/{classId}/matieres", new
        {
            code = "CALC", name = "Calcul", coefficient = 1m, hours = 5m, periodMaxScore = 10m,
        }).Ok()).GetProperty("id").GetGuid();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
            var year = await db.AcademicYears.SingleAsync(y => y.IsCurrent);
            var periods = await db.SchoolCyclePeriods.OrderBy(p => p.Order).ToListAsync();
            for (var i = 0; i < pupils.Length; i++)
                foreach (var p in periods)
                    foreach (var (subjectId, fraction) in new[] { (french, fractions[i].French), (maths, fractions[i].Maths) })
                    {
                        if (skip?.Contains(i) == true && subjectId == maths && p == periods[^1]) continue;
                        db.SchoolCycleAssessments.Add(new SchoolCycleAssessment
                        {
                            AcademicYearId = year.Id, SchoolCycleId = cycleId, PromotionId = classId,
                            EnrollmentId = pupils[i], SchoolCyclePeriodId = p.Id, CurriculumItemId = subjectId,
                            EvaluationCode = p.IsExam ? "EXAMEN" : "INTERRO1", Title = "Évaluation",
                            Mode = SchoolCycleAssessmentMode.Evaluation, Score = 10 * fraction, MaxScore = 10,
                        });
                    }
            scope.ServiceProvider.GetRequiredService<IAuditLog>().Record(
                "tests.cotes", "EvaluationCycle", classId.ToString(), "Cotes de test.");
            await db.SaveChangesAsync();
        }
        await admin.Post("/api/etablissement/reglements/Primaire/validation", new { }).Ok();
        return new EpstClass(admin, cycleId, classId, french, maths, pupils, students);
    }
}
