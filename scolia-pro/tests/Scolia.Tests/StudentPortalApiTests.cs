using System.Net;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Scolia.Tests;

/// <summary>
/// Accès étudiants d'une université : créés par le secrétariat, code provisoire à remplacer,
/// consultation des seuls résultats publiés de l'étudiant, et aucune autre donnée de l'API.
/// </summary>
public sealed class StudentPortalApiTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    private sealed record University(HttpClient Admin, Guid Promotion, Guid S1Element, Guid S2Element, Guid[] Enrollments, Guid[] Students);

    private async Task<University> BuildAsync()
    {
        var admin = app.Client();
        await admin.Post("/api/installation", new
        {
            institutionType = "SuperieurLmd", governance = "Prive", schoolName = "Université de test", shortName = "UT",
            phone = "0810000000", city = "Lubumbashi", currencyCode = "USD", currencySymbol = "$",
            yearLabel = Http.Year(DateOnly.FromDateTime(DateTime.Today)), startMonth = 10, monthCount = 10,
            adminUserName = "admin", adminDisplayName = "Secrétariat", adminPassword = "Scolia2026!", loadDemoData = false,
        }).Ok();
        async Task<Guid> Unit(string kind, Guid? parent, string code) =>
            (await admin.Post("/api/etablissement/lmd/structures", new { kind, parentId = parent, code, name = code }).Ok()).GetProperty("id").GetGuid();
        var filiere = await Unit("Filiere", await Unit("Department", await Unit("Faculty", null, "FS"), "INFO"), "GL");
        var promotion = (await admin.Post("/api/etablissement/promotions", new
        {
            code = "GLL1", name = "Génie logiciel L1", level = 1, isActive = true, nextPromotionCode = (string?)null,
            monthlyFee = 0m, oneTimeFees = Array.Empty<object>(), academicUnitId = filiere,
        }).Ok()).GetProperty("id").GetGuid();
        async Task<Guid> Ue(string semester, string code) => (await admin.Post($"/api/etablissement/lmd/promotions/{promotion}/ues", new
        {
            academicUnitId = filiere, semester, code, name = code, credits = 30m, hours = 300m,
            elementCode = code + "A", elementName = "Élément " + code, elementHours = 60m,
        }).Ok())[1].GetProperty("id").GetGuid();
        var s1 = await Ue("S1", "MAT101");
        var s2 = await Ue("S2", "INF102");
        var enrollments = new Guid[2];
        var students = new Guid[2];
        foreach (var (i, last) in new[] { (0, "MUKENDI"), (1, "ILUNGA") })
        {
            var s = await admin.Post("/api/apprenants", new
            {
                identity = new { lastName = last, middleName = "TEST", firstName = "Etudiant", gender = "F", birthDate = new DateOnly(2004, 3, 14),
                    birthPlace = "Lubumbashi", guardianPhone = "0812345678" },
                promotionId = promotion, confirmDuplicate = true,
            }).Ok();
            students[i] = s.GetProperty("id").GetGuid();
            enrollments[i] = s.GetProperty("enrollments")[0].GetProperty("id").GetGuid();
        }
        await admin.Post("/api/etablissement/reglements/SuperieurLmd/validation", new { }).Ok();
        return new University(admin, promotion, s1, s2, enrollments, students);
    }

    private async Task<HttpClient> StudentAsync(string userName, string code)
    {
        var client = await app.Login(userName, code);
        await client.Post("/api/session/mot-de-passe", new { currentPassword = code, newPassword = "Etudiant2026" }).Ok();
        return await app.Login(userName, "Etudiant2026");
    }

    [Fact]
    public async Task Student_reads_only_own_published_results_after_replacing_the_provisional_code()
    {
        var u = await BuildAsync();
        var credentials = (await u.Admin.Post($"/api/comptes-etudiants/promotions/{u.Promotion}", new { }).Ok()).EnumerateArray().ToList();
        Assert.Equal(2, credentials.Count);
        var mine = credentials.Single(c => c.GetProperty("studentId").GetGuid() == u.Students[0]);
        var userName = mine.GetProperty("userName").GetString()!;
        var code = mine.GetProperty("provisionalCode").GetString()!;
        var (again, _) = await u.Admin.Post($"/api/comptes-etudiants/{u.Students[0]}", new { }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, again);
        var list = await u.Admin.GetAsync("/api/comptes-etudiants").Ok();
        Assert.DoesNotContain(code, list.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(userName, (await u.Admin.GetAsync("/api/utilisateurs").Ok()).GetRawText(), StringComparison.Ordinal);

        // Code provisoire : connexion possible, consultation refusée.
        var provisional = await app.Login(userName, code);
        var (blocked, _) = await provisional.GetAsync("/api/etudiant/mes-resultats").Fail();
        Assert.Equal(HttpStatusCode.Forbidden, blocked);
        var student = await StudentAsync(userName, code);
        var none = await student.GetAsync("/api/etudiant/mes-resultats").Ok();
        Assert.Empty(none.GetProperty("results").EnumerateArray());

        // Notes du S1 saisies mais non publiées : toujours rien.
        await u.Admin.Put($"/api/etablissement/lmd/promotions/{u.Promotion}/ecs/{u.S1Element}/notes", new
        {
            session = 1, lines = new[] { new { enrollmentId = u.Enrollments[0], score = (decimal?)14m }, new { enrollmentId = u.Enrollments[1], score = (decimal?)9m } },
        }).Ok();
        Assert.Empty((await student.GetAsync("/api/etudiant/mes-resultats").Ok()).GetProperty("results").EnumerateArray());

        await u.Admin.Post($"/api/etablissement/lmd/promotions/{u.Promotion}/publication-s1", new { }).Ok();
        var published = await student.GetAsync("/api/etudiant/mes-resultats").Ok();
        var result = Assert.Single(published.GetProperty("results").EnumerateArray());
        Assert.Equal("S1", result.GetProperty("stage").GetString());
        Assert.Equal(14m, result.GetProperty("result").GetProperty("s1").GetProperty("average").GetDecimal());
        Assert.Equal(2, result.GetProperty("headcount").GetInt32());
        Assert.DoesNotContain("ILUNGA", published.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(u.Enrollments[1].ToString(), published.GetRawText(), StringComparison.Ordinal);
        // Le S2 n'apparaît pas tant que la première session n'est pas publiée.
        Assert.DoesNotContain("INF102", published.GetRawText(), StringComparison.Ordinal);

        var pdf = await student.GetAsync($"/api/etudiant/releve.pdf?inscription={u.Enrollments[0]}");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        var (otherPdf, _) = await student.GetAsync($"/api/etudiant/releve.pdf?inscription={u.Enrollments[1]}").Fail();
        Assert.Equal(HttpStatusCode.NotFound, otherPdf);

        // Désactivé : la session ouverte cesse de fonctionner.
        var account = mine.GetProperty("userId").GetGuid();
        await u.Admin.Put($"/api/comptes-etudiants/acces/{account}/statut", new { active = false }).Ok();
        var (disabled, _) = await student.GetAsync("/api/etudiant/mes-resultats").Fail();
        Assert.True(disabled is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Student_account_reaches_no_other_read_route_of_the_api()
    {
        var u = await BuildAsync();
        var issued = await u.Admin.Post($"/api/comptes-etudiants/{u.Students[0]}", new { }).Ok();
        var student = await StudentAsync(issued.GetProperty("userName").GetString()!, issued.GetProperty("provisionalCode").GetString()!);

        // Seules routes admises : la session, la santé du serveur, la vérification publique d'un document.
        string[] allowed = ["/api/etudiant/", "/api/session", "/api/sante", "/api/verifier/", "/api/verifier-bulletin/"];
        var sources = app.Services.GetRequiredService<EndpointDataSource>();
        var leaks = new List<string>();
        foreach (var endpoint in sources.Endpoints.OfType<RouteEndpoint>())
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            if (!methods.Contains("GET")) continue;
            var template = "/" + endpoint.RoutePattern.RawText!.TrimStart('/');
            if (!template.StartsWith("/api", StringComparison.Ordinal)) continue;
            var url = System.Text.RegularExpressions.Regex.Replace(template, @"\{[^}]+\}", m =>
                m.Value.Contains(":guid", StringComparison.Ordinal) ? u.Enrollments[0].ToString() : "1");
            // Statut seulement : une route de flux continu ne se termine jamais.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await student.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if ((int)response.StatusCode is >= 200 and < 300 && !allowed.Any(a => url.StartsWith(a, StringComparison.Ordinal)))
                leaks.Add($"{(int)response.StatusCode} {url}");
        }
        Assert.True(leaks.Count == 0, "Routes lisibles par un étudiant : " + string.Join(", ", leaks));
    }
}
