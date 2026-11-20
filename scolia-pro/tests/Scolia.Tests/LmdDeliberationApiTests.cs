using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scolia.Application.Abstractions;
using Scolia.Domain.Students;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Tests;

/// <summary>
/// Université LMD de bout en bout : filière, promotion, tranches en USD, maquette S1/S2,
/// notes des EC, résultats partiels du S1, première et deuxième session, dettes, relevés, passage.
/// </summary>
public sealed class LmdDeliberationApiTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    [Fact]
    public async Task University_year_from_s1_partial_results_to_second_session_debts_and_reenrollment()
    {
        var admin = app.Client();
        await admin.Post("/api/installation", new
        {
            institutionType = "SuperieurLmd", governance = "Prive",
            schoolName = "Université de test", shortName = "UT", phone = "0810000000",
            city = "Lubumbashi", currencyCode = "USD", currencySymbol = "$",
            yearLabel = Http.Year(DateOnly.FromDateTime(DateTime.Today)),
            startMonth = 10, monthCount = 10,
            adminUserName = "admin", adminDisplayName = "Secrétariat académique",
            adminPassword = "Scolia2026!", loadDemoData = false,
        }).Ok();
        var school = await admin.GetAsync("/api/etablissement").Ok();
        Assert.Equal("USD", school.GetProperty("currencyCode").GetString());

        // Structures : faculté → département → filière.
        async Task<Guid> Unit(string kind, Guid? parent, string code, string name) =>
            (await admin.Post("/api/etablissement/lmd/structures", new { kind, parentId = parent, code, name }).Ok())
            .GetProperty("id").GetGuid();
        var faculty = await Unit("Faculty", null, "FS", "Faculté des sciences");
        var department = await Unit("Department", faculty, "INFO", "Département d'informatique");
        var filiere = await Unit("Filiere", department, "GL", "Génie logiciel");

        var promotion = await admin.Post("/api/etablissement/promotions", new
        {
            code = "GLL1", name = "Génie logiciel L1", level = 1, isActive = true, nextPromotionCode = "GLL2",
            monthlyFee = 0m, oneTimeFees = Array.Empty<object>(), academicUnitId = filiere,
        }).Ok();
        var promotionId = promotion.GetProperty("id").GetGuid();
        Assert.Equal(filiere, promotion.GetProperty("academicUnitId").GetGuid());

        // Frais académiques en trois tranches datées.
        var year = (await admin.GetAsync("/api/etablissement/annees").Ok())[0];
        var start = DateOnly.Parse(year.GetProperty("startDate").GetString()!);
        var withTranches = await admin.Put($"/api/etablissement/lmd/promotions/{promotionId}/tranches", new
        {
            installments = new[]
            {
                new { amount = 200m, dueDate = start.AddDays(20) },
                new { amount = 150m, dueDate = start.AddDays(110) },
                new { amount = 150m, dueDate = start.AddDays(200) },
            },
        }).Ok();
        var fees = withTranches.GetProperty("fees").EnumerateArray().ToList();
        Assert.Equal(new[] { "Frais académiques — 1re tranche", "Frais académiques — 2e tranche", "Frais académiques — 3e tranche" },
            fees.Select(f => f.GetProperty("label").GetString()!).ToArray());
        Assert.Equal(500m, withTranches.GetProperty("annualTotal").GetDecimal());
        var (badOrder, _) = await admin.Put($"/api/etablissement/lmd/promotions/{promotionId}/tranches", new
        {
            installments = new[] { new { amount = 200m, dueDate = start.AddDays(60) }, new { amount = 100m, dueDate = start.AddDays(30) } },
        }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, badOrder);

        // Maquette : S1 = UEA (20 crédits, EC A1 + A2) et UEB (10 crédits) ; S2 = UEC (30 crédits).
        async Task<(Guid Ue, Guid Ec)> Ue(string semester, string code, decimal credits, string ec) =>
            (await admin.Post($"/api/etablissement/lmd/promotions/{promotionId}/ues", new
            {
                academicUnitId = filiere, semester, code, name = $"Unité {code}", credits, hours = credits * 15,
                elementCode = ec, elementName = $"Élément {ec}", elementHours = 30m,
            }).Ok()) is var items ? (items[0].GetProperty("id").GetGuid(), items[1].GetProperty("id").GetGuid()) : default;
        var (ueA, a1) = await Ue("S1", "UEA", 20, "A1");
        var a2 = (await admin.Post($"/api/etablissement/lmd/promotions/{promotionId}/ecs", new
        {
            parentItemId = ueA, code = "A2", name = "Élément A2", hours = 30m,
        }).Ok()).GetProperty("id").GetGuid();
        var (_, b1) = await Ue("S1", "UEB", 10, "B1");
        var (_, c1) = await Ue("S2", "UEC", 30, "C1");

        // Étudiants X, Y, Z, W.
        var enrollments = new Guid[4];
        var students = new Guid[4];
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
            var current = await db.AcademicYears.SingleAsync(y => y.IsCurrent);
            for (var i = 0; i < 4; i++)
            {
                var s = new Student { Matricule = $"GL{i + 1:000}", LastName = $"ETUDIANT{"XYZW"[i]}", FirstName = "Test" };
                s.RefreshKeys();
                var e = new Enrollment { StudentId = s.Id, AcademicYearId = current.Id, PromotionId = promotionId, EnrolledOn = current.StartDate };
                (students[i], enrollments[i]) = (s.Id, e.Id);
                db.Students.Add(s);
                db.Enrollments.Add(e);
            }
            scope.ServiceProvider.GetRequiredService<IAuditLog>().Record("tests.etudiants", "Promotion", promotionId.ToString(), "Étudiants de test.");
            await db.SaveChangesAsync();
        }
        var (X, Y, Z, W) = (enrollments[0], enrollments[1], enrollments[2], enrollments[3]);
        var route = $"/api/etablissement/lmd/promotions/{promotionId}";

        var (noRule, _) = await admin.GetAsync($"{route}/resultats").Fail();
        Assert.Equal(HttpStatusCode.BadRequest, noRule);
        await admin.Post("/api/etablissement/reglements/SuperieurLmd/validation", new { }).Ok();

        Task<JsonElement> Marks(Guid ec, int session, params (Guid Id, decimal Score)[] lines) =>
            admin.Put($"{route}/ecs/{ec}/notes", new
            {
                session, lines = lines.Select(l => new { enrollmentId = l.Id, score = (decimal?)l.Score }).ToArray(),
            }).Ok();
        await Marks(a1, 1, (X, 14), (Y, 12), (Z, 13), (W, 12));
        await Marks(a2, 1, (X, 14), (Y, 11), (Z, 12), (W, 12));
        var (outOfScale, _) = await admin.Put($"{route}/ecs/{b1}/notes",
            new { session = 1, lines = new[] { new { enrollmentId = X, score = (decimal?)21m } } }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, outOfScale);
        var (missingS1, missingBody) = await admin.Post($"{route}/publication-s1", new { }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, missingS1);
        Assert.Contains("ETUDIANTX", missingBody.GetProperty("title").GetString());
        await Marks(b1, 1, (X, 15), (Y, 8), (Z, 11), (W, 10));

        // Publication des résultats partiels du premier semestre.
        var s1 = await admin.Post($"{route}/publication-s1", new { }).Ok();
        Assert.Equal("S1Publie", s1.GetProperty("status").GetString());
        var s1ByStudent = s1.GetProperty("students").EnumerateArray().ToDictionary(s => s.GetProperty("enrollmentId").GetGuid());
        Assert.Equal(20m, s1ByStudent[Y].GetProperty("s1").GetProperty("creditsAcquired").GetDecimal());
        Assert.Equal(30m, s1ByStudent[X].GetProperty("s1").GetProperty("creditsAcquired").GetDecimal());
        var (s1Frozen, _) = await admin.Put($"{route}/ecs/{b1}/notes",
            new { session = 1, lines = new[] { new { enrollmentId = Y, score = (decimal?)12m } } }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, s1Frozen);
        var s1Pdf = await admin.GetAsync($"{route}/releves.pdf?etape=S1");
        Assert.Equal(HttpStatusCode.OK, s1Pdf.StatusCode);

        // Deuxième semestre puis résultats de la première session (tous les cours de la promotion).
        await Marks(c1, 1, (X, 15), (Y, 12), (Z, 6), (W, 5));
        var (early, _) = await admin.Get2Pdf($"{route}/releves.pdf?etape=PremiereSession");
        Assert.Equal(HttpStatusCode.BadRequest, early);
        var first = await admin.Post($"{route}/premiere-session", new { }).Ok();
        Assert.Equal("PremiereSessionCloturee", first.GetProperty("status").GetString());
        var firstBy = first.GetProperty("students").EnumerateArray().ToDictionary(s => s.GetProperty("enrollmentId").GetGuid());
        Assert.Equal("Admis", firstBy[X].GetProperty("decision").GetString());
        Assert.Equal("Deuxième session", firstBy[Y].GetProperty("decision").GetString());
        Assert.Equal("Deuxième session", firstBy[Z].GetProperty("decision").GetString());
        Assert.Equal("Deuxième session", firstBy[W].GetProperty("decision").GetString());

        // Deuxième session : seuls les étudiants ajournés, sur leurs UE non acquises.
        var sheet = await admin.GetAsync($"{route}/ecs/{c1}/notes?session=2").Ok();
        var editable = sheet.GetProperty("lines").EnumerateArray()
            .Where(l => l.GetProperty("editable").GetBoolean()).Select(l => l.GetProperty("enrollmentId").GetGuid()).ToHashSet();
        Assert.Equal(new HashSet<Guid> { Z, W }, editable);
        var (notEligible, _) = await admin.Put($"{route}/ecs/{c1}/notes",
            new { session = 2, lines = new[] { new { enrollmentId = X, score = (decimal?)18m } } }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, notEligible);
        await Marks(b1, 2, (Y, 9));
        await Marks(c1, 2, (Z, 13), (W, 4));
        var closed = await admin.Post($"{route}/cloture", new { }).Ok();
        Assert.Equal("Cloturee", closed.GetProperty("status").GetString());
        var finalBy = closed.GetProperty("students").EnumerateArray().ToDictionary(s => s.GetProperty("enrollmentId").GetGuid());
        Assert.Equal("Admis", finalBy[X].GetProperty("decision").GetString());
        Assert.Equal("Admis avec dettes", finalBy[Y].GetProperty("decision").GetString());
        Assert.Equal("Admis", finalBy[Z].GetProperty("decision").GetString());
        Assert.Equal("Ajourné", finalBy[W].GetProperty("decision").GetString());
        var zC1 = finalBy[Z].GetProperty("units").EnumerateArray().Single(u => u.GetProperty("code").GetString() == "UEC")
            .GetProperty("elements")[0];
        Assert.Equal((13m, 2), (zC1.GetProperty("mark").GetDecimal(), zC1.GetProperty("session").GetInt32()));

        var (reopen, _) = await admin.Post("/api/etablissement/reglements/SuperieurLmd/reouverture",
            new { reason = "Tentative après publication" }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, reopen);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{route}/releves.pdf?etape=Definitif")).StatusCode);
        var minutes = await admin.GetAsync($"{route}/proces-verbal.pdf?etape=Definitif");
        Assert.Equal(HttpStatusCode.OK, minutes.StatusCode);
        var dump = Environment.GetEnvironmentVariable("SCOLIA_LMD_DUMP");
        if (!string.IsNullOrEmpty(dump))
        {
            await File.WriteAllBytesAsync(dump + "-pv.pdf", await minutes.Content.ReadAsByteArrayAsync());
            var one = await admin.GetAsync($"{route}/releves.pdf?etape=Definitif&inscription={Y}");
            await File.WriteAllBytesAsync(dump + "-releve.pdf", await one.Content.ReadAsByteArrayAsync());
        }

        // Dette de Y : UE B, apurée l'année suivante avec au moins 10/20.
        var debt = Assert.Single((await admin.GetAsync("/api/etablissement/lmd/dettes").Ok()).EnumerateArray());
        Assert.Equal(("UEB", 10m), (debt.GetProperty("ueCode").GetString(), debt.GetProperty("credits").GetDecimal()));
        var debtId = debt.GetProperty("id").GetGuid();
        var (tooLow, _) = await admin.Put($"/api/etablissement/lmd/dettes/{debtId}/apurement", new { mark = 9m }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, tooLow);
        var cleared = await admin.Put($"/api/etablissement/lmd/dettes/{debtId}/apurement", new { mark = 11m }).Ok();
        Assert.Equal(11m, cleared.GetProperty("clearedMark").GetDecimal());

        // Passage : X, Y (avec dettes) et Z montent en GLL2 ; W reste en GLL1.
        var firstYear = int.Parse(Http.Year(DateOnly.FromDateTime(DateTime.Today))[..4], System.Globalization.CultureInfo.InvariantCulture);
        var future = await admin.Post("/api/etablissement/annees", new
        {
            label = $"{firstYear + 1}-{firstYear + 2}", startMonth = 10, monthCount = 10, dueDay = 6,
            copyPromotionsFromCurrent = false, makeCurrent = false,
        }).Ok();
        var plan = (await admin.GetAsync($"/api/reinscriptions/plan?anneeId={future.GetProperty("id").GetString()}").Ok())
            .EnumerateArray().ToDictionary(p => p.GetProperty("studentId").GetGuid(), p => p.GetProperty("toPromotion").GetString());
        Assert.Equal(["GLL2", "GLL2", "GLL2", "GLL1"], students.Select(s => plan[s]!).ToArray());
    }
}

internal static class PdfCall
{
    public static async Task<(HttpStatusCode Status, string Body)> Get2Pdf(this HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}
