using System.Net;
using System.Text.Json;

namespace Scolia.Tests;

/// <summary>
/// Comptes enseignants créés par le secrétariat (gestion des délibérations) : identifiant proposé, code provisoire
/// généré et montré une seule fois, remplacement obligatoire avant d'encoder, privilège jury et désactivation ;
/// et cloisonnement complet d'un compte enseignant sur le reste de SCOLIA.
/// </summary>
public sealed class TeacherAccountsApiTests : IDisposable
{
    private readonly ScoliaApp app = new();
    private const string T = "/api/comptes-enseignants";

    public void Dispose() => app.Dispose();

    [Fact]
    public async Task Secretary_creates_teacher_with_provisional_credentials_that_must_be_replaced()
    {
        var admin = await app.InstalledAdmin();

        // Nouvel enseignant en une étape : fiche du personnel et compte, identifiant « prénom.nom » sans accents.
        var created = await admin.Post($"{T}/nouvel-enseignant", new
        {
            lastName = "Kalombo", middleName = "Ilunga", firstName = "Jérôme", gender = "M", phone = "0812345678", email = (string?)null,
            createAccount = true, juryAccess = false,
        }).Ok();
        var account = created.GetProperty("account");
        Assert.Equal("jerome.kalombo", account.GetProperty("userName").GetString());
        var code = account.GetProperty("provisionalCode").GetString()!;
        Assert.Matches("^[a-z]{4}-[2-9]{4}$", code);
        var userId = account.GetProperty("userId").GetString()!;
        var staffId = created.GetProperty("staffMemberId").GetString()!;

        // Le code n'apparaît jamais dans la liste ; l'état du compte, si.
        var list = await admin.GetAsync(T).Ok();
        var row = list.EnumerateArray().Single(r => r.GetProperty("staffMemberId").GetString() == staffId);
        Assert.True(row.GetProperty("hasAccount").GetBoolean());
        Assert.True(row.GetProperty("mustChangePassword").GetBoolean());
        Assert.DoesNotContain(code, list.GetRawText(), StringComparison.Ordinal);

        // Un second compte pour la même fiche est refusé.
        var (twice, _) = await admin.Post(T, new { staffMemberId = staffId }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, twice);

        // Avec le code provisoire, l'enseignant se connecte mais n'ouvre pas ses cours : il doit d'abord choisir son code.
        var teacher = await app.Login("jerome.kalombo", code);
        var (provisional, body) = await teacher.GetAsync("/api/deliberation/mes-cours").Fail();
        Assert.Equal(HttpStatusCode.Forbidden, provisional);
        Assert.Contains("provisoire", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        await teacher.Post("/api/session/mot-de-passe", new { currentPassword = code, newPassword = "Kalombo2026" }).Ok();
        Assert.Empty((await teacher.GetAsync("/api/deliberation/mes-cours").Ok()).EnumerateArray());

        // Code oublié : nouveau code provisoire ; l'ancien code et l'ancienne session ne servent plus.
        var reset = await admin.Post($"{T}/{userId}/code-provisoire", new { }).Ok();
        var second = reset.GetProperty("provisionalCode").GetString()!;
        Assert.NotEqual(code, second);
        Assert.Equal(HttpStatusCode.Unauthorized, (await teacher.GetAsync("/api/deliberation/mes-cours")).StatusCode);
        var (old, _) = await app.Client().Post("/api/session", new { userName = "jerome.kalombo", password = "Kalombo2026" }).Fail();
        Assert.NotEqual(HttpStatusCode.OK, old);
        await app.Login("jerome.kalombo", second);

        // Privilège jury et désactivation depuis la même page ; la réactivation donne un nouveau code.
        var jury = await admin.Put($"{T}/{userId}", new { isActive = true, juryAccess = true }).Ok();
        Assert.True(jury.GetProperty("juryAccess").GetBoolean());
        var off = await admin.Put($"{T}/{userId}", new { isActive = false, juryAccess = false }).Ok();
        Assert.False(off.GetProperty("accountActive").GetBoolean());
        var (locked, _) = await app.Client().Post("/api/session", new { userName = "jerome.kalombo", password = second }).Fail();
        Assert.NotEqual(HttpStatusCode.OK, locked);
        var again = await admin.Post(T, new { staffMemberId = staffId }).Ok();
        Assert.Equal(userId, again.GetProperty("userId").GetString());
        Assert.Equal("jerome.kalombo", again.GetProperty("userName").GetString());

        // Homonyme : l'identifiant proposé reste unique.
        var twin = await admin.Post($"{T}/nouvel-enseignant", new { lastName = "KALOMBO", middleName = "", firstName = "Jerome", gender = "M", createAccount = true }).Ok();
        Assert.Equal("jerome.kalombo2", twin.GetProperty("account").GetProperty("userName").GetString());

        // Les autres comptes ne se gèrent pas ici.
        var users = await admin.GetAsync("/api/utilisateurs").Ok();
        var secretary = users.EnumerateArray().First(u => u.GetProperty("role").GetString() == "Secretariat").GetProperty("id").GetString();
        var (notTeacher, _) = await admin.Post($"{T}/{secretary}/code-provisoire", new { }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, notTeacher);
    }

    [Fact]
    public async Task Teacher_account_sees_nothing_outside_its_own_courses()
    {
        var admin = await app.InstalledAdmin();
        var created = await admin.Post($"{T}/nouvel-enseignant", new { lastName = "MWAMBA", middleName = "", firstName = "Rose", gender = "F", createAccount = true }).Ok();
        var account = created.GetProperty("account");
        var teacher = await app.FirstLogin(account.GetProperty("userName").GetString()!, account.GetProperty("provisionalCode").GetString()!, "Mwamba2026");

        string[] forbidden =
        [
            "/api/apprenants", "/api/inscriptions/00000000-0000-0000-0000-000000000000/compte", "/api/paiements", "/api/depenses", "/api/caisse/situation",
            "/api/rapports/aujourdhui", "/api/cartes/lots", "/api/scolarite/cours", "/api/scolarite/discipline", "/api/prises-en-charge", "/api/personnel", "/api/utilisateurs", T,
            "/api/audit", "/api/sauvegardes", "/api/recherche?q=a", "/api/pilotage/synthese", "/api/enseignants",
            "/api/deliberation/cours", "/api/deliberation/codes", "/api/deliberation/suivi", "/api/deliberation/autorites",
        ];
        foreach (var url in forbidden)
        {
            var status = (await teacher.GetAsync(url)).StatusCode;
            Assert.True(status is HttpStatusCode.Forbidden, $"{url} : {status}");
        }
        var (create, _) = await teacher.Post($"{T}/nouvel-enseignant", new { lastName = "X", firstName = "Y", gender = "M" }).Fail();
        Assert.Equal(HttpStatusCode.Forbidden, create);

        // Ce qui reste ouvert à l'enseignant : sa session, ses cours, ses annonces et les règles de l'encodage.
        var session = await teacher.GetAsync("/api/session").Ok();
        Assert.Equal(new[] { "cotes.encoder" }, session.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToArray());
        Assert.Equal(JsonValueKind.Array, (await teacher.GetAsync("/api/deliberation/mes-cours").Ok()).ValueKind);
        Assert.Equal(JsonValueKind.Array, (await teacher.GetAsync("/api/deliberation/annonces").Ok()).ValueKind);
        Assert.Equal(JsonValueKind.Object, (await teacher.GetAsync("/api/deliberation/reglages").Ok()).ValueKind);
    }
}
