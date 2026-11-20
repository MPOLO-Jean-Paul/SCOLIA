using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.Finance;
using Scolia.Domain.School;

namespace Scolia.Application.Services;

/// <summary>
/// Catalogue des catégories de frais de l'établissement. Une catégorie n'existe que si elle a été créée ;
/// elle s'ajoute ensuite aux promotions avec son montant (modifiable par promotion).
/// Les mensualités ne sont pas une catégorie : elles restent obligatoires pour chaque promotion.
/// </summary>
public sealed class FeeCategoryService(IAppDbContext db, IWriteGate gate, IAuditLog audit)
{
    public async Task<IReadOnlyList<FeeCategoryDto>> ListAsync(CancellationToken ct = default)
    {
        var categories = await db.FeeCategories.AsNoTracking().OrderBy(c => c.Order).ThenBy(c => c.Name).ToListAsync(ct);
        var year = await db.AcademicYears.AsNoTracking().Where(y => y.IsCurrent).Select(y => (Guid?)y.Id).FirstOrDefaultAsync(ct);
        var usage = year is null ? [] : await (from f in db.FeeItems.AsNoTracking()
                                               join p in db.Promotions.AsNoTracking() on f.PromotionId equals p.Id
                                               where p.AcademicYearId == year && f.CategoryId != null
                                               group f by f.CategoryId into g
                                               select new { Id = g.Key!.Value, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        return categories.Select(c => ToDto(c, usage.GetValueOrDefault(c.Id))).ToList();
    }

    public static FeeCategoryDto ToDto(FeeCategory c, int promotions) => new(c.Id, c.Name, c.Amount, c.DueDate, c.IsActive, c.Order, promotions);

    public Task<FeeCategoryDto> UpsertAsync(Guid? id, FeeCategoryUpsert input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var school = await YearContext.SchoolAsync(db, ct);
        if (school.Governance == SchoolGovernanceKind.Public)
            throw new DomainException("fraisPublics",
                "Les frais publics nécessitent une décision réglementaire vérifiée. Le catalogue des tarifs privés n'est pas disponible ici.");
        var name = (input.Name ?? string.Empty).Trim();
        new ValidationBuilder()
            .Require(name.Length is > 0 and <= FeeCategory.MaxNameLength, "name", "Nommez la catégorie (80 caractères au plus), par exemple « Frais d'inscription ».")
            .Require(!TextKey.Normalize(name).StartsWith("MENSUALIT", StringComparison.Ordinal), "name",
                "La mensualité n'est pas une catégorie : elle se règle dans chaque promotion et reste obligatoire.")
            .ThrowIfInvalid();
        Money.EnsureValid(input.Amount, school.WholeAmountsOnly, "amount");
        var key = TextKey.Normalize(name);
        if (await db.FeeCategories.AnyAsync(c => c.NameKey == key && c.Id != id, ct))
            throw new ConflictException($"La catégorie « {name} » existe déjà.");

        var category = id is null
            ? new FeeCategory { Order = await db.FeeCategories.CountAsync(ct) + 1 }
            : await db.FeeCategories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Catégorie de frais introuvable.");
        var renamed = id is not null && category.Name != name;
        category.Name = name;
        category.NameKey = key;
        category.Amount = input.Amount;
        category.DueDate = input.DueDate;
        category.IsActive = input.IsActive;
        if (id is null) db.FeeCategories.Add(category);

        // Le libellé des rubriques suit le nom de la catégorie dans les années ouvertes ; une année clôturée garde le sien.
        if (renamed)
            foreach (var item in await (from f in db.FeeItems
                                        join p in db.Promotions on f.PromotionId equals p.Id
                                        join y in db.AcademicYears on p.AcademicYearId equals y.Id
                                        where f.CategoryId == category.Id && y.Status != AcademicYearStatus.Closed
                                        select f).ToListAsync(ct))
                item.Label = name;

        var applied = 0;
        if (input.ApplyToPromotions && category.IsActive)
            applied = await ApplyAsync(category, ct);

        audit.Record(id is null ? "frais.categorie.creee" : "frais.categorie.modifiee", "CategorieFrais", category.Id.ToString(),
            $"Catégorie de frais « {name} » : {Money.Display(input.Amount, school.CurrencySymbol)}" +
            (applied > 0 ? $", appliquée à {applied} promotion(s)." : "."));
        await db.SaveChangesAsync(ct);
        var count = await db.FeeItems.CountAsync(f => f.CategoryId == category.Id, ct);
        return ToDto(category, count);
    }, ct);

    /// <summary>Ajoute ou met à jour la catégorie dans toutes les promotions actives de l'année courante.</summary>
    private async Task<int> ApplyAsync(FeeCategory category, CancellationToken ct)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var promotions = await db.Promotions.Include(p => p.Fees).Where(p => p.AcademicYearId == year.Id && p.IsActive).ToListAsync(ct);
        var due = DueDateIn(category, year);
        var itemIds = promotions.SelectMany(p => p.Fees).Where(f => f.CategoryId == category.Id).Select(f => f.Id).ToList();
        var paid = await PaidByItemAsync(db, itemIds, ct);
        foreach (var promotion in promotions)
        {
            var item = promotion.Fees.FirstOrDefault(f => f.CategoryId == category.Id);
            if (item is null)
            {
                item = new FeeItem
                {
                    PromotionId = promotion.Id, CategoryId = category.Id, Kind = FeeKind.Autre, Label = category.Name,
                    Amount = category.Amount, DueDate = due, Order = 100 + promotion.Fees.Count,
                };
                promotion.Fees.Add(item);
                db.FeeItems.Add(item);
                continue;
            }
            EnsureNotBelowPaid(item, category.Amount, paid);
            item.Amount = category.Amount;
            item.DueDate = due;
        }
        return promotions.Count;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var category = await db.FeeCategories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Catégorie de frais introuvable.");
        var items = await db.FeeItems.Where(f => f.CategoryId == id).ToListAsync(ct);
        var itemIds = items.Select(f => f.Id).ToList();
        if (itemIds.Count > 0 && (await db.ReceiptAllocations.AnyAsync(a => itemIds.Contains(a.FeeItemId), ct)
                                  || await db.SponsorshipLines.AnyAsync(l => itemIds.Contains(l.FeeItemId), ct)))
            throw new DomainException("categorie", $"« {category.Name} » a déjà été encaissée ou prise en charge : désactivez-la au lieu de la supprimer.");
        // Une année clôturée ne change plus : ses soldes ne doivent pas bouger après coup.
        var promotionIds = items.Select(f => f.PromotionId).Distinct().ToList();
        if (promotionIds.Count > 0 && await (from p in db.Promotions
                                             join y in db.AcademicYears on p.AcademicYearId equals y.Id
                                             where promotionIds.Contains(p.Id) && y.Status == AcademicYearStatus.Closed
                                             select p.Id).AnyAsync(ct))
            throw new DomainException("categorie", $"« {category.Name} » figure dans une année clôturée : désactivez-la au lieu de la supprimer.");
        db.FeeItems.RemoveRange(items);
        db.FeeCategories.Remove(category);
        audit.Record("frais.categorie.supprimee", "CategorieFrais", id.ToString(),
            $"Catégorie de frais « {category.Name} » supprimée ({items.Count} rubrique(s) non encaissée(s) retirée(s)).");
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);

