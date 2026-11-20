using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.School;

namespace Scolia.Application.Services;

/// <summary>
/// Frais académiques d'une promotion universitaire payables par tranches à dates fixes.
/// Chaque tranche est une rubrique ordinaire de la caisse (catégorie « Frais académiques — Ne tranche »),
/// avec son échéance : encaissements, reçus, retards et prises en charge restent ceux de SCOLIA.
/// </summary>
public sealed class LmdFeesService(IAppDbContext db, SchoolService schools, FeeCategoryService categories)
{
    public static string TrancheName(int n) => $"Frais académiques — {(n == 1 ? "1re" : $"{n}e")} tranche";

    public async Task<PromotionDto> SetInstallmentsAsync(Guid promotionId, LmdInstallmentsInput input, CancellationToken ct = default)
    {
        var school = await YearContext.SchoolAsync(db, ct);
        if (InstitutionProfiles.Canonicalize(school.InstitutionType) != InstitutionProfiles.SuperieurLmd)
            throw new DomainException("tranches", "Les frais académiques par tranches concernent les universités LMD.");
        var tranches = input.Installments ?? [];
        if (tranches.Count is < 1 or > 6)
            throw new DomainException("tranches", "Indiquez de 1 à 6 tranches.");
        if (tranches.Any(t => t.Amount <= 0))
            throw new DomainException("tranches", "Chaque tranche doit avoir un montant positif.");
        if (tranches.Zip(tranches.Skip(1)).Any(p => p.Second.DueDate <= p.First.DueDate))
            throw new DomainException("tranches", "Les échéances des tranches doivent se suivre dans le temps.");
        var promotion = await db.Promotions.AsNoTracking().Include(p => p.Fees)
            .FirstOrDefaultAsync(p => p.Id == promotionId, ct) ?? throw new NotFoundException("Promotion introuvable.");
        var year = await db.AcademicYears.AsNoTracking().FirstAsync(y => y.Id == promotion.AcademicYearId, ct);
        if (tranches.Any(t => t.DueDate < year.StartDate || t.DueDate > year.EndDate))
            throw new DomainException("tranches", $"Les échéances doivent se situer dans l'année {year.Label}.");

        // Catégories des tranches : créées une fois, réutilisées par toutes les promotions.
        var known = await db.FeeCategories.AsNoTracking().ToListAsync(ct);
        var trancheCategories = new List<Guid>();
        for (var i = 0; i < tranches.Count; i++)
        {
            var name = TrancheName(i + 1);
            var key = TextKey.Normalize(name);
            var category = known.FirstOrDefault(c => c.NameKey == key);
            trancheCategories.Add(category?.Id ??
                (await categories.UpsertAsync(null, new FeeCategoryUpsert(name, tranches[i].Amount, null), ct)).Id);
        }
        var allTrancheKeys = Enumerable.Range(1, 6).Select(n => TextKey.Normalize(TrancheName(n))).ToHashSet();
        var trancheIds = known.Where(c => allTrancheKeys.Contains(c.NameKey)).Select(c => c.Id).Concat(trancheCategories).ToHashSet();

        // Les autres frais ponctuels de la promotion sont conservés à l'identique.
        var others = promotion.Fees
            .Where(f => f.Kind != FeeKind.Mensualite && !(f.CategoryId is { } id && trancheIds.Contains(id)))
            .Select(f => new OneTimeFee(f.Kind.ToString(), f.Label, f.Amount, f.DueDate, f.CategoryId));
        var installments = tranches.Select((t, i) =>
            new OneTimeFee(FeeKind.Autre.ToString(), TrancheName(i + 1), t.Amount, t.DueDate, trancheCategories[i]));
        var monthly = promotion.Fees.Where(f => f.Kind == FeeKind.Mensualite).Select(f => f.Amount).DefaultIfEmpty(0).Max();
        return await schools.UpsertPromotionAsync(promotion.Id, new PromotionUpsert(
            promotion.Code, promotion.Name, promotion.SectionId, promotion.Level, promotion.Capacity, promotion.IsActive,
            promotion.NextPromotionCode, monthly, [.. others, .. installments], ManagesCategories: true,
            AcademicUnitId: promotion.AcademicUnitId), ct);
    }
}
