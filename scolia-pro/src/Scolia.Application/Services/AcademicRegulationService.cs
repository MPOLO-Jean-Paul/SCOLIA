using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.School;

namespace Scolia.Application.Services;

/// <summary>
/// Règlement pédagogique par année et par portée (cycle EPST ou université LMD).
/// ITM/IM n'est pas concerné : son moteur de délibération historique reste la référence.
/// </summary>
public sealed class AcademicRegulationService(IAppDbContext db, IWriteGate gate, IAuditLog audit, ICurrentUser user)
{
    private async Task<(SchoolProfile School, AcademicYear Year, IReadOnlyList<RegulationScope> Scopes)> ContextAsync(
        CancellationToken ct)
    {
        var school = await YearContext.SchoolAsync(db, ct);
        var year = await YearContext.CurrentAsync(db, ct);
        var cycles = await db.SchoolCycles.AsNoTracking()
            .Where(c => c.SchoolId == school.Id && c.IsActive).Select(c => c.Kind).ToListAsync(ct);
        var scopes = AcademicRegulationRules.ScopesFor(school.InstitutionType, cycles);
        if (scopes.Count == 0)
            throw new DomainException("reglement",
                "Les instituts techniques médicaux conservent leurs paramètres de délibération habituels.");
        return (school, year, scopes);
    }

    private static RegulationScope ParseScope(string? scope, IReadOnlyList<RegulationScope> allowed)
    {
        if (!Enum.TryParse<RegulationScope>(scope, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            throw new NotFoundException("Portée de règlement inconnue.");
        if (!allowed.Contains(parsed))
            throw new DomainException("reglement", "Cette portée ne correspond pas aux cycles de l'établissement.");
        return parsed;
    }

    private static AcademicRegulationDto ToDto(AcademicRegulation r, bool saved) => new(
        saved ? r.Id : null, r.Scope.ToString(), AcademicRegulationRules.Label(r.Scope),
        r.Status.ToString(), saved,
        r.TermsCount, r.PeriodsPerTerm, r.ExamMaxFactor,
        r.PassPercent, r.SubjectPassPercent, r.MaxRepechageSubjects, r.ShowRank,
        r.MarkScale, r.UeValidationMark, r.CompensateWithinUe, r.CompensateBetweenUe,
        r.MaxDebtCredits, r.LegalReference, r.ValidatedByName, r.ValidatedAtUtc);

    private static void RequireOpen(AcademicYear year)
    {
        if (year.Status == AcademicYearStatus.Closed)
            throw new DomainException("reglement", "Le règlement d'une année clôturée ne peut plus changer.");
    }

    /// <summary>Règlements de l'année courante ; une portée sans règlement affiche la proposition SCOLIA.</summary>
    public async Task<IReadOnlyList<AcademicRegulationDto>> ListAsync(CancellationToken ct = default)
    {
        var (_, year, scopes) = await ContextAsync(ct);
        var saved = await db.AcademicRegulations.AsNoTracking()
            .Where(r => r.AcademicYearId == year.Id).ToListAsync(ct);
        return scopes.Select(scope => saved.FirstOrDefault(r => r.Scope == scope) is { } r
                ? ToDto(r, true)
                : ToDto(AcademicRegulationRules.DefaultFor(scope, year.Id), false))
            .ToList();
    }

    /// <summary>Règlement validé de la portée, exigé avant tout bulletin officiel ou toute décision.</summary>
    public static async Task<AcademicRegulation> RequireValidatedAsync(IAppDbContext db, Guid yearId,
        RegulationScope scope, CancellationToken ct)
    {
        var r = await db.AcademicRegulations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.AcademicYearId == yearId && x.Scope == scope, ct);
        if (r is not { IsValidated: true })
            throw new DomainException("reglement",
                $"Le règlement pédagogique « {AcademicRegulationRules.Label(scope)} » de l'année doit être validé " +
                "dans Paramètres › Règlement pédagogique avant tout document officiel.");
        return r;
    }

    public Task<AcademicRegulationDto> SaveAsync(string scope, AcademicRegulationInput input,
        CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var (_, year, scopes) = await ContextAsync(ct);
        RequireOpen(year);
        var target = ParseScope(scope, scopes);
        var r = await db.AcademicRegulations
            .FirstOrDefaultAsync(x => x.AcademicYearId == year.Id && x.Scope == target, ct);
        var created = r is null;
        r ??= AcademicRegulationRules.DefaultFor(target, year.Id);
        AcademicRegulationRules.EnsureEditable(r);
        Apply(r, input);
        AcademicRegulationRules.Validate(r);
        if (created) db.AcademicRegulations.Add(r);
        audit.Record(created ? "reglement.cree" : "reglement.modifie", "ReglementPedagogique", r.Id.ToString(),
            $"{AcademicRegulationRules.Label(target)} {year.Label} : {Summary(r)}.");
        await db.SaveChangesAsync(ct);
        return ToDto(r, true);
    }, ct);

