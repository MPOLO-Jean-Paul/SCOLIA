using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scolia.Domain.Common;
using Scolia.Domain.School;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Tests;

/// <summary>Délibération EPST : décisions, jury motivé, repêchage, figement, palmarès, passage de classe.</summary>
public sealed class EpstDeliberationTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    private static AcademicRegulation Rule()
    {
        var r = AcademicRegulationRules.DefaultFor(RegulationScope.Primaire, Guid.NewGuid());
        r.Status = RegulationStatus.Valide;
        return r;
    }

    private static EpstPupilResult Pupil(decimal? percent, params bool[] failed) => new(Guid.NewGuid(),
        failed.Select(f => new EpstSubjectResult(Guid.NewGuid(), "X", "X", [], [], 1, 1, 1, true, f)).ToList(),
        [], [], percent, 100, percent, percent is null ? 1 : 0, failed.Count(f => f), null, []);

    [Fact]
    public void First_session_rules_follow_the_validated_regulation()
    {
        Assert.Equal(EpstOutcome.Passe, EpstDeliberationRules.FirstSession(Rule(), Pupil(72, false, false)).Outcome);
        Assert.Equal(EpstOutcome.Redouble, EpstDeliberationRules.FirstSession(Rule(), Pupil(49.99m, false)).Outcome);
        var (outcome, subjects) = EpstDeliberationRules.FirstSession(Rule(), Pupil(55, true, true, false));
        Assert.Equal((EpstOutcome.Repechage, 2), (outcome, subjects.Count));
        Assert.Equal(EpstOutcome.Redouble, EpstDeliberationRules.FirstSession(Rule(), Pupil(55, true, true, true)).Outcome);
        Assert.Equal(EpstOutcome.Incomplet, EpstDeliberationRules.FirstSession(Rule(), Pupil(null, false)).Outcome);
    }

    [Fact]
    public void Jury_needs_a_reason_and_repechage_needs_every_mark_at_the_subject_threshold()
    {
        Assert.Throws<DomainException>(() => EpstDeliberationRules.ValidateJury(EpstOutcome.Passe, "ok"));
        EpstDeliberationRules.ValidateJury(EpstOutcome.Passe, "Maladie attestée en fin d'année");
        Assert.Throws<DomainException>(() => EpstDeliberationRules.ValidateJury(EpstOutcome.PasseApresRepechage, "Motif valable"));
        var subject = Guid.NewGuid();
        var other = Guid.NewGuid();
        Assert.Throws<DomainException>(() => EpstDeliberationRules.AfterRepechage(Rule(), [subject], []));
        Assert.Equal(EpstOutcome.PasseApresRepechage, EpstDeliberationRules.AfterRepechage(Rule(), [subject],
            [new EpstRepechageMark { SubjectId = subject, Score = 10, MaxScore = 20 }]));
        Assert.Equal(EpstOutcome.Redouble, EpstDeliberationRules.AfterRepechage(Rule(), [subject, other],
            [new EpstRepechageMark { SubjectId = subject, Score = 15, MaxScore = 20 },
             new EpstRepechageMark { SubjectId = other, Score = 9, MaxScore = 20 }]));
    }

    [Fact]
    public async Task Class_goes_through_jury_first_session_repechage_closure_palmares_and_reenrollment()
    {
        // A passe, B repêchage en Calcul, C double, D incomplet.
        var k = await EpstClassBuilder.BuildAsync(app, [(0.8m, 0.8m), (0.7m, 0.4m), (0.4m, 0.4m), (0.6m, 0.6m)], skip: [3]);
        var admin = k.Admin;
        var route = $"/api/etablissement/cycles/promotions/{k.ClassId}/deliberation";

        var open = await admin.GetAsync(route).Ok();
        Assert.Equal("Ouverte", open.GetProperty("status").GetString());
        var byId = open.GetProperty("pupils").EnumerateArray().ToDictionary(p => p.GetProperty("enrollmentId").GetGuid());
        Assert.Equal("Passe", byId[k.Pupils[0]].GetProperty("computed").GetString());
        Assert.Equal("Repechage", byId[k.Pupils[1]].GetProperty("computed").GetString());
        Assert.Equal("Redouble", byId[k.Pupils[2]].GetProperty("computed").GetString());
        Assert.Equal("Incomplet", byId[k.Pupils[3]].GetProperty("computed").GetString());
        Assert.Equal("Calcul", byId[k.Pupils[1]].GetProperty("repechage")[0].GetProperty("name").GetString());

        var (blocked, blockedBody) = await admin.Post($"{route}/premiere-session", new { }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, blocked);
        Assert.Contains("ELEVED", blockedBody.GetProperty("title").GetString());
        var (noReason, _) = await admin.Put($"{route}/eleves/{k.Pupils[3]}/jury", new { outcome = "Redouble", reason = "" }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, noReason);
        await admin.Put($"{route}/eleves/{k.Pupils[3]}/jury",
            new { outcome = "Redouble", reason = "Absent à l'examen final sans justification" }).Ok();

        var first = await admin.Post($"{route}/premiere-session", new { }).Ok();
        Assert.Equal("PremiereSessionCloturee", first.GetProperty("status").GetString());

        // Cotes, conduite, maxima et règlement sont figés après publication.
        var period = (await admin.GetAsync($"/api/etablissement/cycles/{k.CycleId}/periodes").Ok())[0].GetProperty("id").GetGuid();
        var (marks, _) = await admin.Post($"/api/etablissement/cycles/evaluations/{k.Pupils[0]}", new
        {
            schoolCyclePeriodId = period, evaluationCode = "INTERRO9", title = "Ajout tardif",
            curriculumItemId = k.FrenchId, observation = (string?)null, score = 20m, maxScore = 20m, comment = (string?)null,
        }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, marks);
        var (conduct, _) = await admin.Put($"/api/etablissement/cycles/promotions/{k.ClassId}/conduite", new
        {
            periodId = period, lines = new[] { new { enrollmentId = k.Pupils[0], application = "E", conduct = "E" } },
        }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, conduct);
        var (maximum, _) = await admin.Put($"/api/etablissement/cycles/matieres/{k.MathsId}/maximum",
            new { periodMaxScore = 20m }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, maximum);
        var (reopen, _) = await admin.Post("/api/etablissement/reglements/Primaire/reouverture",
            new { reason = "Tentative de modification après publication" }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, reopen);
        var (lateJury, _) = await admin.Put($"{route}/eleves/{k.Pupils[2]}/jury",
            new { outcome = "Passe", reason = "Décision tardive refusée" }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, lateJury);

        // Repêchage : seul B, seule la branche en échec, puis clôture définitive.
        var (wrongPupil, _) = await admin.Put($"{route}/eleves/{k.Pupils[0]}/repechage",
            new { marks = new[] { new { subjectId = k.MathsId, score = 12m, maxScore = 20m } } }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, wrongPupil);
        var (wrongSubject, _) = await admin.Put($"{route}/eleves/{k.Pupils[1]}/repechage",
            new { marks = new[] { new { subjectId = k.FrenchId, score = 12m, maxScore = 20m } } }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, wrongSubject);
        var (early, _) = await admin.Post($"{route}/cloture", new { }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, early);
        await admin.Put($"{route}/eleves/{k.Pupils[1]}/repechage",
            new { marks = new[] { new { subjectId = k.MathsId, score = 12m, maxScore = 20m } } }).Ok();
        var closed = await admin.Post($"{route}/cloture", new { }).Ok();
        Assert.Equal("Cloturee", closed.GetProperty("status").GetString());
        var finals = closed.GetProperty("pupils").EnumerateArray()
            .ToDictionary(p => p.GetProperty("enrollmentId").GetGuid(), p => p.GetProperty("effective").GetString());
        Assert.Equal(["Passe", "PasseApresRepechage", "Redouble", "Redouble"],
            k.Pupils.Select(id => finals[id]!).ToArray());

        var results = await admin.GetAsync($"/api/etablissement/cycles/promotions/{k.ClassId}/resultats").Ok();
        Assert.Contains(results.GetProperty("pupils").EnumerateArray(),
            p => p.GetProperty("decision").GetString() == "Passe après repêchage");
        var palmares = await admin.GetAsync($"/api/etablissement/cycles/promotions/{k.ClassId}/palmares.pdf");
        Assert.Equal(HttpStatusCode.OK, palmares.StatusCode);
        var dump = Environment.GetEnvironmentVariable("SCOLIA_PALMARES_DUMP");
        if (!string.IsNullOrEmpty(dump)) await File.WriteAllBytesAsync(dump, await palmares.Content.ReadAsByteArrayAsync());

        // Passage de classe : A et B montent en P6A, C et D restent en P5A ; le cycle est conservé.
        var firstYear = int.Parse(Http.Year(DateOnly.FromDateTime(DateTime.Today))[..4],
            System.Globalization.CultureInfo.InvariantCulture);
        var future = await admin.Post("/api/etablissement/annees", new
        {
            label = $"{firstYear + 1}-{firstYear + 2}", startMonth = 9, monthCount = 10, dueDay = 6,
            copyPromotionsFromCurrent = false, makeCurrent = false,
        }).Ok();
        var futureId = future.GetProperty("id").GetString();
        var plan = (await admin.GetAsync($"/api/reinscriptions/plan?anneeId={futureId}").Ok()).EnumerateArray()
            .ToDictionary(p => p.GetProperty("studentId").GetGuid(), p => p.GetProperty("toPromotion").GetString());
        Assert.Equal(["P6A", "P6A", "P5A", "P5A"], k.Students.Select(id => plan[id]!).ToArray());
        await admin.Post("/api/reinscriptions", new
        {
            targetYearId = futureId,
            choices = new[] { new { studentId = k.Students[0], promotionId = (Guid?)null, promotionCode = "P6A" } },
        }).Ok();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        var created = await db.Promotions.SingleAsync(p => p.Code == "P6A");
        Assert.Equal(k.CycleId, created.SchoolCycleId);
    }
}
