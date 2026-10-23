using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Common;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.Finance;
using Scolia.Domain.School;
using Scolia.Domain.Students;

namespace Scolia.Application.Services;

/// <summary>Établissement, années scolaires, sections, promotions et grilles de frais.</summary>
public sealed class SchoolService(IAppDbContext db, IWriteGate gate, IAuditLog audit)
{
    public async Task<SchoolDto> GetSchoolAsync(CancellationToken ct = default)
    {
        var s = await YearContext.SchoolAsync(db, ct);
        return ToDto(s);
    }

    public static SchoolDto ToDto(SchoolProfile s) => new(s.Name, s.ShortName, s.Motto, s.Address, s.City, s.Country, s.Phone, s.Email, s.Website,
        s.CurrencyCode, s.CurrencySymbol, s.WholeAmountsOnly, s.ReceiptFooter, s.Logo is { Length: > 0 }, s.PhoneCountryCode);

    public Task<SchoolDto> UpdateSchoolAsync(SchoolUpdate input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var v = new ValidationBuilder()
            .Require(!string.IsNullOrWhiteSpace(input.Name) && input.Name.Length <= 160, "name", "Nom de l'établissement obligatoire (160 caractères maximum).")
            .Require(!string.IsNullOrWhiteSpace(input.ShortName) && input.ShortName.Length <= 40, "shortName", "Sigle obligatoire (40 caractères maximum).")
            .Require(input.CurrencyCode is { Length: 3 } && input.CurrencyCode.All(char.IsAsciiLetterUpper), "currencyCode", "Code monnaie ISO à 3 lettres (ex. CDF).")
            .Require(!string.IsNullOrWhiteSpace(input.CurrencySymbol) && input.CurrencySymbol.Length <= 6, "currencySymbol", "Symbole monétaire obligatoire.")
            .Require(input.PhoneCountryCode is { Length: >= 1 and <= 4 } && input.PhoneCountryCode.All(char.IsDigit), "phoneCountryCode", "Indicatif téléphonique invalide.");
        v.ThrowIfInvalid();
        var s = await db.Schools.FirstAsync(ct);
        s.Name = input.Name.Trim(); s.ShortName = input.ShortName.Trim(); s.Motto = Clean(input.Motto); s.Address = Clean(input.Address);
        s.City = Clean(input.City); s.Country = Clean(input.Country); s.Phone = Clean(input.Phone); s.Email = Clean(input.Email); s.Website = Clean(input.Website);
        s.CurrencyCode = input.CurrencyCode; s.CurrencySymbol = input.CurrencySymbol.Trim(); s.WholeAmountsOnly = input.WholeAmountsOnly;
        s.ReceiptFooter = Clean(input.ReceiptFooter); s.PhoneCountryCode = input.PhoneCountryCode;
        audit.Record("etablissement.modifie", "Etablissement", s.Id.ToString(), "Paramètres de l'établissement modifiés.");
        await db.SaveChangesAsync(ct);
        return ToDto(s);
    }, ct);

    public Task SetLogoAsync(byte[]? content, string? contentType, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        if (content is { Length: > 2_000_000 }) throw new DomainException("logo", "Le logo ne doit pas dépasser 2 Mo.");
        if (content is not null && contentType is not ("image/png" or "image/jpeg")) throw new DomainException("logo", "Le logo doit être une image PNG ou JPEG.");
        var s = await db.Schools.FirstAsync(ct);
        s.Logo = content; s.LogoContentType = contentType;
        audit.Record("etablissement.logo", "Etablissement", s.Id.ToString(), content is null ? "Logo retiré." : "Logo remplacé.");
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);

    public async Task<(byte[] Content, string Type)?> GetLogoAsync(CancellationToken ct = default)
    {
        var s = await db.Schools.AsNoTracking().Select(x => new { x.Logo, x.LogoContentType }).FirstOrDefaultAsync(ct);
        return s?.Logo is { Length: > 0 } ? (s.Logo, s.LogoContentType ?? "image/png") : null;
    }

    // ---------- Années scolaires ----------
    public async Task<IReadOnlyList<AcademicYearDto>> YearsAsync(CancellationToken ct = default)
    {
        var years = await db.AcademicYears.AsNoTracking().Include(y => y.Periods).OrderByDescending(y => y.StartDate).ToListAsync(ct);
        return years.Select(ToDto).ToList();
    }

    public static AcademicYearDto ToDto(AcademicYear y) => new(y.Id, y.Label, y.StartDate, y.EndDate, y.Status.ToString(), y.IsCurrent,
        y.Periods.OrderBy(p => p.Order).Select(p => new PeriodDto(p.Id, p.Order, p.Name, p.Month, p.CalendarYear, p.DueDate)).ToList());

    public Task<AcademicYearDto> CreateYearAsync(AcademicYearCreate input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var label = AcademicYear.NormalizeLabel(input.Label);
        if (await db.AcademicYears.AnyAsync(y => y.Label == label, ct)) throw new ConflictException($"L'année {label} existe déjà.");
        var year = BuildYear(label, input.StartMonth, input.MonthCount, input.DueDay);
        var previous = await db.AcademicYears.FirstOrDefaultAsync(y => y.IsCurrent, ct);
        db.AcademicYears.Add(year);
        if (input.CopyPromotionsFromCurrent && previous is not null)
        {
            var promotions = await db.Promotions.AsNoTracking().Include(p => p.Fees).Where(p => p.AcademicYearId == previous.Id).ToListAsync(ct);
            foreach (var old in promotions)
            {
                var monthly = old.Fees.Where(f => f.Kind == FeeKind.Mensualite).Select(f => f.Amount).DefaultIfEmpty(0).Max();
                var copy = new Promotion
                {
                    AcademicYearId = year.Id, Code = old.Code, Name = old.Name, SectionId = old.SectionId, Level = old.Level, Capacity = old.Capacity,
                    IsActive = old.IsActive, NextPromotionCode = old.NextPromotionCode,
                };
                copy.Fees.AddRange(MonthlyFees(copy.Id, year, monthly));
                var shift = year.FirstCalendarYear - previous.FirstCalendarYear;
                var order = 100;
                foreach (var f in old.Fees.Where(f => f.Kind != FeeKind.Mensualite).OrderBy(f => f.Order))
                    copy.Fees.Add(new FeeItem { PromotionId = copy.Id, Kind = f.Kind, Label = f.Label, Amount = f.Amount, DueDate = f.DueDate.AddYears(shift), Order = order++ });
                db.Promotions.Add(copy);
            }
        }
        if (input.MakeCurrent || previous is null)
        {
            if (previous is not null) previous.IsCurrent = false;
            year.IsCurrent = true;
        }
        audit.Record("annee.creee", "AnneeScolaire", year.Id.ToString(), $"Année scolaire {label} créée.");
        await db.SaveChangesAsync(ct);
        return ToDto(year);
    }, ct);

    public static AcademicYear BuildYear(string label, int startMonth, int monthCount, int dueDay = 6)
    {
        var normalized = AcademicYear.NormalizeLabel(label);
        var year = new AcademicYear { Label = normalized, Status = AcademicYearStatus.Open };
        year.Periods = AcademicPeriod.Monthly(year.Id, year.FirstCalendarYear, startMonth, monthCount, dueDay);
        var first = year.Periods[0];
        var last = year.Periods[^1];
        year.StartDate = new DateOnly(first.CalendarYear, first.Month, 1);
        year.EndDate = new DateOnly(last.CalendarYear, last.Month, DateTime.DaysInMonth(last.CalendarYear, last.Month));
        return year;
    }

    public Task SetCurrentYearAsync(Guid id, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var target = await db.AcademicYears.FirstOrDefaultAsync(y => y.Id == id, ct) ?? throw new NotFoundException("Année scolaire introuvable.");
        foreach (var y in await db.AcademicYears.Where(y => y.IsCurrent).ToListAsync(ct)) y.IsCurrent = false;
        target.IsCurrent = true;
        audit.Record("annee.courante", "AnneeScolaire", id.ToString(), $"Année courante : {target.Label}.");
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);

    public Task CloseYearAsync(Guid id, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var year = await db.AcademicYears.FirstOrDefaultAsync(y => y.Id == id, ct) ?? throw new NotFoundException("Année scolaire introuvable.");
        year.Status = AcademicYearStatus.Closed;
        audit.Record("annee.cloturee", "AnneeScolaire", id.ToString(), $"Année {year.Label} clôturée : plus aucun encaissement possible.");
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);

    // ---------- Sections ----------
    public async Task<IReadOnlyList<SectionDto>> SectionsAsync(CancellationToken ct = default) =>
        await db.Sections.AsNoTracking().OrderBy(s => s.Name).Select(s => new SectionDto(s.Id, s.Code, s.Name, s.IsActive)).ToListAsync(ct);

    public Task<SectionDto> UpsertSectionAsync(Guid? id, SectionUpsert input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        new ValidationBuilder()
            .Require(!string.IsNullOrWhiteSpace(input.Code) && input.Code.Trim().Length <= 12, "code", "Code obligatoire (12 caractères maximum).")
            .Require(!string.IsNullOrWhiteSpace(input.Name) && input.Name.Trim().Length <= 120, "name", "Nom obligatoire.")
            .ThrowIfInvalid();
        var code = TextKey.Normalize(input.Code);
        if (await db.Sections.AnyAsync(s => s.Code == code && s.Id != id, ct)) throw new ConflictException("Ce code de section existe déjà.");
        var section = id is null ? new Section() : await db.Sections.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Section introuvable.");
        section.Code = code; section.Name = input.Name.Trim(); section.IsActive = input.IsActive;
        if (id is null) db.Sections.Add(section);
        audit.Record(id is null ? "section.creee" : "section.modifiee", "Section", section.Id.ToString(), $"Section {section.Name}.");
        await db.SaveChangesAsync(ct);
        return new SectionDto(section.Id, section.Code, section.Name, section.IsActive);
    }, ct);

    // ---------- Promotions ----------
    public async Task<IReadOnlyList<PromotionDto>> PromotionsAsync(Guid? yearId, CancellationToken ct = default)
    {
        var year = await YearContext.GetAsync(db, yearId, ct);
        var promotions = await db.Promotions.AsNoTracking().Include(p => p.Fees).Include(p => p.Section)
            .Where(p => p.AcademicYearId == year.Id).OrderBy(p => p.Code).ToListAsync(ct);
        var counts = await db.Enrollments.AsNoTracking().Where(e => e.AcademicYearId == year.Id && e.Status == EnrollmentStatus.Actif)
            .GroupBy(e => e.PromotionId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return promotions.Select(p => ToDto(p, counts.GetValueOrDefault(p.Id))).ToList();
    }

    public static PromotionDto ToDto(Promotion p, int count) => new(p.Id, p.AcademicYearId, p.Code, p.Name, p.SectionId, p.Section?.Name, p.Level, p.Capacity,
        p.IsActive, p.NextPromotionCode,
        p.Fees.Where(f => f.Kind == FeeKind.Mensualite).Select(f => f.Amount).DefaultIfEmpty(0).Max(),
        p.Fees.Sum(f => f.Amount), count,
        p.Fees.OrderBy(f => f.DueDate).ThenBy(f => f.Order).Select(f => new FeeItemDto(f.Id, f.Kind.ToString(), f.Label, f.Amount, f.DueDate, f.Order, f.PeriodId)).ToList());

    public static IEnumerable<FeeItem> MonthlyFees(Guid promotionId, AcademicYear year, decimal monthly) =>
        year.Periods.OrderBy(p => p.Order).Select(p => new FeeItem
        {
            PromotionId = promotionId, PeriodId = p.Id, Kind = FeeKind.Mensualite, Label = $"{p.Name} {p.CalendarYear}",
            Amount = monthly, DueDate = p.DueDate, Order = p.Order,
        });

    public Task<PromotionDto> UpsertPromotionAsync(Guid? id, PromotionUpsert input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var school = await YearContext.SchoolAsync(db, ct);
        var code = Promotion.NormalizeCode(input.Code);
        var v = new ValidationBuilder()
            .Require(code.Length == 4 && code.All(char.IsAsciiLetterOrDigit), "code", "Le code doit compter 4 lettres ou chiffres (ex. INF1) : il préfixe les matricules.")
            .Require(!string.IsNullOrWhiteSpace(input.Name) && input.Name.Trim().Length <= 120, "name", "Désignation obligatoire.")
            .Require(input.MonthlyFee >= 0, "monthlyFee", "La mensualité ne peut pas être négative.")
            .Require(!school.WholeAmountsOnly || input.MonthlyFee == decimal.Truncate(input.MonthlyFee), "monthlyFee", "La mensualité doit être un nombre entier.")
            .Require(input.Capacity is null or > 0, "capacity", "La capacité doit être positive.");
        foreach (var fee in input.OneTimeFees ?? [])
        {
            v.Require(!string.IsNullOrWhiteSpace(fee.Label), "oneTimeFees", "Chaque frais ponctuel doit avoir un libellé.");
            v.Require(fee.Amount > 0, "oneTimeFees", $"Montant invalide pour {fee.Label}.");
        }
        v.ThrowIfInvalid();

        Promotion promotion;
        AcademicYear year;
        if (id is null)
        {
            year = await YearContext.CurrentAsync(db, ct);
            if (await db.Promotions.AnyAsync(p => p.AcademicYearId == year.Id && p.Code == code, ct)) throw new ConflictException($"La promotion {code} existe déjà pour {year.Label}.");
            promotion = new Promotion { AcademicYearId = year.Id };
            db.Promotions.Add(promotion);
        }
        else
        {
            promotion = await db.Promotions.Include(p => p.Fees).FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Promotion introuvable.");
            year = await YearContext.GetAsync(db, promotion.AcademicYearId, ct);
            if (promotion.Code != code)
            {
                var used = await db.Enrollments.AnyAsync(e => e.PromotionId == promotion.Id, ct);
                if (used) throw new DomainException("code", "Le code d'une promotion qui a des inscrits ne peut plus changer (il figure dans les matricules).");
                if (await db.Promotions.AnyAsync(p => p.AcademicYearId == year.Id && p.Code == code && p.Id != promotion.Id, ct)) throw new ConflictException("Ce code existe déjà.");
            }
        }
        promotion.Code = code; promotion.Name = input.Name.Trim(); promotion.SectionId = input.SectionId; promotion.Level = input.Level;
        promotion.Capacity = input.Capacity; promotion.IsActive = input.IsActive;
        promotion.NextPromotionCode = string.IsNullOrWhiteSpace(input.NextPromotionCode) ? null : Promotion.NormalizeCode(input.NextPromotionCode);

        // Montants déjà encaissés par rubrique : une rubrique ne peut pas descendre sous ce qui a été payé.
        var paidByItem = await PaidByItemAsync(promotion.Fees.Select(f => f.Id).ToList(), ct);

        // Mensualités : une rubrique par période.
        foreach (var period in year.Periods)
        {
            var existing = promotion.Fees.FirstOrDefault(f => f.Kind == FeeKind.Mensualite && f.PeriodId == period.Id);
            if (existing is null)
            {
                if (input.MonthlyFee > 0)
                {
                    var created = MonthlyFees(promotion.Id, year, input.MonthlyFee).First(f => f.PeriodId == period.Id);
                    promotion.Fees.Add(created);
                    db.FeeItems.Add(created);
                }
                continue;
            }
            EnsureNotBelowPaid(existing, input.MonthlyFee, paidByItem);
            existing.Amount = input.MonthlyFee;
        }

        // Frais ponctuels : remplacés par la nouvelle liste (rapprochement par nature + libellé).
        var wanted = (input.OneTimeFees ?? []).Select(f => (Kind: EnumText.Parse<FeeKind>(f.Kind, "oneTimeFees"), Label: f.Label.Trim(), f.Amount, f.DueDate)).ToList();
        var oneTime = promotion.Fees.Where(f => f.Kind != FeeKind.Mensualite).ToList();
        foreach (var old in oneTime)
        {
            var match = wanted.FirstOrDefault(w => w.Kind == old.Kind && TextKey.Normalize(w.Label) == TextKey.Normalize(old.Label));
            if (match.Label is null)
            {
                if (paidByItem.GetValueOrDefault(old.Id) > 0) throw new DomainException("oneTimeFees", $"« {old.Label} » a déjà été encaissé et ne peut pas être supprimé.");
                promotion.Fees.Remove(old);
                db.FeeItems.Remove(old);
                continue;
            }
            EnsureNotBelowPaid(old, match.Amount, paidByItem);
            old.Amount = match.Amount; old.DueDate = match.DueDate;
            wanted.Remove(match);
        }
        var order = 100 + promotion.Fees.Count;
        foreach (var fee in wanted)
        {
            var item = new FeeItem { PromotionId = promotion.Id, Kind = fee.Kind, Label = fee.Label, Amount = fee.Amount, DueDate = fee.DueDate, Order = order++ };
            promotion.Fees.Add(item);
            db.FeeItems.Add(item);
        }
        audit.Record(id is null ? "promotion.creee" : "promotion.modifiee", "Promotion", promotion.Id.ToString(),
            $"Promotion {promotion.Code} ({year.Label}) : mensualité {Money.Display(input.MonthlyFee, school.CurrencySymbol)}.");
        await db.SaveChangesAsync(ct);
        var count = await db.Enrollments.CountAsync(e => e.PromotionId == promotion.Id && e.Status == EnrollmentStatus.Actif, ct);
        if (promotion.SectionId is not null) promotion.Section = await db.Sections.FindAsync([promotion.SectionId], ct);
        return ToDto(promotion, count);
    }, ct);

    private static void EnsureNotBelowPaid(FeeItem item, decimal newAmount, IReadOnlyDictionary<Guid, decimal> paidByItem)
    {
        var maxPaid = paidByItem.GetValueOrDefault(item.Id);
        if (newAmount < maxPaid)
            throw new DomainException("montant", $"« {item.Label} » : un apprenant a déjà versé {maxPaid:N0}. Le montant ne peut pas être inférieur.");
    }

    /// <summary>Plus grand montant versé par une même inscription sur chaque rubrique.</summary>
    private async Task<Dictionary<Guid, decimal>> PaidByItemAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0) return [];
        var rows = await (from a in db.ReceiptAllocations.AsNoTracking()
                          join r in db.Receipts.AsNoTracking() on a.ReceiptId equals r.Id
                          where itemIds.Contains(a.FeeItemId) && r.Status == OperationStatus.Valide
                          select new { a.FeeItemId, r.EnrollmentId, a.Amount }).ToListAsync(ct);
        return rows.GroupBy(r => r.FeeItemId).ToDictionary(g => g.Key, g => g.GroupBy(x => x.EnrollmentId).Max(e => e.Sum(x => x.Amount)));
    }

    public Task DeletePromotionAsync(Guid id, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var promotion = await db.Promotions.Include(p => p.Fees).FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Promotion introuvable.");
        if (await db.Enrollments.AnyAsync(e => e.PromotionId == id, ct)) throw new DomainException("promotion", "Cette promotion a des inscrits : désactivez-la au lieu de la supprimer.");
        db.FeeItems.RemoveRange(promotion.Fees);
        db.Promotions.Remove(promotion);
        audit.Record("promotion.supprimee", "Promotion", id.ToString(), $"Promotion {promotion.Code} supprimée.");
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
