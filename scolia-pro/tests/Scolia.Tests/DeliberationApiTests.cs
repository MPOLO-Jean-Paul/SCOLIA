using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public async Task Submitted_sheet_is_tracked_and_only_the_jury_returns_it()
    {
        var admin = await app.InstalledAdmin();
        var setup = await SetupAsync(admin, "MUKENDI", "KASONGO");
        var (good, weak) = (setup.Enrollments[0], setup.Enrollments[1]);
        await admin.Put($"{D}/feuille?coursId={setup.S1Course}&nature=S1", new[] { new { enrollmentId = good, value = (decimal?)80m } }).Ok();

        // Feuille incomplète : la remise est refusée, comme le dépôt d'ITMCD tant qu'il manque une cote.
        var (incomplete, body) = await admin.Post($"{D}/remise?coursId={setup.S1Course}&nature=S1", new { }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, incomplete);
        Assert.Contains("1 cote(s) manquante(s)", body.GetProperty("title").GetString());

        await admin.Put($"{D}/feuille?coursId={setup.S1Course}&nature=S1", new[] { new { enrollmentId = weak, value = (decimal?)30m } }).Ok();
        var submitted = await admin.Post($"{D}/remise?coursId={setup.S1Course}&nature=S1", new { }).Ok();
        Assert.Equal(2, submitted.GetProperty("graded").GetInt32());
        var (twice, _) = await admin.Post($"{D}/remise?coursId={setup.S1Course}&nature=S1", new { }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, twice);

        var sheet = await admin.GetAsync($"{D}/feuille?coursId={setup.S1Course}&nature=S1").Ok();
        Assert.NotEqual(JsonValueKind.Null, sheet.GetProperty("submittedAtUtc").ValueKind);
        Assert.Equal(1, sheet.GetProperty("failed").GetInt32());
        Assert.Equal(55m, sheet.GetProperty("average").GetDecimal());
        var courses = (await admin.GetAsync($"{D}/cours?promotionId={setup.PromotionId}").Ok()).EnumerateArray()
            .Single(c => c.GetProperty("id").GetString() == setup.S1Course);
        Assert.True(courses.GetProperty("submittedS1").GetBoolean());

        // Le jury (secrétariat) corrige encore une feuille remise : la correction est tracée.
        await admin.Put($"{D}/feuille?coursId={setup.S1Course}&nature=S1", new[] { new { enrollmentId = weak, value = (decimal?)35m } }).Ok();

        // Suivi de l'encodage : la feuille S1 est remise, le cours annuel reste à encoder pour S1 et S2.
        var progress = await admin.GetAsync($"{D}/suivi?promotionId={setup.PromotionId}").Ok();
        var rows = progress.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(3, rows.Count);
        var s1Row = rows.Single(r => r.GetProperty("courseId").GetString() == setup.S1Course);
        Assert.Equal("Remis", s1Row.GetProperty("status").GetString());
        Assert.Equal(1, s1Row.GetProperty("failed").GetInt32());
        Assert.All(rows.Where(r => r.GetProperty("courseId").GetString() == setup.AnnualCourse), r => Assert.Equal("À encoder", r.GetProperty("status").GetString()));
        Assert.Equal(1, progress.GetProperty("submitted").GetInt32());
        Assert.Equal(2, (await admin.GetAsync($"{D}/suivi?promotionId={setup.PromotionId}&periode=S1").Ok()).GetProperty("rows").GetArrayLength());

        foreach (var url in new[]
        {
            $"{D}/suivi/export?promotionId={setup.PromotionId}&format=xlsx",
            $"{D}/suivi/export?promotionId={setup.PromotionId}",
            $"{D}/feuille/export?coursId={setup.S1Course}&nature=S1&format=xlsx",
            $"{D}/feuille/export?coursId={setup.S1Course}&nature=S1&echecs=true",
        })
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode);

        // Renvoi à l'enseignant : motif obligatoire, puis la feuille redevient non remise.
        var (noReason, _) = await admin.Post($"{D}/renvoi?coursId={setup.S1Course}&nature=S1", new { reason = "" }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, noReason);
        var returned = await admin.Post($"{D}/renvoi?coursId={setup.S1Course}&nature=S1", new { reason = "Erreur de report sur deux copies" });
        Assert.Equal(HttpStatusCode.NoContent, returned.StatusCode);
        var after = await admin.GetAsync($"{D}/feuille?coursId={setup.S1Course}&nature=S1").Ok();
        Assert.Equal(JsonValueKind.Null, after.GetProperty("submittedAtUtc").ValueKind);
        var (nothing, _) = await admin.Post($"{D}/renvoi?coursId={setup.S1Course}&nature=S1", new { reason = "Encore une fois" }).Fail();
        Assert.Equal(HttpStatusCode.NotFound, nothing);
    }

    [Fact]
    public async Task Official_codes_from_itmcd_are_previewed_then_imported()
    {
        var admin = await app.InstalledAdmin();
        var setup = await SetupAsync(admin, "MUKENDI", "KASONGO", "ILUNGA");
        const string list = "Nom;Promotion;Code\nMukendi Kabeya Grace;INF1;itm-26-ab12\nKASONGO KABEYA GRÂCE;INF1;P13W5R\nInconnu Personne;INF1;X12Y3Z\nILUNGA Kabeya Grace;INF1;??\n";

        // Aperçu : rien n'est enregistré.
        var preview = await admin.Post($"{D}/codes/import", new { text = list, promotionId = setup.PromotionId, apply = false }).Ok();
        Assert.False(preview.GetProperty("applied").GetBoolean());
        Assert.Equal(2, preview.GetProperty("ready").GetInt32());
        var statuses = preview.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("status").GetString()!).ToArray();
        Assert.Equal(["À attribuer", "À attribuer", "Introuvable", "Code invalide"], statuses);
        Assert.All((await CodesAsync(admin, setup.PromotionId)).Values, c => Assert.Null(c));

        var applied = await admin.Post($"{D}/codes/import", new { text = list, promotionId = setup.PromotionId, apply = true }).Ok();
        Assert.Equal(2, applied.GetProperty("assigned").GetInt32());
        var codes = await CodesAsync(admin, setup.PromotionId);
        Assert.Equal("ITM-26-AB12", codes[setup.Enrollments[0]]);
        Assert.Equal("P13W5R", codes[setup.Enrollments[1]]);
        Assert.Equal(setup.Enrollments[1], (await admin.GetAsync($"{D}/codes/resolution?code=p13w5r").Ok()).GetProperty("enrollmentId").GetString());

        // Relancer ne change rien ; un code déjà porté par un autre apprenant est refusé.
        var again = await admin.Post($"{D}/codes/import", new { text = list, promotionId = setup.PromotionId, apply = true }).Ok();
        Assert.Equal(0, again.GetProperty("assigned").GetInt32());
        Assert.Equal(2, again.GetProperty("unchanged").GetInt32());
        var taken = await admin.Post($"{D}/codes/import", new { text = "ILUNGA Kabeya Grace;P13W5R", promotionId = setup.PromotionId, apply = true }).Ok();
        Assert.Equal("Code pris", taken.GetProperty("lines")[0].GetProperty("status").GetString());
        Assert.Null((await CodesAsync(admin, setup.PromotionId))[setup.Enrollments[2]]);
    }

    [Fact]
    public async Task Teacher_account_encodes_only_assigned_courses_anonymously()
    {
        var admin = await app.InstalledAdmin();
        var setup = await SetupAsync(admin, "MUKENDI", "KASONGO");
        await admin.Post($"{D}/codes/attribution", new { promotionId = setup.PromotionId }).Ok();
        var codes = await CodesAsync(admin, setup.PromotionId);
        var staff = await admin.Post("/api/personnel", new
        {
            lastName = "LUMBALA", middleName = "", firstName = "Paul", gender = "M", function = "Enseignant", phone = "0812345678", email = (string?)null,
            hireDate = new DateOnly(2020, 1, 1), exitDate = (DateOnly?)null, baseSalary = 300000m, isActive = true,
        }).Ok();
        var staffId = staff.GetProperty("id").GetString()!;
        await admin.Put($"/api/scolarite/cours/{setup.S1Course}/attribution", new { teacherId = staffId, confirmReplace = false }).Ok();

        var created = await admin.Post("/api/utilisateurs", new { userName = "prof.lumbala", displayName = "Pr LUMBALA", role = "Enseignant", password = "Provisoire1", staffMemberId = staffId }).Ok();
        Assert.Equal(staffId, created.GetProperty("staffMemberId").GetString());
        var (second, _) = await admin.Post("/api/utilisateurs", new { userName = "prof.bis", displayName = "Bis", role = "Enseignant", password = "Provisoire1", staffMemberId = staffId }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, second);
        var teacher = await app.Login("prof.lumbala", "Provisoire1");

        // Mes cours : seulement le cours attribué, rien d'autre de la scolarité.
        var mine = (await teacher.GetAsync($"{D}/mes-cours").Ok()).EnumerateArray().ToList();
        Assert.Equal(setup.S1Course, Assert.Single(mine).GetProperty("course").GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"{D}/resultats?promotionId={setup.PromotionId}&periode=S1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"{D}/codes?promotionId={setup.PromotionId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"/api/apprenants")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"{D}/feuille?coursId={setup.AnnualCourse}&nature=S1")).StatusCode);
        var (other, _) = await teacher.Post($"{D}/cotes-par-code", new { code = codes[setup.Enrollments[0]], courseId = setup.AnnualCourse, kind = "S1", value = 10m }).Fail();
        Assert.Equal(HttpStatusCode.Forbidden, other);

        // Sa feuille est toujours anonyme : les codes, jamais les noms, même s'il demande la feuille nominative.
        var sheet = await teacher.GetAsync($"{D}/feuille?coursId={setup.S1Course}&nature=S1&anonyme=false").Ok();
        Assert.True(sheet.GetProperty("anonymous").GetBoolean());
        Assert.All(sheet.GetProperty("rows").EnumerateArray(), r =>
        {
            Assert.Equal(JsonValueKind.Null, r.GetProperty("fullName").ValueKind);
            Assert.Equal(JsonValueKind.String, r.GetProperty("code").ValueKind);
        });

        // Encodage par code et par feuille, puis remise : l'enseignant ne peut plus modifier, le jury si.
        await teacher.Post($"{D}/cotes-par-code", new { code = codes[setup.Enrollments[0]], courseId = setup.S1Course, kind = "S1", value = 75m }).Ok();
        await teacher.Put($"{D}/feuille?coursId={setup.S1Course}&nature=S1", new[] { new { enrollmentId = setup.Enrollments[1], value = (decimal?)45m } }).Ok();
        await teacher.Post($"{D}/remise?coursId={setup.S1Course}&nature=S1", new { }).Ok();
        var (locked, _) = await teacher.Put($"{D}/feuille?coursId={setup.S1Course}&nature=S1", new[] { new { enrollmentId = setup.Enrollments[1], value = (decimal?)50m } }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, locked);
        await admin.Put($"{D}/feuille?coursId={setup.S1Course}&nature=S1", new[] { new { enrollmentId = setup.Enrollments[1], value = (decimal?)50m } }).Ok();
        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync($"{D}/feuille/export?coursId={setup.S1Course}&nature=S1&format=xlsx")).StatusCode);
        Assert.True((await teacher.GetAsync($"{D}/mes-cours").Ok())[0].GetProperty("course").GetProperty("submittedS1").GetBoolean());

        // Annonces (notifications d'ITMCD) : la remise de son cours, et l'ouverture ou la fermeture de l'encodage.
        await admin.Put($"{D}/reglages", new { encodingS1Open = true, encodingS2Open = false, supportEnabled = true, passPercent = 60m }).Ok();
        var notices = (await teacher.GetAsync($"{D}/annonces").Ok()).EnumerateArray().Select(n => n.GetProperty("title").GetString()).ToList();
        Assert.Contains("Cotes remises", notices);
        Assert.Contains("Encodage des cotes", notices);

        // Cours retiré : l'enseignant n'y a plus accès.
        await admin.Post($"/api/scolarite/cours/{setup.S1Course}/retrait-attribution", new { reason = "Fin de la charge du semestre" }).Ok();
        Assert.Empty((await teacher.GetAsync($"{D}/mes-cours").Ok()).EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"{D}/feuille?coursId={setup.S1Course}&nature=S1")).StatusCode);
    }

    [Fact]
    public async Task Jury_evaluations_authorities_and_official_grid_from_the_deliberation_project()
    {
        var admin = await app.InstalledAdmin();
        var setup = await SetupAsync(admin, "MUKENDI");
        var student = setup.Enrollments[0];

        // Autorités : quatre membres au plus ; elles signent les documents.
        var (tooMany, _) = await admin.Put($"{D}/autorites", new { prefect = "P", principal = "Q", juryPresident = "R", jurySecretary = "S", juryMembers = new[] { "a", "b", "c", "d", "e" } }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, tooMany);
        var authorities = await admin.Put($"{D}/autorites", new
        {
            prefect = "Abbé KALALA", principal = "Mme NGOY", juryPresident = "Dr MUKENGE", jurySecretary = "M. KABONGO", juryMembers = new[] { "Mme ILUNGA", "M. TSHIBANDA" },
        }).Ok();
        Assert.Equal(2, authorities.GetProperty("juryMembers").GetArrayLength());
        Assert.Equal("Dr MUKENGE", (await admin.GetAsync($"{D}/autorites").Ok()).GetProperty("juryPresident").GetString());

        // Épreuves du jury standard : créées une fois, au jury annuel seulement.
        var created = await admin.Post($"{D}/epreuves-jury?promotionId={setup.PromotionId}", new { }).Ok();
        Assert.Equal(7, created.GetProperty("created").GetInt32());
        Assert.Equal(0, (await admin.Post($"{D}/epreuves-jury?promotionId={setup.PromotionId}", new { }).Ok()).GetProperty("created").GetInt32());
        var courses = (await admin.GetAsync($"{D}/cours?promotionId={setup.PromotionId}").Ok()).EnumerateArray().ToList();
        Assert.Equal(9, courses.Count);
        var jury = courses.Where(c => c.GetProperty("semester").GetString()!.StartsWith("Jury", StringComparison.Ordinal)).ToList();
        Assert.Equal(7, jury.Count);
        var s1Board = await admin.GetAsync($"{D}/resultats?promotionId={setup.PromotionId}&periode=S1").Ok();
        Assert.Equal(2, s1Board.GetProperty("courses").GetArrayLength());

        // Cotes : cours ordinaires et épreuves du jury (jamais une cote S1 sur une épreuve).
        await admin.Put($"{D}/feuille?coursId={setup.S1Course}&nature=S1", new[] { new { enrollmentId = student, value = (decimal?)70m } }).Ok();
        foreach (var kind in new[] { "S1", "S2" })
            await admin.Put($"{D}/feuille?coursId={setup.AnnualCourse}&nature={kind}", new[] { new { enrollmentId = student, value = (decimal?)40m } }).Ok();
        var stage = jury.Single(c => c.GetProperty("name").GetString() == "Stage").GetProperty("id").GetString();
        var (wrongKind, _) = await admin.Put($"{D}/feuille?coursId={stage}&nature=S1", new[] { new { enrollmentId = student, value = (decimal?)10m } }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, wrongKind);
        foreach (var c in jury)
        {
            var max = c.GetProperty("maxScore").GetDecimal();
            var value = c.GetProperty("name").GetString() == "Bloc gestion" ? max / 5 : max * 0.8m;
            await admin.Put($"{D}/feuille?coursId={c.GetProperty("id").GetString()}&nature=Jury", new[] { new { enrollmentId = student, value = (decimal?)value } }).Ok();
        }

        // Jury annuel : TOTAL 1 = 0,8 × 515, SOUS-TOTAL 1 = 70 + 40, SOUS-TOTAL 2 = 48 + 40 + 10 (bloc gestion en échec) → code A.
        var final = await admin.GetAsync($"{D}/resultats?promotionId={setup.PromotionId}&periode=Annuel").Ok();
        var row = Row(final, student);
        Assert.Equal("Admis", row.GetProperty("decision").GetString());
        Assert.Equal("A", row.GetProperty("grade").GetString());
        var totals = row.GetProperty("totals");
        Assert.Equal(412m, totals.GetProperty("total1").GetDecimal());
        Assert.Equal(515m, totals.GetProperty("total1Max").GetDecimal());
        Assert.Equal(110m, totals.GetProperty("subtotal1").GetDecimal());
        Assert.Equal(150m, totals.GetProperty("subtotal1Max").GetDecimal());
        Assert.Equal(98m, totals.GetProperty("subtotal2").GetDecimal());
        Assert.Equal(160m, totals.GetProperty("subtotal2Max").GetDecimal());
        Assert.Equal(620m, totals.GetProperty("general").GetDecimal());
        Assert.Equal(totals.GetProperty("general").GetDecimal(), row.GetProperty("points").GetDecimal());

        foreach (var url in new[]
        {
            $"{D}/grille?promotionId={setup.PromotionId}&periode=Annuel&format=xlsx",
            $"{D}/grille?promotionId={setup.PromotionId}&periode=Annuel",
            $"{D}/bulletin.pdf?promotionId={setup.PromotionId}&periode=Annuel&inscriptionId={student}",
            $"{D}/proces-verbal.pdf?promotionId={setup.PromotionId}&periode=Annuel",
            $"{D}/bulletins.pdf?promotionId={setup.PromotionId}&periode=Annuel",
            $"{D}/bulletins.pdf?promotionId={setup.PromotionId}&periode=S1",
        })
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode);

        // Clôture sans président saisi : le président enregistré dans les autorités.
        var closed = await admin.Post($"{D}/cloture", new { promotionId = setup.PromotionId, period = "Annuel", password = "Scolia2026!", president = "", members = "", notes = "" }).Ok();
        Assert.Equal("Dr MUKENGE", closed.GetProperty("president").GetString());
        Assert.Contains("Mme ILUNGA", closed.GetProperty("members").GetString());
        Assert.Equal("A", Row(closed, student).GetProperty("grade").GetString());
        var (lockedJury, _) = await admin.Put($"{D}/feuille?coursId={stage}&nature=Jury", new[] { new { enrollmentId = student, value = (decimal?)100m } }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, lockedJury);
    }

    [Fact]
    public async Task Jury_privilege_lets_a_teacher_encode_jury_evaluations_and_can_be_withdrawn()
    {
        var admin = await app.InstalledAdmin();
        var setup = await SetupAsync(admin, "MUKENDI", "KASONGO");
        await admin.Post($"{D}/codes/attribution", new { promotionId = setup.PromotionId }).Ok();
        var codes = await CodesAsync(admin, setup.PromotionId);
        await admin.Post($"{D}/epreuves-jury?promotionId={setup.PromotionId}", new { }).Ok();
        var stage = (await admin.GetAsync($"{D}/cours?promotionId={setup.PromotionId}").Ok()).EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == "Stage").GetProperty("id").GetString()!;
        var staff = await admin.Post("/api/personnel", new
        {
            lastName = "KAPEND", middleName = "", firstName = "Rose", gender = "F", function = "Enseignante", phone = "0812345678", email = (string?)null,
            hireDate = new DateOnly(2020, 1, 1), exitDate = (DateOnly?)null, baseSalary = 300000m, isActive = true,
        }).Ok();
        var staffId = staff.GetProperty("id").GetString()!;
        var account = await admin.Post("/api/utilisateurs", new { userName = "prof.kapend", displayName = "Pr KAPEND", role = "Enseignant", password = "Provisoire1", staffMemberId = staffId }).Ok();
        var userId = account.GetProperty("id").GetString()!;
        Assert.False(account.GetProperty("juryAccess").GetBoolean());

        // Sans le privilège : aucune épreuve du jury.
        var teacher = await app.Login("prof.kapend", "Provisoire1");
        Assert.Empty((await teacher.GetAsync($"{D}/mes-cours").Ok()).EnumerateArray());
        var (denied, _) = await teacher.Post($"{D}/cotes-par-code", new { code = codes[setup.Enrollments[0]], courseId = stage, kind = "Jury", value = 100m }).Fail();
        Assert.Equal(HttpStatusCode.Forbidden, denied);

        // Le secrétariat accorde le privilège : c'est le même compte, la session est simplement renouvelée.
        var granted = await admin.Put($"/api/utilisateurs/{userId}", new { displayName = "Pr KAPEND", role = "Enseignant", isActive = true, staffMemberId = staffId, juryAccess = true }).Ok();
        Assert.True(granted.GetProperty("juryAccess").GetBoolean());
        Assert.Equal("Enseignant · membre du jury", granted.GetProperty("roleLabel").GetString());
        teacher = await app.Login("prof.kapend", "Provisoire1");
        Assert.Contains("jury.encoder", (await teacher.GetAsync("/api/session").Ok()).GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
        var mine = (await teacher.GetAsync($"{D}/mes-cours").Ok()).EnumerateArray().ToList();
        Assert.Equal(7, mine.Count);
        Assert.All(mine, m => Assert.StartsWith("Jury", m.GetProperty("course").GetProperty("semester").GetString(), StringComparison.Ordinal));

        // Encodage anonyme des épreuves, sans devenir titulaire ni toucher aux cours ordinaires.
        await teacher.Post($"{D}/cotes-par-code", new { code = codes[setup.Enrollments[0]], courseId = stage, kind = "Jury", value = 120m }).Ok();
        var sheet = await teacher.GetAsync($"{D}/feuille?coursId={stage}&nature=Jury").Ok();
        Assert.True(sheet.GetProperty("anonymous").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"{D}/feuille?coursId={setup.S1Course}&nature=S1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"{D}/resultats?promotionId={setup.PromotionId}&periode=Annuel")).StatusCode);
        var history = (await admin.GetAsync($"/api/scolarite/cours/{stage}/attributions").Ok()).EnumerateArray();
        Assert.Empty(history);

        // Retrait du privilège : la cote encodée reste, l'enseignant ne peut plus encoder.
        await admin.Put($"/api/utilisateurs/{userId}", new { displayName = "Pr KAPEND", role = "Enseignant", isActive = true, staffMemberId = staffId, juryAccess = false }).Ok();
        teacher = await app.Login("prof.kapend", "Provisoire1");
        var (withdrawn, _) = await teacher.Post($"{D}/cotes-par-code", new { code = codes[setup.Enrollments[1]], courseId = stage, kind = "Jury", value = 100m }).Fail();
        Assert.Equal(HttpStatusCode.Forbidden, withdrawn);
        var kept = await admin.GetAsync($"{D}/feuille?coursId={stage}&nature=Jury").Ok();
        Assert.Equal(120m, kept.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("enrollmentId").GetString() == setup.Enrollments[0]).GetProperty("value").GetDecimal());
    }

    [Fact]
    public void Live_feed_hides_payments_and_codes_from_teacher_accounts()
    {
        var teacher = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim(Scolia.Api.Security.ScoliaClaims.Permission, "cotes.encoder") }, "test"));
        Assert.True(Scolia.Api.Endpoints.LiveEndpoints.Restricted(teacher));
        var payment = new Scolia.Infrastructure.Live.LiveEvent("donnees", 5, "pc", "PC-SECRETARIAT", "Caissier", "Reçu R-1 : 50 000 FC pour MUKENDI.", ["Recu"], DateTime.UtcNow);
        var hidden = Scolia.Api.Endpoints.LiveEndpoints.ForRestricted(payment)!;
        Assert.Null(hidden.Summary);
        Assert.Null(hidden.User);
        Assert.Empty(hidden.Topics);
        var opened = payment with { Summary = "Délibération : encodage S1 ouvert.", Topics = ["ReglagesDeliberation"] };
        Assert.Equal(opened.Summary, Scolia.Api.Endpoints.LiveEndpoints.ForRestricted(opened)!.Summary);
        Assert.Null(Scolia.Api.Endpoints.LiveEndpoints.ForRestricted(payment with { Kind = "presence" }));
    }

    [Fact]
    public async Task Itmcd_marks_are_imported_by_code_and_official_bulletins_are_verifiable()
    {
        var admin = await app.InstalledAdmin();
        var setup = await SetupAsync(admin, "MUKENDI", "KASONGO", "ILUNGA");
        await admin.Post($"{D}/codes/attribution", new { promotionId = setup.PromotionId }).Ok();
        var codes = await CodesAsync(admin, setup.PromotionId);
        var (a, b, c) = (setup.Enrollments[0], setup.Enrollments[1], setup.Enrollments[2]);
        await admin.Put($"{D}/feuille?coursId={setup.S1Course}&nature=S1", new[] { new { enrollmentId = c, value = (decimal?)50m } }).Ok();
        var matricule = (await admin.GetAsync($"{D}/codes?promotionId={setup.PromotionId}").Ok()).EnumerateArray()
            .Single(r => r.GetProperty("enrollmentId").GetString() == c).GetProperty("matricule").GetString();

        // Export d'ITMCD : code ; cote (virgule décimale), en-tête, une cote hors maxima, un code inconnu, un doublon.
        var text = $"Code;Cote\n{codes[a].ToLowerInvariant()};72,5\n{codes[b]};101\n{matricule};55\nZ99Z9Z;10\n{codes[a]};70\n";
        var preview = await admin.Post($"{D}/cotes/import", new { courseId = setup.S1Course, kind = "S1", text, apply = false }).Ok();
        var statuses = preview.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("status").GetString()!).ToArray();
        Assert.Equal(["À enregistrer", "Cote invalide", "Remplace", "Code ou matricule inconnu", "Doublon dans la liste"], statuses);
        Assert.Equal(2, preview.GetProperty("ready").GetInt32());
        var applied = await admin.Post($"{D}/cotes/import", new { courseId = setup.S1Course, kind = "S1", text, apply = true }).Ok();
        Assert.Equal(2, applied.GetProperty("saved").GetInt32());
        var sheet = await admin.GetAsync($"{D}/feuille?coursId={setup.S1Course}&nature=S1").Ok();
        decimal? Value(string id) => sheet.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("enrollmentId").GetString() == id)
            .GetProperty("value") is { ValueKind: JsonValueKind.Number } v ? v.GetDecimal() : null;
        Assert.Equal(72.5m, Value(a));
        Assert.Null(Value(b));
        Assert.Equal(55m, Value(c));

        // Bulletin officiel après clôture : code de vérification public, nom masqué ; invalide après réouverture.
        await admin.Put($"{D}/feuille?coursId={setup.S1Course}&nature=S1", new[] { new { enrollmentId = b, value = (decimal?)40m } }).Ok();
        foreach (var e in setup.Enrollments)
            await admin.Put($"{D}/feuille?coursId={setup.AnnualCourse}&nature=S1", new[] { new { enrollmentId = e, value = (decimal?)30m } }).Ok();
        await admin.Post($"{D}/cloture", new { promotionId = setup.PromotionId, period = "S1", password = "Scolia2026!", president = "Dr MUKENGE", members = "", notes = "" }).Ok();
        string code;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Scolia.Infrastructure.Persistence.ScoliaDbContext>();
            code = (await db.DeliberationResults.SingleAsync(r => r.EnrollmentId == Guid.Parse(a))).VerificationCode;
        }
        Assert.Equal(12, code.Length);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{D}/bulletin.pdf?promotionId={setup.PromotionId}&periode=S1&inscriptionId={a}")).StatusCode);
        var anonymous = app.Client();
        var verified = await anonymous.GetAsync($"/api/verifier-bulletin/{code}").Ok();
        Assert.True(verified.GetProperty("found").GetBoolean());
        Assert.Equal("MUKENDI K. G.", verified.GetProperty("student").GetString());
        Assert.Equal("Premier semestre", verified.GetProperty("period").GetString());
        Assert.False((await anonymous.GetAsync("/api/verifier-bulletin/ZZZZZZZZZZZZ").Ok()).GetProperty("found").GetBoolean());
        await admin.Post($"{D}/reouverture", new { promotionId = setup.PromotionId, period = "S1", password = "Scolia2026!", reason = "Erreur de report sur une copie" }).Ok();
        Assert.False((await anonymous.GetAsync($"/api/verifier-bulletin/{code}").Ok()).GetProperty("found").GetBoolean());
    }

    [Fact]
    public async Task A_promotion_with_courses_is_kept_and_code_previews_write_nothing()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await admin.Post("/api/etablissement/promotions", new
        {
            code = "INF3", name = "Troisième année", level = 3, isActive = true, monthlyFee = 55000m, oneTimeFees = Array.Empty<object>(),
        }).Ok();
        var promotionId = promotion.GetProperty("id").GetString()!;
        await admin.Post("/api/scolarite/cours", new { promotionId, number = 1, name = "Pharmacologie", coefficient = 2m, hours = 30m, maxScore = 20m, semester = "S1" }).Ok();
        // Auparavant une erreur interne (clé étrangère) ; désormais un refus clair.
        var (status, body) = await admin.DeleteAsync($"/api/etablissement/promotions/{promotionId}").Fail();
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("promotion", body.GetProperty("code").GetString());

        var setup = await SetupAsync(admin, "MUKENDI");
        var preview = await admin.Post($"{D}/codes/import", new { text = "MUKENDI Kabeya Grace;P13W5R", promotionId = setup.PromotionId, apply = false }).Ok();
        Assert.Equal(1, preview.GetProperty("ready").GetInt32());
        Assert.Null((await CodesAsync(admin, setup.PromotionId))[setup.Enrollments[0]]);
    }

    [Fact]
    public async Task Teacher_parameters_govern_updates_and_downloads()
    {
        var admin = await app.InstalledAdmin();
        var setup = await SetupAsync(admin, "MUKENDI", "KASONGO");
        await admin.Post($"{D}/codes/attribution", new { promotionId = setup.PromotionId }).Ok();
        var codes = await CodesAsync(admin, setup.PromotionId);
        var staff = await admin.Post("/api/personnel", new
        {
            lastName = "NGOIE", middleName = "", firstName = "Marc", gender = "M", function = "Enseignant", phone = "0812345678", email = (string?)null,
            hireDate = new DateOnly(2020, 1, 1), exitDate = (DateOnly?)null, baseSalary = 300000m, isActive = true,
        }).Ok();
        var staffId = staff.GetProperty("id").GetString()!;
        await admin.Put($"/api/scolarite/cours/{setup.S1Course}/attribution", new { teacherId = staffId, confirmReplace = false }).Ok();
        await admin.Post("/api/utilisateurs", new { userName = "prof.ngoie", displayName = "Pr NGOIE", role = "Enseignant", password = "Provisoire1", staffMemberId = staffId }).Ok();
        var teacher = await app.Login("prof.ngoie", "Provisoire1");

        // L'enseignant lit les règles en vigueur : mise à jour fermée, téléchargements ouverts (projet Délibération).
        var rules = await teacher.GetAsync($"{D}/reglages").Ok();
        Assert.False(rules.GetProperty("teacherUpdates").GetBoolean());
        Assert.True(rules.GetProperty("teacherDownloads").GetBoolean());

        var a = codes[setup.Enrollments[0]];
        await teacher.Post($"{D}/cotes-par-code", new { code = a, courseId = setup.S1Course, kind = "S1", value = 50m }).Ok();
        var (closed, _) = await teacher.Post($"{D}/cotes-par-code", new { code = a, courseId = setup.S1Course, kind = "S1", value = 60m }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, closed);
        var (cleared, _) = await teacher.Put($"{D}/feuille?coursId={setup.S1Course}&nature=S1", new[] { new { enrollmentId = setup.Enrollments[0], value = (decimal?)null } }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, cleared);
        var preview = await teacher.Post($"{D}/cotes/import", new { courseId = setup.S1Course, kind = "S1", text = $"{a};70", apply = false }).Ok();
        Assert.Equal("Déjà encodée (mise à jour fermée)", preview.GetProperty("lines")[0].GetProperty("status").GetString());
        // Le secrétariat corrige toujours.
        await admin.Post($"{D}/cotes-par-code", new { code = a, courseId = setup.S1Course, kind = "S1", value = 55m }).Ok();

        // Mise à jour ouverte, téléchargements fermés.
        await admin.Put($"{D}/reglages", new { encodingS1Open = true, encodingS2Open = true, supportEnabled = true, passPercent = 60m, teacherUpdates = true, teacherDownloads = false }).Ok();
        await teacher.Post($"{D}/cotes-par-code", new { code = a, courseId = setup.S1Course, kind = "S1", value = 60m }).Ok();
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"{D}/feuille/export?coursId={setup.S1Course}&nature=S1&format=xlsx")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{D}/feuille/export?coursId={setup.S1Course}&nature=S1&format=xlsx")).StatusCode);
        var saved = await admin.GetAsync($"{D}/reglages").Ok();
        Assert.True(saved.GetProperty("teacherUpdates").GetBoolean());
        Assert.False(saved.GetProperty("teacherDownloads").GetBoolean());
    }

    [Fact]
    public async Task A_closed_jury_keeps_its_official_roster_after_a_student_leaves()
    {
        var admin = await app.InstalledAdmin();
        var setup = await SetupAsync(admin, "MUKENDI", "KASONGO");
        var learner = setup.Enrollments[0];
        await admin.Put($"{D}/feuille?coursId={setup.S1Course}&nature=S1", new[]
        {
            new { enrollmentId = learner, value = (decimal?)80m },
        }).Ok();
        await admin.Post($"{D}/cloture", new
        {
            promotionId = setup.PromotionId, period = "S1", password = "Scolia2026!",
            president = "Dr MUKENGE", members = "", notes = "",
        }).Ok();
        var before = await admin.GetAsync($"{D}/resultats?promotionId={setup.PromotionId}&periode=S1").Ok();
        Assert.Equal(2, before.GetProperty("students").GetInt32());

        // Le dossier peut devenir diplômé sans perdre sa place dans le procès-verbal signé.
        var status = await admin.PutAsync($"/api/inscriptions/{learner}/statut",
            JsonContent.Create(new { status = "Diplômé", reason = "Fin de cursus après jury" }));
        Assert.Equal(HttpStatusCode.NoContent, status.StatusCode);
        var after = await admin.GetAsync($"{D}/resultats?promotionId={setup.PromotionId}&periode=S1").Ok();
        Assert.Equal("Clôturée", after.GetProperty("status").GetString());
        Assert.Equal(2, after.GetProperty("students").GetInt32());
        Assert.Equal(Row(before, learner).GetProperty("percent").GetDecimal(), Row(after, learner).GetProperty("percent").GetDecimal());
        Assert.Equal(Row(before, learner).GetProperty("decision").GetString(), Row(after, learner).GetProperty("decision").GetString());
        Assert.Equal(HttpStatusCode.OK,
            (await admin.GetAsync($"{D}/bulletin.pdf?promotionId={setup.PromotionId}&periode=S1&inscriptionId={learner}")).StatusCode);
    }

    [Fact]
    public async Task A_closed_jury_protects_course_definitions_archives_and_academic_rules()
    {
        var admin = await app.InstalledAdmin();
        var setup = await SetupAsync(admin, "MUKENDI");
        await admin.Post($"{D}/cloture", new
        {
            promotionId = setup.PromotionId, period = "S1", password = "Scolia2026!",
            president = "Dr MUKENGE", members = "", notes = "",
        }).Ok();

        var (ruleStatus, _) = await admin.Put($"{D}/reglages", new
        {
            encodingS1Open = true, encodingS2Open = true, supportEnabled = true, passPercent = 65m,
            teacherUpdates = false, teacherDownloads = true,
        }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, ruleStatus);

        var (editedStatus, _) = await admin.Put($"/api/scolarite/cours/{setup.S1Course}", new
        {
            promotionId = setup.PromotionId, number = 1, name = "Anatomie modifiée",
            coefficient = 1m, hours = 30m, maxScore = 110m, semester = "S1",
        }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, editedStatus);

        var (archiveStatus, _) = await admin.Post($"/api/scolarite/cours/{setup.S1Course}/archivage", new { }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, archiveStatus);

        var (newS1Status, _) = await admin.Post("/api/scolarite/cours", new
        {
            promotionId = setup.PromotionId, number = 15, name = "Biologie nouvelle",
            coefficient = 1m, hours = 30m, maxScore = 20m, semester = "S1",
        }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, newS1Status);

        // Une épreuve S2 n'altère pas le procès-verbal du S1 clôturé.
        await admin.Post("/api/scolarite/cours", new
        {
            promotionId = setup.PromotionId, number = 16, name = "Biologie S2",
            coefficient = 1m, hours = 30m, maxScore = 20m, semester = "S2",
        }).Ok();
    }

    [Fact]
    public async Task Frozen_jury_results_prevent_deletion_even_without_grades()
    {
        var admin = await app.InstalledAdmin();
        var setup = await SetupAsync(admin, "MUKENDI");
        await admin.Post($"{D}/cloture", new
        {
            promotionId = setup.PromotionId, period = "S1", password = "Scolia2026!",
            president = "Dr MUKENGE", members = "", notes = "",
        }).Ok();

        Guid studentId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Scolia.Infrastructure.Persistence.ScoliaDbContext>();
            studentId = await db.Enrollments.Where(e => e.Id == Guid.Parse(setup.Enrollments[0]))
                .Select(e => e.StudentId).SingleAsync();
        }
        var (status, error) = await admin.DeleteAsync($"/api/apprenants/{studentId}").Fail();
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("apprenant", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Historic_anonymous_code_is_never_reassigned_even_to_its_previous_owner()
    {
        var admin = await app.InstalledAdmin();
        var setup = await SetupAsync(admin, "MUKENDI");
        await admin.Post($"{D}/codes/attribution", new { promotionId = setup.PromotionId }).Ok();
        var former = (await CodesAsync(admin, setup.PromotionId))[setup.Enrollments[0]];
        await admin.Put($"{D}/codes/{setup.Enrollments[0]}",
            new { code = "ITM-26-AB12", reason = "Reprise du code officiel" }).Ok();

        var preview = await admin.Post($"{D}/codes/import", new
        {
            promotionId = setup.PromotionId, text = $"MUKENDI Kabeya Grace;{former}", apply = false,
        }).Ok();
        Assert.Equal("Code pris", preview.GetProperty("lines")[0].GetProperty("status").GetString());
        Assert.Equal(0, preview.GetProperty("ready").GetInt32());
    }
}
