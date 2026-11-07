using System.Net;
using System.Text.Json;

namespace Scolia.Tests;

/// <summary>
/// Délibération (fusion d'ITMCD) de bout en bout : codes anonymes pour les apprenants déjà inscrits,
/// encodage par feuille et par code, tableau du jury, décision motivée, clôture figée, réouverture et passage de classe.
/// </summary>
public sealed class DeliberationApiTests : IDisposable
{
    private readonly ScoliaApp app = new();
    private const string D = "/api/deliberation";

    public void Dispose() => app.Dispose();

    private sealed record Setup(string PromotionId, List<string> Enrollments, string S1Course, string AnnualCourse);

    private static async Task<Setup> SetupAsync(HttpClient admin, params string[] names)
    {
        var promotion = await admin.Post("/api/etablissement/promotions", new
        {
            code = "INF1", name = "Première année", level = 1, isActive = true, monthlyFee = 55000m, oneTimeFees = Array.Empty<object>(), nextPromotionCode = "INF2",
        }).Ok();
        var promotionId = promotion.GetProperty("id").GetString()!;
        var enrollments = new List<string>();
        foreach (var name in names)
        {
            var student = await admin.Post("/api/apprenants", new
            {
                identity = new { lastName = name, middleName = "Kabeya", firstName = "Grace", birthPlace = "Lubumbashi", birthDate = new DateOnly(2007, 3, 14), gender = "F", guardianPhone = "0812345678", guardianName = "Parent" },
                promotionId, enrolledOn = DateOnly.FromDateTime(DateTime.Today), confirmDuplicate = false,
            }).Ok();
            enrollments.Add(student.GetProperty("enrollments")[0].GetProperty("id").GetString()!);
        }
        var s1 = await admin.Post("/api/scolarite/cours", new { promotionId, number = 1, name = "Anatomie", coefficient = 1m, hours = 30m, maxScore = 100m, semester = "S1" }).Ok();
        var annual = await admin.Post("/api/scolarite/cours", new { promotionId, number = 2, name = "Soins infirmiers", coefficient = 1m, hours = 60m, maxScore = 50m, semester = "Annuel" }).Ok();
        Assert.Equal("S1", s1.GetProperty("semester").GetString());
        return new Setup(promotionId, enrollments, s1.GetProperty("id").GetString()!, annual.GetProperty("id").GetString()!);
    }

    private static async Task<Dictionary<string, string>> CodesAsync(HttpClient admin, string promotionId) =>
        (await admin.GetAsync($"{D}/codes?promotionId={promotionId}").Ok()).EnumerateArray()
            .ToDictionary(r => r.GetProperty("enrollmentId").GetString()!, r => r.GetProperty("code").GetString()!);

