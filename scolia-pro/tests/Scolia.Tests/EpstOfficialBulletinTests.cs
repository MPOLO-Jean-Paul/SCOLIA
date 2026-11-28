using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Scolia.Application.Services;
using Scolia.Infrastructure.Documents;

namespace Scolia.Tests;

/// <summary>
/// Bulletin officiel EPST : identification de l'école (province, commune, code) et de l'élève
/// (N° permanent, sexe, naissance), décision de la délibération et notes de repêchage.
/// </summary>
public sealed class EpstOfficialBulletinTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    [Fact]
    public async Task Official_bulletin_carries_school_and_pupil_identification_decision_and_repechage()
    {
        // A passe, B va au repêchage en Calcul.
        var k = await EpstClassBuilder.BuildAsync(app, [(0.8m, 0.8m), (0.7m, 0.4m)]);
        var admin = k.Admin;

        var school = await admin.GetAsync("/api/etablissement").Ok();
        var update = new
        {
            name = school.GetProperty("name").GetString(), shortName = school.GetProperty("shortName").GetString(),
            city = "Kinshasa", currencyCode = school.GetProperty("currencyCode").GetString(),
            currencySymbol = school.GetProperty("currencySymbol").GetString(),
            wholeAmountsOnly = school.GetProperty("wholeAmountsOnly").GetBoolean(),
            phoneCountryCode = school.GetProperty("phoneCountryCode").GetString(),
            province = " Kinshasa ", commune = "Lingwala", officialCode = "ep-1012345",
        };
        var saved = await admin.Put("/api/etablissement", update).Ok();
        Assert.Equal(("Kinshasa", "Lingwala", "EP-1012345"), (saved.GetProperty("province").GetString(),
            saved.GetProperty("commune").GetString(), saved.GetProperty("officialCode").GetString()));
        var (tooLong, _) = await admin.Put("/api/etablissement", update with { officialCode = new string('9', 21) }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, tooLong);

        var student = await admin.GetAsync($"/api/apprenants/{k.Students[1]}").Ok();
        object Identity(string permanent) => new
        {
            lastName = student.GetProperty("lastName").GetString(), middleName = "MWEMA",
            firstName = student.GetProperty("firstName").GetString(), gender = "M", birthDate = new DateOnly(2015, 4, 9),
            birthPlace = "Matadi", guardianPhone = "0810000001", version = student.GetProperty("version").GetInt64(),
            permanentNumber = permanent,
        };
        var (badNumber, badBody) = await admin.Put($"/api/apprenants/{k.Students[1]}", Identity("12#45")).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, badNumber);
        Assert.Contains("permanent", badBody.ToString(), StringComparison.OrdinalIgnoreCase);
        await admin.Put($"/api/apprenants/{k.Students[1]}", Identity("ab 0123456")).Ok();
        Assert.Equal("AB 0123456",
            (await admin.GetAsync($"/api/apprenants/{k.Students[1]}").Ok()).GetProperty("permanentNumber").GetString());

        // Délibération ouverte : aucune décision imprimée, aucune mention biffée.
        using (var scope = app.Services.CreateScope())
        {
            var print = await scope.ServiceProvider.GetRequiredService<EpstResultsService>().PrintAsync(k.ClassId, null);
            Assert.Equal(("Kinshasa", "Lingwala", "EP-1012345"), (print.Province, print.Commune, print.SchoolCode));
            Assert.NotNull(print.IssuedOn);
            var b = print.Details!.Single(x => x.EnrollmentId == k.Pupils[1]);
            Assert.Equal(("M", "Matadi", new DateOnly(2015, 4, 9), "AB 0123456"), (b.Sex, b.BirthPlace, b.BirthDate!.Value, b.PermanentNumber));
            Assert.Null(b.Outcome);
            var layout = EpstBulletinLayout.For(print, print.Pupils.Single(p => p.EnrollmentId == k.Pupils[1]));
            Assert.All(layout.Mentions, m => Assert.False(m.Struck));
            Assert.Null(layout.Decision);
        }

        var route = $"/api/etablissement/cycles/promotions/{k.ClassId}/deliberation";
        await admin.Post($"{route}/premiere-session", new { }).Ok();
        using (var scope = app.Services.CreateScope())
        {
            var print = await scope.ServiceProvider.GetRequiredService<EpstResultsService>().PrintAsync(k.ClassId, null);
            var b = print.Details!.Single(x => x.EnrollmentId == k.Pupils[1]);
            Assert.Equal("Repechage", b.Outcome);
            Assert.Equal(["Calcul"], b.RepechageSubjects);
            var layout = EpstBulletinLayout.For(print, print.Pupils.Single(p => p.EnrollmentId == k.Pupils[1]));
            Assert.Equal("REPÊCHAGE", layout.Decision);
            Assert.Single(layout.Mentions, m => !m.Struck);
            Assert.Contains("repêchage en : Calcul", layout.Mentions.Single(m => !m.Struck).Text);
            Assert.Equal("Passe", print.Details!.Single(x => x.EnrollmentId == k.Pupils[0]).Outcome);
        }

        await admin.Put($"{route}/eleves/{k.Pupils[1]}/repechage",
            new { marks = new[] { new { subjectId = k.MathsId, score = 12m, maxScore = 20m } } }).Ok();
        await admin.Post($"{route}/cloture", new { }).Ok();
        using (var scope = app.Services.CreateScope())
        {
            var print = await scope.ServiceProvider.GetRequiredService<EpstResultsService>().PrintAsync(k.ClassId, k.Pupils[1]);
            var b = Assert.Single(print.Details!);
            Assert.Equal("PasseApresRepechage", b.Outcome);
            Assert.Equal(60m, Assert.Single(b.RepechageScores).Percent);
            var layout = EpstBulletinLayout.For(print, print.Pupils[0]);
            Assert.Equal("PASSE APRÈS REPÊCHAGE", layout.Decision);
            Assert.False(layout.Mentions[0].Struck);
            Assert.All(layout.Mentions.Skip(1), m => Assert.True(m.Struck));
        }

        var pdf = await admin.GetAsync($"/api/etablissement/cycles/promotions/{k.ClassId}/bulletins.pdf");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        var dump = Environment.GetEnvironmentVariable("SCOLIA_BULLETIN_DUMP");
        if (!string.IsNullOrEmpty(dump)) await File.WriteAllBytesAsync(dump, bytes);
    }
}
