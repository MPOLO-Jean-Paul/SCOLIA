using System.Net;
using Scolia.Domain.Common;
using Scolia.Domain.Students;

namespace Scolia.Tests;

/// <summary>Règles des cartes d'accès : format A4, pagination, exemplaires, session, couleurs et exclusions.</summary>
public sealed class StudentCardRulesTests
{
    [Fact]
    public void Eight_cards_fit_per_page_and_the_grid_stays_on_the_sheet()
    {
        Assert.Equal(0, StudentCardRules.PagesFor(0));
        Assert.Equal(1, StudentCardRules.PagesFor(8));
        Assert.Equal(2, StudentCardRules.PagesFor(9));
        Assert.Equal(126, StudentCardRules.PagesFor(1001));
        Assert.True(StudentCardRules.FitsOnPage());
    }

    [Fact]
    public void Copies_go_from_one_to_five()
    {
        Assert.Throws<DomainException>(() => StudentCardRules.Copies(0));
        Assert.Throws<DomainException>(() => StudentCardRules.Copies(6));
        Assert.Equal(5, StudentCardRules.Copies(5));
    }

    [Fact]
    public void Session_is_written_in_capitals_with_the_month_and_year()
    {
        Assert.Equal("SEPTEMBRE 2026", StudentCardRules.SessionLabel(9, 2026));
        Assert.Throws<DomainException>(() => StudentCardRules.SessionLabel(13, 2026));
    }

    [Fact]
    public void Promotion_colours_match_the_v79_band_and_others_are_neutral()
    {
        Assert.Equal(StudentCardRules.ColorOf("TLM1"), StudentCardRules.ColorOf("TML1"));
        Assert.Equal("#FACC15", StudentCardRules.ColorOf("ACC1"));
        Assert.Equal("#CBD5E1", StudentCardRules.ColorOf("XYZ9"));
    }

    [Fact]
    public void Only_active_students_are_eligible_unless_left_ones_are_requested()
    {
        Assert.True(StudentCardRules.Eligible(EnrollmentStatus.Actif, includeLeft: false));
        Assert.False(StudentCardRules.Eligible(EnrollmentStatus.Suspendu, includeLeft: false));
        Assert.False(StudentCardRules.Eligible(EnrollmentStatus.Sorti, includeLeft: false));
        Assert.True(StudentCardRules.Eligible(EnrollmentStatus.Sorti, includeLeft: true));
    }

    [Fact]
    public void A_name_too_long_for_the_card_is_refused_not_truncated()
    {
        Assert.Throws<DomainException>(() => StudentCardRules.EnsureFits(new string('A', 61), "nom"));
        StudentCardRules.EnsureFits(new string('A', 60), "nom");
    }
}