    /// <summary>Valide le règlement tel qu'il est affiché. Le nom du valideur et l'heure sont conservés.</summary>
    public Task<AcademicRegulationDto> ValidateAsync(string scope, CancellationToken ct = default) =>
        gate.RunAsync(async () =>
    {
        var (_, year, scopes) = await ContextAsync(ct);
        RequireOpen(year);
        var target = ParseScope(scope, scopes);
        var r = await db.AcademicRegulations
            .FirstOrDefaultAsync(x => x.AcademicYearId == year.Id && x.Scope == target, ct);
        var created = r is null;
        r ??= AcademicRegulationRules.DefaultFor(target, year.Id);
        AcademicRegulationRules.EnsureEditable(r);
        AcademicRegulationRules.Validate(r);
        r.Status = RegulationStatus.Valide;
        r.ValidatedByName = string.IsNullOrWhiteSpace(user.Name) ? "Utilisateur" : user.Name.Trim();
        r.ValidatedAtUtc = DateTime.UtcNow;
        if (created) db.AcademicRegulations.Add(r);
        audit.Record("reglement.valide", "ReglementPedagogique", r.Id.ToString(),
            $"{AcademicRegulationRules.Label(target)} {year.Label} validé par {r.ValidatedByName} : {Summary(r)}.");
        await db.SaveChangesAsync(ct);
        return ToDto(r, true);
    }, ct);

    /// <summary>Rouvre un règlement validé, avec motif obligatoire, pour corriger une erreur.</summary>
    public Task<AcademicRegulationDto> ReopenAsync(string scope, AcademicRegulationReopen input,
        CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var (_, year, scopes) = await ContextAsync(ct);
        RequireOpen(year);
        var target = ParseScope(scope, scopes);
        var reason = input.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length is < 5 or > 300)
            throw new DomainException("reglement", "Indiquez le motif de la réouverture (5 à 300 caractères).");
        var r = await db.AcademicRegulations
            .FirstOrDefaultAsync(x => x.AcademicYearId == year.Id && x.Scope == target, ct)
            ?? throw new NotFoundException("Aucun règlement enregistré pour cette portée.");
        if (!r.IsValidated)
            throw new DomainException("reglement", "Ce règlement n'est pas validé.");
        await RegulationUsage.EnsureReopenAllowedAsync(db, r, ct);
        r.Status = RegulationStatus.Brouillon;
        r.ValidatedByName = null;
        r.ValidatedAtUtc = null;
        audit.Record("reglement.rouvert", "ReglementPedagogique", r.Id.ToString(),
            $"{AcademicRegulationRules.Label(target)} {year.Label} rouvert par {user.Name} : {reason}");
        await db.SaveChangesAsync(ct);
        return ToDto(r, true);
    }, ct);

    private static void Apply(AcademicRegulation r, AcademicRegulationInput i)
    {
        if (AcademicRegulationRules.IsEpst(r.Scope))
        {
            r.TermsCount = i.TermsCount; r.PeriodsPerTerm = i.PeriodsPerTerm; r.ExamMaxFactor = i.ExamMaxFactor;
            r.PassPercent = i.PassPercent; r.SubjectPassPercent = i.SubjectPassPercent;
            r.MaxRepechageSubjects = i.MaxRepechageSubjects; r.ShowRank = i.ShowRank;
        }
        else if (r.Scope == RegulationScope.SuperieurLmd)
        {
            r.MarkScale = i.MarkScale; r.UeValidationMark = i.UeValidationMark;
            r.CompensateWithinUe = i.CompensateWithinUe; r.CompensateBetweenUe = i.CompensateBetweenUe;
            r.MaxDebtCredits = i.MaxDebtCredits;
        }
        else
        {
            r.TermsCount = i.TermsCount;
        }
        r.LegalReference = string.IsNullOrWhiteSpace(i.LegalReference) ? null : i.LegalReference.Trim();
    }

    private static string Summary(AcademicRegulation r) => r.Scope switch
    {
        RegulationScope.SuperieurLmd =>
            $"UE acquise à {r.UeValidationMark}/{r.MarkScale}, compensation EC {(r.CompensateWithinUe ? "oui" : "non")}, " +
            $"entre UE {(r.CompensateBetweenUe ? "oui" : "non")}, dette maximale {r.MaxDebtCredits} crédits",
        RegulationScope.Maternelle => $"{r.TermsCount} trimestre(s) d'observation",
        _ => $"{r.TermsCount} × {r.PeriodsPerTerm} périodes, examen ×{r.ExamMaxFactor}, réussite {r.PassPercent} %, " +
             $"branche {r.SubjectPassPercent} %, repêchage {r.MaxRepechageSubjects} branche(s), place {(r.ShowRank ? "oui" : "non")}",
    };
}

/// <summary>
/// Garde-fous de réouverture : un règlement ne peut pas être rouvert une fois que
/// des résultats officiels de la portée ont été publiés (voir les lots de délibération).
/// </summary>
public static class RegulationUsage
{
    public static async Task EnsureReopenAllowedAsync(IAppDbContext db, AcademicRegulation r, CancellationToken ct)
    {
        if (!AcademicRegulationRules.IsEpst(r.Scope)) return;
        var kind = r.Scope == RegulationScope.Primaire ? SchoolCycleKind.Primaire : SchoolCycleKind.Secondaire;
        var classes = from p in db.Promotions
                      join c in db.SchoolCycles on p.SchoolCycleId equals c.Id
                      where p.AcademicYearId == r.AcademicYearId && c.Kind == kind
                      select p.Id;
        if (await db.EpstDeliberations.AnyAsync(d => classes.Contains(d.PromotionId) &&
                d.Status != EpstSessionStatus.Ouverte, ct))
            throw new DomainException("reglement",
                "Des décisions de fin d'année ont été publiées selon ce règlement : il ne peut plus être rouvert.");
    }
}
