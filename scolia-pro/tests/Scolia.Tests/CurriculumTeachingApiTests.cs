using System.Net;

namespace Scolia.Tests;

/// <summary>Enseignants des écoles : attribution des branches, saisie limitée à ses branches, figement.</summary>
public sealed class CurriculumTeachingApiTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    private async Task<(HttpClient Teacher, Guid StaffId)> TeacherAsync(HttpClient admin, string last, string first, string user)
    {
        var created = await admin.Post("/api/comptes-enseignants/nouvel-enseignant", new
        {
            lastName = last, middleName = "Test", firstName = first, gender = "M", phone = "0812345678", email = (string?)null,
            createAccount = true, juryAccess = false,
        }).Ok();
        var code = created.GetProperty("account").GetProperty("provisionalCode").GetString()!;
        var teacher = await app.Login(user, code);
        await teacher.Post("/api/session/mot-de-passe", new { currentPassword = code, newPassword = "Enseigne2026" }).Ok();
        return (await app.Login(user, "Enseigne2026"), created.GetProperty("staffMemberId").GetGuid());
    }

    [Fact]
    public async Task Class_teacher_encodes_only_assigned_subjects_until_deliberation_is_published()
    {
        var k = await EpstClassBuilder.BuildAsync(app, [(0.8m, 0.8m), (0.6m, 0.6m)]);
        var admin = k.Admin;
        var (titular, titularId) = await TeacherAsync(admin, "Kalombo", "Jerome", "jerome.kalombo");
        var (other, otherId) = await TeacherAsync(admin, "Banza", "Paul", "paul.banza");

        // Titulaire : toutes les branches de la classe ; puis Calcul réattribué à un autre enseignant.
        Assert.Equal(2, (await admin.Put($"/api/etablissement/programme/classes/{k.ClassId}/titulaire",
            new { teacherId = titularId, confirmReplace = false }).Ok()).GetInt32());
        var (needConfirm, _) = await admin.Put($"/api/etablissement/programme/{k.MathsId}/attribution",
            new { teacherId = otherId, confirmReplace = false }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, needConfirm);
        await admin.Put($"/api/etablissement/programme/{k.MathsId}/attribution", new { teacherId = otherId, confirmReplace = true }).Ok();

        var mine = (await titular.GetAsync("/api/enseignant/mes-elements").Ok()).EnumerateArray().ToList();
        Assert.Equal(["Français"], mine.Select(m => m.GetProperty("name").GetString()!).ToArray());

        var period = (await admin.GetAsync($"/api/etablissement/cycles/{k.CycleId}/periodes").Ok())[0].GetProperty("id").GetGuid();
        var sheet = await titular.GetAsync($"/api/enseignant/branches/{k.FrenchId}/feuille?periode={period}&code=INTERRO2").Ok();
        Assert.True(sheet.GetProperty("editable").GetBoolean());
        Assert.Equal(2, sheet.GetProperty("lines").GetArrayLength());
        var saved = await titular.Put($"/api/enseignant/branches/{k.FrenchId}/feuille", new
        {
            periodId = period, evaluationCode = "interro2", title = "Dictée", maxScore = 10m,
            lines = new[] { new { enrollmentId = k.Pupils[0], score = (decimal?)9m }, new { enrollmentId = k.Pupils[1], score = (decimal?)6.5m } },
        }).Ok();
        Assert.Contains("INTERRO2", saved.GetProperty("existingCodes").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(9m, saved.GetProperty("lines").EnumerateArray().First(l => l.GetProperty("enrollmentId").GetGuid() == k.Pupils[0])
            .GetProperty("score").GetDecimal());

        // Une branche non attribuée est refusée, une cote au-delà du maximum aussi.
        var (notMine, _) = await titular.GetAsync($"/api/enseignant/branches/{k.MathsId}/feuille?periode={period}").Fail();
        Assert.Equal(HttpStatusCode.Forbidden, notMine);
        var (tooHigh, _) = await titular.Put($"/api/enseignant/branches/{k.FrenchId}/feuille", new
        {
            periodId = period, evaluationCode = "INTERRO3", title = "Lecture", maxScore = 10m,
            lines = new[] { new { enrollmentId = k.Pupils[0], score = (decimal?)12m } },
        }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, tooHigh);
        var (secretariatList, _) = await titular.GetAsync("/api/etablissement/programme/attributions").Fail();
        Assert.Equal(HttpStatusCode.Forbidden, secretariatList);

        // Après la clôture de la première session, la feuille est figée.
        await admin.Post($"/api/etablissement/cycles/promotions/{k.ClassId}/deliberation/premiere-session", new { }).Ok();
        var locked = await titular.GetAsync($"/api/enseignant/branches/{k.FrenchId}/feuille?periode={period}&code=INTERRO2").Ok();
        Assert.False(locked.GetProperty("editable").GetBoolean());
        var (frozen, _) = await titular.Put($"/api/enseignant/branches/{k.FrenchId}/feuille", new
        {
            periodId = period, evaluationCode = "INTERRO2", title = "Dictée", maxScore = 10m,
            lines = new[] { new { enrollmentId = k.Pupils[1], score = (decimal?)10m } },
        }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, frozen);

        var list = (await admin.GetAsync("/api/etablissement/programme/attributions").Ok()).EnumerateArray()
            .ToDictionary(i => i.GetProperty("code").GetString()!, i => i.GetProperty("teacherName").GetString());
        Assert.StartsWith("KALOMBO", list["FR"], StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("BANZA", list["CALC"], StringComparison.OrdinalIgnoreCase);
        _ = other;
    }

    [Fact]
    public async Task University_lecturer_encodes_marks_of_assigned_elements_only()
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
        var ue = await admin.Post($"/api/etablissement/lmd/promotions/{promotion}/ues", new
        {
            academicUnitId = filiere, semester = "S1", code = "MAT101", name = "Mathématiques", credits = 30m, hours = 300m,
            elementCode = "ANA", elementName = "Analyse", elementHours = 60m,
        }).Ok();
        var analyse = ue[1].GetProperty("id").GetGuid();
        var algebre = (await admin.Post($"/api/etablissement/lmd/promotions/{promotion}/ecs", new
        {
            parentItemId = ue[0].GetProperty("id").GetGuid(), code = "ALG", name = "Algèbre", hours = 60m,
        }).Ok()).GetProperty("id").GetGuid();
        var student = await admin.Post("/api/apprenants", new
        {
            identity = new { lastName = "MUKENDI", middleName = "KABEYA", firstName = "Grace", gender = "F", birthDate = new DateOnly(2004, 3, 14),
                birthPlace = "Lubumbashi", guardianPhone = "0812345678" },
            promotionId = promotion, confirmDuplicate = false,
        }).Ok();
        var enrollment = student.GetProperty("enrollments")[0].GetProperty("id").GetGuid();
        await admin.Post("/api/etablissement/reglements/SuperieurLmd/validation", new { }).Ok();

        var (lecturer, lecturerId) = await TeacherAsync(admin, "Ilunga", "Marc", "marc.ilunga");
        await admin.Put($"/api/etablissement/programme/{analyse}/attribution", new { teacherId = lecturerId, confirmReplace = false }).Ok();

        var route = $"/api/enseignant/lmd/promotions/{promotion}/ecs";
        await lecturer.Put($"{route}/{analyse}/notes", new
        {
            session = 1, lines = new[] { new { enrollmentId = enrollment, score = (decimal?)14.5m } },
        }).Ok();
        var sheet = await lecturer.GetAsync($"{route}/{analyse}/notes?session=1").Ok();
        Assert.Equal(14.5m, sheet.GetProperty("lines")[0].GetProperty("score").GetDecimal());
        var (other, _) = await lecturer.Put($"{route}/{algebre}/notes", new
        {
            session = 1, lines = new[] { new { enrollmentId = enrollment, score = (decimal?)12m } },
        }).Fail();
        Assert.Equal(HttpStatusCode.Forbidden, other);
        var (direct, _) = await lecturer.Put($"/api/etablissement/lmd/promotions/{promotion}/ecs/{analyse}/notes", new
        {
            session = 1, lines = new[] { new { enrollmentId = enrollment, score = (decimal?)20m } },
        }).Fail();
        Assert.Equal(HttpStatusCode.Forbidden, direct);
    }
}
