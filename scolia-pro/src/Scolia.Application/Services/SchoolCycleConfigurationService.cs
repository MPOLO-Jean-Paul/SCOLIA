using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.School;

namespace Scolia.Application.Services;

/// <summary>
/// Configuration de l'offre maternelle / primaire / secondaire d'un complexe.
/// Les données communes sont celles de l'établissement ; ni les inscriptions,
/// ni les paiements, ni les notes ne sont changés par cette configuration.
/// 
/// Tant que les modules scolaires ne sont pas livrés, le contrôle
/// InstitutionProfiles.IsEnabled empêche toute modification hors ITM.
/// </summary>
public sealed class SchoolCycleConfigurationService(IAppDbContext db, IWriteGate gate, IAuditLog audit)
{
    public async Task<SchoolCycleConfigurationDto> GetAsync(CancellationToken ct = default)
    {
        var school = await db.Schools.AsNoTracking().OrderBy(s => s.CreatedAtUtc).FirstAsync(ct);
        var yearId = await db.AcademicYears.AsNoTracking().Where(y => y.IsCurrent)
            .Select(y => (Guid?)y.Id).FirstOrDefaultAsync(ct);
        var cycles = await db.SchoolCycles.AsNoTracking().Where(c => c.SchoolId == school.Id)
            .OrderBy(c => c.Kind).ToListAsync(ct);
        var fees = yearId is { } id
            ? await db.SchoolCycleFeePolicies.AsNoTracking()
                .Where(f => f.AcademicYearId == id).ToListAsync(ct)
            : [];
        var cycleIds = cycles.Select(c => c.Id).ToArray();
        var counts = cycleIds.Length == 0 ? new Dictionary<Guid, int>()
            : await db.Promotions.AsNoTracking()
                .Where(p => p.SchoolCycleId != null && cycleIds.Contains(p.SchoolCycleId.Value))
                .GroupBy(p => p.SchoolCycleId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var items = cycles.Select(c =>
        {
            var fee = fees.FirstOrDefault(f => f.SchoolCycleId == c.Id);
            return new SchoolCycleConfigurationItemDto(c.Id, c.Kind.ToString(), c.Name,
                c.AuthorizationNumber, c.IsActive, fee?.ChargeMonthlyTuition ?? false,
                fee?.MonthlyAmount ?? 0, counts.GetValueOrDefault(c.Id));
        }).ToList();
        return new SchoolCycleConfigurationDto(
            InstitutionProfiles.Canonicalize(school.InstitutionType),
            school.Governance.ToString(), yearId, items);
    }

    /// <summary>
    /// Préserve les identifiants des cycles, ne supprime aucune classe et ne
    /// crée aucune facture. Les valeurs sont enregistrées dans les tables
    /// de configuration uniquement, avec audit et transaction métier.
    /// </summary>
    public Task<SchoolCycleConfigurationDto> ConfigureAsync(
        SchoolCycleConfigurationInput input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var school = await db.Schools.OrderBy(s => s.CreatedAtUtc).FirstAsync(ct);
        var profile = InstitutionProfiles.Canonicalize(school.InstitutionType);
        if (profile is InstitutionProfiles.ItmIm or InstitutionProfiles.SuperieurLmd)
            throw new DomainException("cycles", "La configuration des cycles scolaires n'est pas applicable à un établissement ITM/IM ou LMD.");
        if (!InstitutionProfiles.IsEnabled(profile))
            throw new DomainException("cycles", "Le module des complexes scolaires reste en préparation et ne peut pas encore être activé.");

        var year = await db.AcademicYears.FirstOrDefaultAsync(y => y.IsCurrent, ct)
            ?? throw new DomainException("annee", "Choisissez une année scolaire courante.");
        var drafts = input.Cycles
            ?? throw new DomainException("cycles", "Choisissez les cycles de l'établissement.");
        var old = await db.SchoolCycles.Where(c => c.SchoolId == school.Id).ToListAsync(ct);
        var selected = new List<(SchoolCycle Cycle, SchoolCycleConfigurationDraft Draft)>();
        var seen = new HashSet<SchoolCycleKind>();

        foreach (var draft in drafts)
        {
            if (!Enum.TryParse<SchoolCycleKind>(draft.Kind, true, out var kind) ||
                !Enum.IsDefined(kind) || !seen.Add(kind))
                throw new DomainException("cycles", "Cycle inconnu ou sélectionné plusieurs fois.");
            if (draft.MonthlyAmount < 0 ||
                (school.WholeAmountsOnly && draft.MonthlyAmount != decimal.Truncate(draft.MonthlyAmount)))
                throw new DomainException("cycles", "Le tarif du cycle doit être un montant positif ou nul, dans la précision monétaire de l'établissement.");
            if ((draft.ChargeMonthlyTuition && draft.MonthlyAmount <= 0) ||
                (!draft.ChargeMonthlyTuition && draft.MonthlyAmount != 0))
                throw new DomainException("cycles", "Indiquez un tarif positif pour une mensualité active, sinon zéro.");

            var cycle = old.FirstOrDefault(c => c.Kind == kind) ?? new SchoolCycle
            {
                SchoolId = school.Id, Kind = kind,
            };
            cycle.Name = (draft.Name ?? string.Empty).Trim();
            cycle.AuthorizationNumber = string.IsNullOrWhiteSpace(draft.AuthorizationNumber)
                ? null : draft.AuthorizationNumber.Trim();
            cycle.IsActive = true;
            selected.Add((cycle, draft));
        }
        SchoolCycleRules.ValidateOffering(school, selected.Select(x => x.Cycle).ToArray());

        foreach (var existing in old.Where(c => !seen.Contains(c.Kind)))
        {
            if (await db.Promotions.AnyAsync(p => p.SchoolCycleId == existing.Id, ct))
                throw new DomainException("cycles",
                    $"Le cycle « {existing.Name} » possède déjà des classes ; il ne peut pas être retiré de l'offre.");
            existing.IsActive = false;
        }

        foreach (var (cycle, draft) in selected)
        {
            var fee = await db.SchoolCycleFeePolicies
                .FirstOrDefaultAsync(f => f.AcademicYearId == year.Id && f.SchoolCycleId == cycle.Id, ct)
                ?? new SchoolCycleFeePolicy { SchoolCycleId = cycle.Id, AcademicYearId = year.Id };
            fee.ChargeMonthlyTuition = draft.ChargeMonthlyTuition;
            fee.MonthlyAmount = draft.MonthlyAmount;
            SchoolCycleRules.ValidateFeePolicy(school, cycle, year, fee);
            if (db.SchoolCycles.Local.All(c => c.Id != cycle.Id))
                db.SchoolCycles.Add(cycle);
            if (db.SchoolCycleFeePolicies.Local.All(f => f.Id != fee.Id))
                db.SchoolCycleFeePolicies.Add(fee);
        }

        audit.Record("cycles.configures", "CyclesScolaires", school.Id.ToString(),
            $"Offre scolaire configurée : {string.Join(", ", selected.Select(s => s.Cycle.Name))}. Tarifs de référence uniquement.");
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }, ct);
}
