using System.Text.Json;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class OfflineReplicaTests
{
    private static (OfflineStore Store, string Directory) NewStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scolia-replica", Guid.NewGuid().ToString("N"));
        return (new OfflineStore(new Uri("http://127.0.0.1:1/"), dir) { User = "secretariat" }, dir);
    }

    private static OutboxItem Queue(OfflineStore store, string method, string path, string body)
    {
        var item = new OutboxItem { User = store.User, Method = method, Path = path,
            Body = body, Key = Guid.NewGuid().ToString("N"), Label = "Test local" };
        store.Enqueue(item);
        return item;
    }

    [Fact]
    public async Task Identite_modifiee_reste_visible_hors_ligne_jusqu_a_la_confirmation()
    {
        var (store, directory) = NewStore();
        var id = Guid.NewGuid();
        var detailPath = $"api/apprenants/{id}";
        try
        {
            var student = new StudentDto(id, "INF293", "ANCIEN", "POST", "PRENOM", "M", null, null,
                null, null, null, null, null, null, null, null, null, false, [], 8);
            store.SavePage(detailPath, JsonSerializer.Serialize(student, Json.Options));
            store.SavePage("api/apprenants?page=1&taille=50", JsonSerializer.Serialize(
                new PagedResult<StudentListItem>([
                    new StudentListItem(id, null, "INF293", "ANCIEN", "POST", "PRENOM",
                        "M", "INF2", "Actif", null, 0, 0)], 1, 1, 50, null), Json.Options));
            var operation = Queue(store, "PUT", detailPath,
                """{"lastName":"NOUVEAU","middleName":"POST","firstName":"PRENOM","version":8}""");

            using var api = new ScoliaClient(new Uri("http://127.0.0.1:1/")) { Offline = store };
            Assert.False(await api.IsRunningAsync());
            Assert.Equal("NOUVEAU", (await api.GetAsync<StudentDto>(detailPath)).LastName);

            var page = OfflineStudentSearch.Search(store, "NOUVEAU", null, null, 1);
            Assert.NotNull(page);
            Assert.Equal("NOUVEAU", Assert.Single(page.Page.Items).LastName);

            store.Sent(operation.Id);
            Assert.Equal("ANCIEN", (await api.GetAsync<StudentDto>(detailPath)).LastName);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Inscription_locale_reste_recherchable_avec_un_matricule_provisoire()
    {
        var (store, directory) = NewStore();
        try
        {
            store.SavePage("api/etablissement/promotions", "[]");
            var queued = Queue(store, "POST", "api/apprenants",
                """{"identity":{"lastName":"KASONGO","middleName":"LONO","firstName":"PAUL","gender":"M"},"promotionId":"ac000000-0000-0000-0000-000000000001","enrolledOn":"2026-11-06"}""");
            var provisional = Assert.Single(OfflineReplica.PendingRegistrationRows(store));
            Assert.StartsWith("LOCAL-", provisional.Matricule, StringComparison.Ordinal);
            Assert.Null(provisional.EnrollmentId);
            Assert.Equal(Guid.Parse(queued.Id), provisional.Id);

            using var api = new ScoliaClient(new Uri("http://127.0.0.1:1/")) { Offline = store };
            Assert.False(await api.IsRunningAsync());
            var detail = await api.GetAsync<StudentDto>($"api/apprenants/{provisional.Id}");
            Assert.Equal("KASONGO", detail.LastName);
            Assert.Empty(detail.Enrollments);
            var search = OfflineStudentSearch.Search(store, "KASONGO", null, null, 1);
            Assert.Equal(provisional.Id, Assert.Single(search!.Page.Items).Id);
            Assert.False(search.Complete);
            store.User = "comptabilite";
            Assert.Empty(OfflineReplica.PendingRegistrationRows(store));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Notes_et_presences_sont_immediatement_visibles_apres_ecriture_locale()
    {
        var (store, directory) = NewStore();
        try
        {
            var evaluationId = Guid.NewGuid();
            var enrollmentId = Guid.NewGuid();
            var gradePath = $"api/scolarite/evaluations/{evaluationId}/notes";
            var grade = new GradeSheetDto(new AssessmentDto(evaluationId, Guid.NewGuid(),
                    "Interrogation", "Devoir", 1, new DateOnly(2026, 11, 6), 20, 1, 0),
                [new GradeRowDto(enrollmentId, "INF293", "KASONGO", null, null)]);
            store.SavePage(gradePath, JsonSerializer.Serialize(grade, Json.Options));
            Queue(store, "PUT", gradePath,
                $"[{{\"enrollmentId\":\"{enrollmentId}\",\"score\":17,\"comment\":\"Bon travail\"}}]");

            var attendancePath = "api/scolarite/presences?promotionId=" + Guid.NewGuid()
                + "&date=2026-11-06";
            var attendance = new AttendanceSheetDto(
                [new AttendanceRowDto(enrollmentId, "INF293", "KASONGO", null, null)], true);
            store.SavePage(attendancePath, JsonSerializer.Serialize(attendance, Json.Options));
            Queue(store, "PUT", attendancePath,
                $"[{{\"enrollmentId\":\"{enrollmentId}\",\"status\":\"Present\",\"note\":null}}]");

            using var api = new ScoliaClient(new Uri("http://127.0.0.1:1/")) { Offline = store };
            Assert.False(await api.IsRunningAsync());
            var grades = await api.GetAsync<GradeSheetDto>(gradePath);
            var sheet = await api.GetAsync<AttendanceSheetDto>(attendancePath);
            Assert.Equal(17, Assert.Single(grades.Rows).Score);
            Assert.Equal("Bon travail", Assert.Single(grades.Rows).Comment);
            Assert.Equal("Present", Assert.Single(sheet.Rows).Status);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