/// <summary>Cartes par l'API : sélection, exclusion des sortis, écartement signalé et pagination.</summary>
public sealed class StudentCardsApiTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    private static async Task<string> PromotionAsync(HttpClient admin)
    {
        var promotion = await admin.Post("/api/etablissement/promotions", new
        {
            code = "INF1", name = "Première année", level = 1, isActive = true, monthlyFee = 55000m, oneTimeFees = Array.Empty<object>(),
        }).Ok();
        return promotion.GetProperty("id").GetString()!;
    }

    private static async Task<string> StudentAsync(HttpClient admin, string promotionId, string lastName)
    {
        var student = await admin.Post("/api/apprenants", new
        {
            identity = new { lastName, middleName = "Kabeya", firstName = "Grace", birthPlace = "Lubumbashi", birthDate = new DateOnly(2007, 3, 14), gender = "F", guardianPhone = "0812345678", guardianName = "Parent" },
            promotionId, confirmDuplicate = false,
        }).Ok();
        return student.GetProperty("enrollments")[0].GetProperty("id").GetString()!;
    }

    private static async Task<string> LeaveAsync(HttpClient admin, string enrollmentId)
    {
        await admin.Post("/api/scolarite/discipline/registre", new
        {
            enrollmentId, measure = "RenvoiDefinitif", reason = "Faute grave", decisionDate = DateOnly.FromDateTime(DateTime.Today), confirmDefinitive = true,
        }).Ok();
        return enrollmentId;
    }

    [Fact]
    public async Task Left_students_are_not_eligible_by_default_and_appear_only_when_requested()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await PromotionAsync(admin);
        var active = await StudentAsync(admin, promotion, "MUKENDI");
        var left = await LeaveAsync(admin, await StudentAsync(admin, promotion, "KABEYA"));

        var defaults = (await admin.GetAsync($"/api/cartes/candidats?promotionId={promotion}").Ok()).EnumerateArray().ToList();
        Assert.True(defaults.Single(c => c.GetProperty("enrollmentId").GetString() == active).GetProperty("eligible").GetBoolean());
        var sortie = defaults.Single(c => c.GetProperty("enrollmentId").GetString() == left);
        Assert.False(sortie.GetProperty("eligible").GetBoolean());
        Assert.Equal("Sorti", sortie.GetProperty("status").GetString());

        var all = (await admin.GetAsync($"/api/cartes/candidats?promotionId={promotion}&sortis=true").Ok()).EnumerateArray().ToList();
        Assert.True(all.Single(c => c.GetProperty("enrollmentId").GetString() == left).GetProperty("eligible").GetBoolean());
    }

    [Fact]
    public async Task Export_returns_a_pdf_and_records_the_batch()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await PromotionAsync(admin);
        var student = await StudentAsync(admin, promotion, "MUKENDI");

        var response = await admin.Post("/api/cartes/export", new { enrollmentIds = new[] { student }, month = 9, year = 2026, copies = 2, includeLeft = false });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(await response.Content.ReadAsByteArrayAsync())[..4], StringComparison.Ordinal);
        Assert.Equal("0", response.Headers.GetValues("X-Cartes-Ecartees").Single());

        var batch = (await admin.GetAsync("/api/cartes/lots").Ok()).EnumerateArray().Single();
        Assert.Equal("SEPTEMBRE 2026", batch.GetProperty("session").GetString());
        Assert.Equal(2, batch.GetProperty("cardCount").GetInt32());
        Assert.Equal(1, batch.GetProperty("pageCount").GetInt32());
        Assert.Equal("PDF", batch.GetProperty("format").GetString());
    }

    [Fact]
    public async Task Word_export_is_a_well_formed_package_recorded_as_word()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await PromotionAsync(admin);
        var student = await StudentAsync(admin, promotion, "MUKENDI");

        var response = await admin.Post("/api/cartes/export", new { enrollmentIds = new[] { student }, month = 9, year = 2026, copies = 3, includeLeft = false, format = "docx" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", response.Content.Headers.ContentType?.MediaType);

        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()), System.IO.Compression.ZipArchiveMode.Read);
        var document = zip.GetEntry("word/document.xml") ?? throw new Xunit.Sdk.XunitException("word/document.xml absent");
        using (var stream = document.Open())
        {
            var xml = System.Xml.Linq.XDocument.Load(stream);
            // Trois exemplaires : un sceau et un QR par carte.
            Assert.Equal(6, xml.Descendants().Count(e => e.Name.LocalName == "drawing"));
        }
        Assert.NotNull(zip.GetEntry("word/_rels/document.xml.rels"));
        Assert.Contains(zip.Entries, e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal));

        var batch = (await admin.GetAsync("/api/cartes/lots").Ok()).EnumerateArray().Single();
        Assert.Equal("Word", batch.GetProperty("format").GetString());
        Assert.Equal(3, batch.GetProperty("cardCount").GetInt32());
    }

    [Fact]
    public async Task Left_students_are_counted_as_skipped_and_ten_cards_make_two_pages()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await PromotionAsync(admin);
        var first = await StudentAsync(admin, promotion, "MUKENDI");
        var second = await StudentAsync(admin, promotion, "NGANDU");
        var left = await LeaveAsync(admin, await StudentAsync(admin, promotion, "KABEYA"));

        var mixed = await admin.Post("/api/cartes/export", new { enrollmentIds = new[] { first, left }, month = 9, year = 2026, copies = 1, includeLeft = false });
        Assert.True(mixed.IsSuccessStatusCode);
        Assert.Equal("1", mixed.Headers.GetValues("X-Cartes-Ecartees").Single());

        var onlyLeft = await admin.Post("/api/cartes/export", new { enrollmentIds = new[] { left }, month = 9, year = 2026, copies = 1, includeLeft = false }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, onlyLeft.Status);

        var ten = await admin.Post("/api/cartes/export", new { enrollmentIds = new[] { first, second }, month = 9, year = 2026, copies = 5, includeLeft = false });
        Assert.True(ten.IsSuccessStatusCode);
        var batch = (await admin.GetAsync("/api/cartes/lots").Ok()).EnumerateArray().First();
        Assert.Equal(10, batch.GetProperty("cardCount").GetInt32());
        Assert.Equal(2, batch.GetProperty("pageCount").GetInt32());
    }
}
