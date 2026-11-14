using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scolia.Application.Abstractions;
using Scolia.Domain.School;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Tests;

/// <summary>ENAFEP : seuls les élèves de 6e primaire sont candidats ; le résultat est saisi.</summary>
public sealed class NationalExamApiTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    [Fact]
    public async Task Sixth_grade_pupils_are_enafep_candidates_and_results_are_recorded_not_computed()
    {
        // La classe du constructeur est une 5e : la passer en 6e primaire.
        var k = await EpstClassBuilder.BuildAsync(app, [(0.8m, 0.8m), (0.6m, 0.6m)]);
        var empty = await k.Admin.GetAsync("/api/etablissement/examens-nationaux/Enafep").Ok();
        Assert.Empty(empty.GetProperty("candidates").EnumerateArray());
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
            var classroom = await db.Promotions.SingleAsync(p => p.Id == k.ClassId);
            classroom.Level = 6;
            classroom.Name = "6e primaire A";
            scope.ServiceProvider.GetRequiredService<IAuditLog>().Record("tests.niveau", "Promotion",
                classroom.Id.ToString(), "Passage du niveau de test en 6e.");
            await db.SaveChangesAsync();
        }

        var list = await k.Admin.GetAsync("/api/etablissement/examens-nationaux/enafep").Ok();
        Assert.Equal("ENAFEP", list.GetProperty("label").GetString());
        Assert.Equal(2, list.GetProperty("candidates").GetArrayLength());
        Assert.Empty((await k.Admin.GetAsync("/api/etablissement/examens-nationaux/ExamenEtat").Ok())
            .GetProperty("candidates").EnumerateArray());

        var saved = await k.Admin.Put($"/api/etablissement/examens-nationaux/Enafep/eleves/{k.Pupils[0]}",
            new { passed = true, percent = 68.5m, reference = "KIN-04-0123" }).Ok();
        Assert.True(saved.GetProperty("passed").GetBoolean());
        var (bad, _) = await k.Admin.Put($"/api/etablissement/examens-nationaux/Enafep/eleves/{k.Pupils[1]}",
            new { passed = true, percent = 120m, reference = (string?)null }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, bad);
        var (notCandidate, _) = await k.Admin.Put($"/api/etablissement/examens-nationaux/FinCteb/eleves/{k.Pupils[1]}",
            new { passed = true, percent = 60m, reference = (string?)null }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, notCandidate);

        var xlsx = await k.Admin.GetAsync("/api/etablissement/examens-nationaux/Enafep/liste?format=xlsx");
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        Assert.Equal("PK", System.Text.Encoding.ASCII.GetString((await xlsx.Content.ReadAsByteArrayAsync())[..2]));
        var pdf = await k.Admin.GetAsync("/api/etablissement/examens-nationaux/Enafep/liste");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
    }
}