    /// <summary>Échéance d'une catégorie dans une année : sa date si elle tombe dans l'année, sinon la rentrée.</summary>
    public static DateOnly DueDateIn(FeeCategory category, AcademicYear year) =>
        category.DueDate is { } d && d >= year.StartDate && d <= year.EndDate ? d : year.StartDate;

    public static void EnsureNotBelowPaid(FeeItem item, decimal newAmount, IReadOnlyDictionary<Guid, decimal> paidByItem)
    {
        var maxPaid = paidByItem.GetValueOrDefault(item.Id);
        if (newAmount < maxPaid)
            throw new DomainException("montant", $"« {item.Label} » : un apprenant a déjà versé {maxPaid:N0}. Le montant ne peut pas être inférieur.");
    }

    /// <summary>Plus grand montant versé par une même inscription sur chaque rubrique.</summary>
    public static async Task<Dictionary<Guid, decimal>> PaidByItemAsync(IAppDbContext db, IReadOnlyCollection<Guid> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0) return [];
        var rows = await (from a in db.ReceiptAllocations.AsNoTracking()
                          join r in db.Receipts.AsNoTracking() on a.ReceiptId equals r.Id
                          where itemIds.Contains(a.FeeItemId) && r.Status == OperationStatus.Valide
                          select new { a.FeeItemId, r.EnrollmentId, a.Amount }).ToListAsync(ct);
        return rows.GroupBy(r => r.FeeItemId).ToDictionary(g => g.Key, g => g.GroupBy(x => x.EnrollmentId).Max(e => e.Sum(x => x.Amount)));
    }
}
