using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scolia.Domain.School;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Tests;

/// <summary>Règlement pédagogique par année : ITM intact, proposition, validation tracée, réouverture motivée.</summary>
public sealed class AcademicRegulationApiTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    private async Task<HttpClient> Install(string type)
    {
        var admin = app.Client();
        await admin.Post("/api/installation", new
        {
            institutionType = type, governance = "Prive",
            schoolName = "Établissement de test", shortName = "TEST", phone = "0810000000",
            city = "Kinshasa", currencyCode = "CDF", currencySymbol = "FC",
            yearLabel = Http.Year(DateOnly.FromDateTime(DateTime.Today)),
            startMonth = 9, monthCount = 10,
            adminUserName = "admin", adminDisplayName = "Secrétariat",
            adminPassword = "Scolia2026!", loadDemoData = false,
        }).Ok();
        return admin;
    }

    private static object Epst(decimal pass) => new
    {
        termsCount = 3, periodsPerTerm = 2, examMaxFactor = 2m, passPercent = pass, subjectPassPercent = 50m,
        maxRepechageSubjects = 2, showRank = true, markScale = 0m, ueValidationMark = 0m,
        compensateWithinUe = false, compensateBetweenUe = false, maxDebtCredits = 0m,
        legalReference = "Conseil de l'école du 14 novembre",
    };

    [Fact]
    public async Task Itm_keeps_its_historical_deliberation_settings_without_regulation()
    {
        var admin = await app.InstalledAdmin();
        var (status, problem) = await admin.GetAsync("/api/etablissement/reglements").Fail();
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("reglement", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Primary_school_reviews_proposal_saves_validates_then_reopens_with_reason()
    {
        var admin = await Install("Primaire");
        var list = await admin.GetAsync("/api/etablissement/reglements").Ok();
        var proposal = Assert.Single(list.EnumerateArray());
        Assert.Equal("Primaire", proposal.GetProperty("scope").GetString());
        Assert.False(proposal.GetProperty("saved").GetBoolean());
        Assert.Equal(3, proposal.GetProperty("termsCount").GetInt32());
        Assert.Equal(50m, proposal.GetProperty("passPercent").GetDecimal());

        var saved = await admin.Put("/api/etablissement/reglements/primaire", Epst(55m)).Ok();
        Assert.True(saved.GetProperty("saved").GetBoolean());
        Assert.Equal("Brouillon", saved.GetProperty("status").GetString());
        Assert.Equal(55m, saved.GetProperty("passPercent").GetDecimal());

        var validated = await admin.Post("/api/etablissement/reglements/Primaire/validation", new { }).Ok();
        Assert.Equal("Valide", validated.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(validated.GetProperty("validatedByName").GetString()));

        var (frozen, frozenBody) = await admin.Put("/api/etablissement/reglements/Primaire", Epst(60m)).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, frozen);
        Assert.Equal("reglement", frozenBody.GetProperty("code").GetString());

        var (noReason, _) = await admin.Post("/api/etablissement/reglements/Primaire/reouverture",
            new { reason = "" }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, noReason);
        var reopened = await admin.Post("/api/etablissement/reglements/Primaire/reouverture",
            new { reason = "Erreur sur le seuil de réussite" }).Ok();
        Assert.Equal("Brouillon", reopened.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, reopened.GetProperty("validatedByName").ValueKind);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        var row = await db.AcademicRegulations.SingleAsync();
        Assert.Equal((RegulationScope.Primaire, 55m), (row.Scope, row.PassPercent));
        var actions = await db.AuditEntries.Where(a => a.Action.StartsWith("reglement."))
            .Select(a => a.Action).ToListAsync();
        Assert.Equal(["reglement.cree", "reglement.rouvert", "reglement.valide"], actions.Order().ToArray());
    }

    [Fact]
    public async Task University_can_validate_the_lmd_proposal_directly_and_refuses_epst_scope()
    {
        var admin = await Install("SuperieurLmd");
        var validated = await admin.Post("/api/etablissement/reglements/SuperieurLmd/validation", new { }).Ok();
        Assert.Equal("Valide", validated.GetProperty("status").GetString());
        Assert.Equal(10m, validated.GetProperty("ueValidationMark").GetDecimal());
        Assert.True(validated.GetProperty("compensateWithinUe").GetBoolean());
        Assert.False(validated.GetProperty("compensateBetweenUe").GetBoolean());
        Assert.Equal(15m, validated.GetProperty("maxDebtCredits").GetDecimal());

        var (status, _) = await admin.Put("/api/etablissement/reglements/Primaire", Epst(50m)).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, status);
        var (unknown, _) = await admin.Put("/api/etablissement/reglements/Lycee", Epst(50m)).Fail();
        Assert.Equal(HttpStatusCode.NotFound, unknown);
    }

    [Fact]
    public async Task Complex_school_gets_one_regulation_per_declared_cycle()
    {
        var admin = await Install("ComplexeScolaire");
        await admin.Put("/api/etablissement/cycles", new
        {
            cycles = new[]
            {
                new { kind = "Maternelle", name = "Maternelle", authorizationNumber = (string?)null,
                      chargeMonthlyTuition = true, monthlyAmount = 50000m },
                new { kind = "Secondaire", name = "Secondaire", authorizationNumber = (string?)null,
                      chargeMonthlyTuition = true, monthlyAmount = 90000m },
            },
        }).Ok();
        var list = await admin.GetAsync("/api/etablissement/reglements").Ok();
        Assert.Equal(new[] { "Maternelle", "Secondaire" },
            list.EnumerateArray().Select(r => r.GetProperty("scope").GetString()!).ToArray());
        var (status, _) = await admin.Put("/api/etablissement/reglements/Primaire", Epst(50m)).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }
}