    private static JsonElement Row(JsonElement board, string enrollmentId) =>
        board.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("enrollmentId").GetString() == enrollmentId);

    [Fact]
    public async Task Existing_students_receive_unique_official_codes_once()
    {
        var admin = await app.InstalledAdmin();
        var setup = await SetupAsync(admin, "MUKENDI", "KASONGO", "ILUNGA");

        var first = await admin.Post($"{D}/codes/attribution", new { promotionId = setup.PromotionId }).Ok();
        Assert.Equal(3, first.GetProperty("assigned").GetInt32());
        var again = await admin.Post($"{D}/codes/attribution", new { promotionId = setup.PromotionId }).Ok();
        Assert.Equal(0, again.GetProperty("assigned").GetInt32());
        Assert.Equal(3, again.GetProperty("alreadyCoded").GetInt32());

        var codes = await CodesAsync(admin, setup.PromotionId);
        Assert.Equal(3, codes.Values.Distinct().Count());
        Assert.All(codes.Values, c => Assert.Matches("^[A-Z][0-9]{2}[A-Z][0-9][A-Z]$", c));

        // Code officiel d'ITMCD saisi à la main ; remplacer exige un motif ; un code déjà pris est refusé.
        var target = setup.Enrollments[0];
        var (noReason, _) = await admin.Put($"{D}/codes/{target}", new { code = "itm-26-ab12", reason = (string?)null }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, noReason);
        var manual = await admin.Put($"{D}/codes/{target}", new { code = "itm-26-ab12", reason = "Code officiel ITMCD" }).Ok();
        Assert.Equal("ITM-26-AB12", manual.GetProperty("code").GetString());
        Assert.True(manual.GetProperty("isManual").GetBoolean());
        var (taken, _) = await admin.Put($"{D}/codes/{setup.Enrollments[1]}", new { code = "ITM-26-AB12", reason = "Essai de doublon" }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, taken);
        // L'ancien code n'est jamais réattribué ni reconnu.
        var (reuse, _) = await admin.Put($"{D}/codes/{setup.Enrollments[1]}", new { code = codes[target], reason = "Essai de réemploi" }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, reuse);
        var lookup = await admin.GetAsync($"{D}/codes/resolution?code=itm-26-ab12").Ok();
        Assert.Equal(target, lookup.GetProperty("enrollmentId").GetString());

        var list = await admin.GetAsync($"{D}/codes/liste?promotionId={setup.PromotionId}&format=xlsx");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task Anonymous_entry_and_sheet_feed_the_jury_board()
    {
        var admin = await app.InstalledAdmin();
        var setup = await SetupAsync(admin, "MUKENDI", "KASONGO");
        await admin.Post($"{D}/codes/attribution", new { promotionId = setup.PromotionId }).Ok();
        var codes = await CodesAsync(admin, setup.PromotionId);
        var (good, weak) = (setup.Enrollments[0], setup.Enrollments[1]);

        // Saisie anonyme : le code et la cote, la feuille anonyme ne montre aucun nom.
        var byCode = await admin.Post($"{D}/cotes-par-code", new { code = codes[good].ToLowerInvariant(), courseId = setup.S1Course, kind = "S1", value = 80m }).Ok();
        Assert.Equal(1, byCode.GetProperty("remaining").GetInt32());
        var (tooHigh, _) = await admin.Post($"{D}/cotes-par-code", new { code = codes[weak], courseId = setup.S1Course, kind = "S1", value = 101m }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, tooHigh);
        var (wrongTerm, _) = await admin.Post($"{D}/cotes-par-code", new { code = codes[weak], courseId = setup.S1Course, kind = "S2", value = 10m }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, wrongTerm);
        var (unknown, _) = await admin.Post($"{D}/cotes-par-code", new { code = "Z99Z9Z", courseId = setup.S1Course, kind = "S1", value = 10m }).Fail();
        Assert.Equal(HttpStatusCode.NotFound, unknown);
        var anonymousSheet = await admin.GetAsync($"{D}/feuille?coursId={setup.S1Course}&nature=S1&anonyme=true").Ok();
        Assert.All(anonymousSheet.GetProperty("rows").EnumerateArray(), r => Assert.Equal(JsonValueKind.Null, r.GetProperty("fullName").ValueKind));

        // Feuille nominative : cote S1 faible avec appui, et le cours annuel.
        await admin.Put($"{D}/feuille?coursId={setup.S1Course}&nature=S1", new[] { new { enrollmentId = weak, value = (decimal?)40m } }).Ok();
        await admin.Put($"{D}/feuille?coursId={setup.S1Course}&nature=Appui", new[] { new { enrollmentId = weak, value = (decimal?)8m } }).Ok();
        await admin.Put($"{D}/feuille?coursId={setup.AnnualCourse}&nature=S1", new[] { new { enrollmentId = good, value = (decimal?)40m }, new { enrollmentId = weak, value = (decimal?)20m } }).Ok();

        // Premier semestre : l'appui relève 40 en 40 + 0,8 × 25 = 60 sur 100 ; le cours annuel compte (S1 seul au S1).
        var s1 = await admin.GetAsync($"{D}/resultats?promotionId={setup.PromotionId}&periode=S1").Ok();
        Assert.Equal("Admis", Row(s1, good).GetProperty("decision").GetString());
        Assert.Equal(80m, Row(s1, weak).GetProperty("points").GetDecimal());
        Assert.Equal(53.33m, Row(s1, weak).GetProperty("percent").GetDecimal());
        Assert.Equal("Ajourné", Row(s1, weak).GetProperty("decision").GetString());
        Assert.Equal(1, Row(s1, good).GetProperty("rank").GetInt32());

        // Jury annuel : le cours annuel sans cote S2 est une cote manquante.
        var final = await admin.GetAsync($"{D}/resultats?promotionId={setup.PromotionId}&periode=Annuel").Ok();
        Assert.Equal("Incomplet", Row(final, good).GetProperty("decision").GetString());
        Assert.Equal(1, Row(final, good).GetProperty("missing").GetInt32());

        var detail = await admin.GetAsync($"{D}/resultats/{weak}?promotionId={setup.PromotionId}&periode=S1").Ok();
        var anatomy = detail.GetProperty("courses").EnumerateArray().First();
        Assert.Equal(20m, anatomy.GetProperty("bonus").GetDecimal());
    }

    [Fact]
    public async Task Closing_freezes_results_locks_marks_and_reopening_requires_a_reason()
    {
        var admin = await app.InstalledAdmin();
        var setup = await SetupAsync(admin, "MUKENDI", "KASONGO");
        var (good, weak) = (setup.Enrollments[0], setup.Enrollments[1]);
        await admin.Put($"{D}/feuille?coursId={setup.S1Course}&nature=S1", new[] { new { enrollmentId = good, value = (decimal?)90m }, new { enrollmentId = weak, value = (decimal?)55m } }).Ok();
        await admin.Put($"{D}/feuille?coursId={setup.AnnualCourse}&nature=S1", new[] { new { enrollmentId = good, value = (decimal?)45m }, new { enrollmentId = weak, value = (decimal?)25m } }).Ok();

        // Décision motivée : le jury admet l'apprenant à 53,33 % ; un motif est obligatoire.
        var (unmotivated, _) = await admin.Put($"{D}/decisions", new { promotionId = setup.PromotionId, period = "S1", enrollmentId = weak, outcome = "Admis", reason = "" }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, unmotivated);
        var decided = await admin.Put($"{D}/decisions", new { promotionId = setup.PromotionId, period = "S1", enrollmentId = weak, outcome = "Admis", reason = "Progrès constants au second trimestre" }).Ok();
        Assert.Equal("Admis par délibération", decided.GetProperty("decision").GetString());
        Assert.Equal("Ajourné", decided.GetProperty("computed").GetString());

        var (badPassword, _) = await admin.Post($"{D}/cloture", new { promotionId = setup.PromotionId, period = "S1", password = "faux-mot-de-passe", president = "Dr MUKENGE", members = "", notes = "" }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, badPassword);
        var closed = await admin.Post($"{D}/cloture", new { promotionId = setup.PromotionId, period = "S1", password = "Scolia2026!", president = "Dr MUKENGE", members = "M. ILUNGA, Mme KASONGO", notes = "" }).Ok();
        Assert.Equal("Clôturée", closed.GetProperty("status").GetString());
        Assert.Equal(2, closed.GetProperty("admitted").GetInt32());

        // Cotes S1 figées ; décisions figées ; documents disponibles.
        var (locked, _) = await admin.Put($"{D}/feuille?coursId={setup.S1Course}&nature=S1", new[] { new { enrollmentId = weak, value = (decimal?)70m } }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, locked);
        var (frozenDecision, _) = await admin.Put($"{D}/decisions", new { promotionId = setup.PromotionId, period = "S1", enrollmentId = weak, outcome = (string?)null, reason = (string?)null }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, frozenDecision);
        // Le cours annuel reste encodable pour le S2.
        await admin.Put($"{D}/feuille?coursId={setup.AnnualCourse}&nature=S2", new[] { new { enrollmentId = good, value = (decimal?)45m } }).Ok();
        foreach (var path in new[] { "proces-verbal.pdf", "grille?format=xlsx&x=1", "grille", "palmares", $"bulletin.pdf?inscriptionId={weak}&x=1" })
        {
            var separator = path.Contains('?') ? '&' : '?';
            var response = await admin.GetAsync($"{D}/{path}{separator}promotionId={setup.PromotionId}&periode=S1");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var (shortReason, _) = await admin.Post($"{D}/reouverture", new { promotionId = setup.PromotionId, period = "S1", password = "Scolia2026!", reason = "Erreur" }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, shortReason);
        var reopened = await admin.Post($"{D}/reouverture", new { promotionId = setup.PromotionId, period = "S1", password = "Scolia2026!", reason = "Erreur de report d'une copie d'anatomie" }).Ok();
        Assert.Equal("Ouverte", reopened.GetProperty("status").GetString());
        Assert.Equal(1, reopened.GetProperty("reopenCount").GetInt32());
        await admin.Put($"{D}/feuille?coursId={setup.S1Course}&nature=S1", new[] { new { enrollmentId = weak, value = (decimal?)70m } }).Ok();
    }

    [Fact]
    public async Task Closed_encoding_period_and_disabled_support_are_refused()
    {
        var admin = await app.InstalledAdmin();
        var setup = await SetupAsync(admin, "MUKENDI");
        await admin.Put($"{D}/reglages", new { encodingS1Open = false, encodingS2Open = true, supportEnabled = false, passPercent = 60m }).Ok();
        var (s1Closed, _) = await admin.Put($"{D}/feuille?coursId={setup.S1Course}&nature=S1", new[] { new { enrollmentId = setup.Enrollments[0], value = (decimal?)50m } }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, s1Closed);
        var (noSupport, _) = await admin.Put($"{D}/feuille?coursId={setup.S1Course}&nature=Appui", new[] { new { enrollmentId = setup.Enrollments[0], value = (decimal?)5m } }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, noSupport);
        await admin.Put($"{D}/feuille?coursId={setup.AnnualCourse}&nature=S2", new[] { new { enrollmentId = setup.Enrollments[0], value = (decimal?)30m } }).Ok();
        var courses = (await admin.GetAsync($"{D}/cours?promotionId={setup.PromotionId}").Ok()).EnumerateArray().ToList();
        Assert.Equal(1, courses.Single(c => c.GetProperty("id").GetString() == setup.AnnualCourse).GetProperty("gradedS2").GetInt32());

        // Le semestre d'un cours qui a des cotes ne change plus.
        var (semesterChange, _) = await admin.Put($"/api/scolarite/cours/{setup.AnnualCourse}", new
        {
            promotionId = setup.PromotionId, number = 2, name = "Soins infirmiers", coefficient = 1m, hours = 60m, maxScore = 50m, semester = "S1",
        }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, semesterChange);
    }

    [Fact]
    public async Task Annual_jury_drives_the_re_enrollment_plan()
    {
        var admin = await app.InstalledAdmin();
        var setup = await SetupAsync(admin, "MUKENDI", "KASONGO");
        var (good, weak) = (setup.Enrollments[0], setup.Enrollments[1]);
        await admin.Put($"{D}/feuille?coursId={setup.S1Course}&nature=S1", new[] { new { enrollmentId = good, value = (decimal?)80m }, new { enrollmentId = weak, value = (decimal?)30m } }).Ok();
        foreach (var kind in new[] { "S1", "S2" })
            await admin.Put($"{D}/feuille?coursId={setup.AnnualCourse}&nature={kind}", new[] { new { enrollmentId = good, value = (decimal?)40m }, new { enrollmentId = weak, value = (decimal?)20m } }).Ok();
        await admin.Post($"{D}/cloture", new { promotionId = setup.PromotionId, period = "Annuel", password = "Scolia2026!", president = "Dr MUKENGE", members = "", notes = "" }).Ok();

        var first = int.Parse(Http.Year(DateOnly.FromDateTime(DateTime.Today))[..4], System.Globalization.CultureInfo.InvariantCulture);
        var future = await admin.Post("/api/etablissement/annees", new
        {
            label = $"{first + 1}-{first + 2}", startMonth = 9, monthCount = 10, dueDay = 6, copyPromotionsFromCurrent = false, makeCurrent = false,
        }).Ok();
        var year = future.GetProperty("id").GetString()!;
        var plan = (await admin.GetAsync($"/api/reinscriptions/plan?anneeId={year}").Ok()).EnumerateArray().ToList();
        Assert.Equal(2, plan.Count);
        var promoted = plan.Single(p => p.GetProperty("fullName").GetString()!.StartsWith("MUKENDI", StringComparison.Ordinal));
        var repeating = plan.Single(p => p.GetProperty("fullName").GetString()!.StartsWith("KASONGO", StringComparison.Ordinal));
        Assert.Equal("INF2", promoted.GetProperty("toPromotion").GetString());
        Assert.Equal("Admis", promoted.GetProperty("juryDecision").GetString());
        Assert.Equal("INF1", repeating.GetProperty("toPromotion").GetString());
        Assert.Equal("Ajourné", repeating.GetProperty("juryDecision").GetString());

        // Le redoublement crée la promotion INF1 de la nouvelle année.
        var count = await admin.Post("/api/reinscriptions", new
        {
            targetYearId = year,
            choices = new[] { new { studentId = repeating.GetProperty("studentId").GetString(), promotionId = (Guid?)null, promotionCode = "INF1" } },
        }).Ok();
        Assert.Equal(1, count.GetInt32());
    }
}
