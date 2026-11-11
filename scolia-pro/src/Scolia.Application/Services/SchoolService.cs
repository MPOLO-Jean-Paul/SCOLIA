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
        s.CurrencyCode, s.CurrencySymbol, s.WholeAmountsOnly, s.ReceiptFooter, s.Logo is { Length: > 0 }, s.PhoneCountryCode,
        s.CardMinistry, s.CardDirection, s.CardDivision, s.CardZone ?? s.City,
        CardHeaderDefaults.Or(s.CardInstitute, s.Name),
        CardHeaderDefaults.Or(s.CardAddress, s.Address ?? s.City ?? string.Empty), InstitutionProfiles.Canonicalize(s.InstitutionType),
        s.Governance.ToString());

    public Task<SchoolDto> UpdateSchoolAsync(SchoolUpdate input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var v = new ValidationBuilder()
            .Require(!string.IsNullOrWhiteSpace(input.Name) && input.Name.Length <= 160, "name", "Nom de l'établissement obligatoire (160 caractères maximum).")
            .Require(!string.IsNullOrWhiteSpace(input.ShortName) && input.ShortName.Length <= 40, "shortName", "Sigle obligatoire (40 caractères maximum).")
            .Require(input.CurrencyCode is { Length: 3 } && input.CurrencyCode.All(char.IsAsciiLetterUpper), "currencyCode", "Code monnaie ISO à 3 lettres (ex. CDF).")
            .Require(!string.IsNullOrWhiteSpace(input.CurrencySymbol) && input.CurrencySymbol.Length <= 6, "currencySymbol", "Symbole monétaire obligatoire.")
            .Require(input.PhoneCountryCode is { Length: >= 1 and <= 4 } && input.PhoneCountryCode.All(char.IsDigit), "phoneCountryCode", "Indicatif téléphonique invalide.");
        v.ThrowIfInvalid();
        var s = await db.Schools.OrderBy(x => x.CreatedAtUtc).FirstAsync(ct);
        s.Name = input.Name.Trim(); s.ShortName = input.ShortName.Trim(); s.Motto = Clean(input.Motto); s.Address = Clean(input.Address);
        s.City = Clean(input.City); s.Country = Clean(input.Country); s.Phone = IdentityRules.NormalizePhone(input.Phone); s.Email = Clean(input.Email); s.Website = Clean(input.Website);
        s.CurrencyCode = input.CurrencyCode; s.CurrencySymbol = input.CurrencySymbol.Trim(); s.WholeAmountsOnly = input.WholeAmountsOnly;
        s.ReceiptFooter = Clean(input.ReceiptFooter); s.PhoneCountryCode = input.PhoneCountryCode;
        s.CardMinistry = Clean(input.CardMinistry); s.CardDirection = Clean(input.CardDirection); s.CardDivision = Clean(input.CardDivision); s.CardZone = Clean(input.CardZone);
        s.CardInstitute = Clean(input.CardInstitute); s.CardAddress = Clean(input.CardAddress);
        audit.Record("etablissement.modifie", "Etablissement", s.Id.ToString(), "Paramètres de l'établissement modifiés.");
        await db.SaveChangesAsync(ct);
        return ToDto(s);
    }, ct);

    public Task SetLogoAsync(byte[]? content, string? contentType, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        if (content is { Length: > 2_000_000 }) throw new DomainException("logo", "Le logo ne doit pas dépasser 2 Mo.");
        // Le type est lu dans le fichier lui-même : un envoi sans type déclaré, ou mal déclaré, reste accepté.
        // Les applications SCOLIA convertissent déjà toute image (GIF, WebP, SVG, HEIC…) en PNG avant l'envoi.
        if (content is not null)
        {
            contentType = ImageKind.Detect(content);
            if (contentType is not ("image/png" or "image/jpeg"))
                throw new DomainException("logo", "Ce fichier n'est pas une image lisible. Choisissez le logo depuis l'application SCOLIA : elle convertit toute image courante.");
        }
        var s = await db.Schools.OrderBy(x => x.CreatedAtUtc).FirstAsync(ct);
        s.Logo = content; s.LogoContentType = contentType;
        audit.Record("etablissement.logo", "Etablissement", s.Id.ToString(), content is null ? "Logo retiré." : "Logo remplacé.");
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);

    public async Task<(byte[] Content, string Type)?> GetLogoAsync(CancellationToken ct = default)
    {
        var s = await db.Schools.AsNoTracking().OrderBy(x => x.CreatedAtUtc).Select(x => new { x.Logo, x.LogoContentType }).FirstOrDefaultAsync(ct);
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
        // Les anciennes intégrations peuvent encore envoyer dueDay (premier jour de retard).
        // Les nouvelles précisent explicitement le mois de fin et le DERNIER jour autorisé.
        var count = input.EndMonth is { } endMonth
            ? AcademicPeriod.CountBetween(input.StartMonth, endMonth)
            : input.MonthCount;
        var firstLateDay = input.LastOnTimeDay is { } cutoff
            ? ValidatedLastOnTimeDay(cutoff) + 1 : input.DueDay;
        var year = BuildYear(label, input.StartMonth, count, firstLateDay);
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
                    AcademicYearId = year.Id, Code = old.Code, Name = old.Name, SectionId = old.SectionId, SchoolCycleId = old.SchoolCycleId,
                    Level = old.Level, Capacity = old.Capacity,
                    IsActive = old.IsActive, NextPromotionCode = old.NextPromotionCode,
                };
                copy.Fees.AddRange(MonthlyFees(copy.Id, year, monthly));
                var shift = year.FirstCalendarYear - previous.FirstCalendarYear;
                var order = 100;
                foreach (var f in old.Fees.Where(f => f.Kind != FeeKind.Mensualite).OrderBy(f => f.Order))
                    copy.Fees.Add(new FeeItem { PromotionId = copy.Id, Kind = f.Kind, CategoryId = f.CategoryId, Label = f.Label, Amount = f.Amount, DueDate = f.DueDate.AddYears(shift), Order = order++ });
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

    /// <summary>La date limite est comprise entre le 1er et le 28 du mois suivant.</summary>
    private static int ValidatedLastOnTimeDay(int day)
    {
        if (day is < 1 or > 28)
            throw new DomainException("echeance", "Le dernier jour pour payer sans retard doit être compris entre 1 et 28.");
        return day;
    }

    /// <summary>
    /// Modifie les mois de paiement et leur échéance sur une année EXISTANTE, sans recréer l'année.
    /// Toute mensualité déjà payée ou prise en charge garde son mois, son identifiant et son échéance.
    /// Il reste possible d'ajouter des mois à la fin ou de changer les dates des mois non affectés,
    /// mais jamais de renommer ou supprimer rétroactivement une mensualité comptabilisée.
    /// </summary>
    public Task<AcademicYearDto> UpdateYearScheduleAsync(Guid id, AcademicYearScheduleUpdate input, CancellationToken ct = default) =>
        gate.RunAsync(async () =>
        {
            var year = await db.AcademicYears.Include(y => y.Periods)
                .FirstOrDefaultAsync(y => y.Id == id, ct)
                ?? throw new NotFoundException("Année scolaire introuvable.");
            if (year.Status == AcademicYearStatus.Closed)
                throw new DomainException("annee", "Cette année est clôturée : son calendrier ne peut plus être modifié.");

            var lastOnTimeDay = ValidatedLastOnTimeDay(input.LastOnTimeDay);
            var count = AcademicPeriod.CountBetween(input.StartMonth, input.EndMonth);
            var proposed = AcademicPeriod.Monthly(year.Id, year.FirstCalendarYear, input.StartMonth, count, lastOnTimeDay + 1);
            var previous = year.Periods.OrderBy(p => p.Order).ToList();
            if (previous.Count == proposed.Count && previous.Zip(proposed)
                    .All(pair => pair.First.Month == pair.Second.Month &&
                                 pair.First.CalendarYear == pair.Second.CalendarYear &&
                                 pair.First.DueDate == pair.Second.DueDate))
                return ToDto(year);

            var promotions = await db.Promotions.Include(p => p.Fees)
                .Where(p => p.AcademicYearId == id).ToListAsync(ct);
            var monthlyFees = promotions.SelectMany(p => p.Fees)
                .Where(f => f.Kind == FeeKind.Mensualite).ToList();
            var feeIds = monthlyFees.Select(f => f.Id).ToList();
            var protectedFees = new HashSet<Guid>();
            if (feeIds.Count > 0)
            {
                protectedFees.UnionWith(await db.ReceiptAllocations
                    .Where(a => feeIds.Contains(a.FeeItemId))
                    .Select(a => a.FeeItemId).Distinct().ToListAsync(ct));
                protectedFees.UnionWith(await db.SponsorshipLines
                    .Where(a => feeIds.Contains(a.FeeItemId))
                    .Select(a => a.FeeItemId).Distinct().ToListAsync(ct));
            }
            var protectedPeriodIds = monthlyFees
                .Where(f => protectedFees.Contains(f.Id) && f.PeriodId.HasValue)
                .Select(f => f.PeriodId!.Value).ToHashSet();

            // Les mois associés à une écriture financière doivent rester à leur place.
            for (var i = 0; i < previous.Count; i++)
            {
                if (!protectedPeriodIds.Contains(previous[i].Id)) continue;
                if (i >= proposed.Count || previous[i].Month != proposed[i].Month ||
                    previous[i].CalendarYear != proposed[i].CalendarYear)
                    throw new DomainException("periodes",
                        $"Le mois de {previous[i].Name} {previous[i].CalendarYear} possède déjà un paiement ou une prise en charge. " +
                        "Il ne peut pas être déplacé ou supprimé. Conservez les mois payés et ajustez uniquement les mois suivants.");
            }

            // Réutiliser les périodes présentes pour préserver les clés et les liens des frais.
            var ordered = new List<AcademicPeriod>(proposed.Count);
            for (var i = 0; i < proposed.Count; i++)
            {
                var generated = proposed[i];
                var period = i < previous.Count ? previous[i] : new AcademicPeriod { AcademicYearId = year.Id };
                period.Order = i + 1;
                period.Name = generated.Name;
                period.Month = generated.Month;
                period.CalendarYear = generated.CalendarYear;
                // L'échéance historique d'un mois déjà comptabilisé reste identique.
                if (!protectedPeriodIds.Contains(period.Id))
                    period.DueDate = generated.DueDate;
                if (i >= previous.Count) year.Periods.Add(period);
                ordered.Add(period);
            }

            foreach (var promotion in promotions)
            {
                var oldFees = promotion.Fees.Where(f => f.Kind == FeeKind.Mensualite).ToList();
                var monthlyAmount = oldFees.Select(f => f.Amount).DefaultIfEmpty(0m).Max();
                foreach (var period in ordered)
                {
                    var fee = oldFees.FirstOrDefault(f => f.PeriodId == period.Id);
                    if (fee is null && monthlyAmount > 0)
                    {
                        fee = new FeeItem { PromotionId = promotion.Id, PeriodId = period.Id, Kind = FeeKind.Mensualite };
                        promotion.Fees.Add(fee);
                    }
                    if (fee is null) continue;
                    fee.Label = $"{period.Name} {period.CalendarYear}";
                    fee.DueDate = period.DueDate;
                    fee.Order = period.Order;
                }
                var validPeriodIds = ordered.Select(p => p.Id).ToHashSet();
                foreach (var fee in oldFees.Where(f => f.PeriodId is not { } periodId || !validPeriodIds.Contains(periodId)))
                {
                    promotion.Fees.Remove(fee);
                    db.FeeItems.Remove(fee);
                }
            }
            foreach (var removed in previous.Skip(ordered.Count))
            {
                year.Periods.Remove(removed);
                db.Periods.Remove(removed);
            }

            var first = ordered[0];
            var last = ordered[^1];
            year.StartDate = new DateOnly(first.CalendarYear, first.Month, 1);
            year.EndDate = new DateOnly(last.CalendarYear, last.Month,
                DateTime.DaysInMonth(last.CalendarYear, last.Month));
            audit.Record("annee.calendrier.modifie", "AnneeScolaire", year.Id.ToString(),
                $"Année {year.Label} : {first.Name} {first.CalendarYear} à {last.Name} {last.CalendarYear}, " +
                $"limite le {lastOnTimeDay} du mois suivant pour les mois non encore comptabilisés ; " +
                $"{protectedPeriodIds.Count} échéance(s) historique(s) conservée(s).");
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
    /// <summary>Sections avec, pour l'année courante, leurs promotions et leurs apprenants actifs.</summary>
    public async Task<IReadOnlyList<SectionDto>> SectionsAsync(CancellationToken ct = default)
    {
        var sections = await db.Sections.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        var year = await db.AcademicYears.AsNoTracking().Where(y => y.IsCurrent).Select(y => (Guid?)y.Id).FirstOrDefaultAsync(ct);
        var promotions = year is null ? [] : await db.Promotions.AsNoTracking()
            .Where(p => p.AcademicYearId == year && p.SectionId != null).Select(p => new { p.Id, p.SectionId }).ToListAsync(ct);
        var promotionIds = promotions.Select(p => p.Id).ToList();
        var students = promotionIds.Count == 0 ? [] : await db.Enrollments.AsNoTracking()
            .Where(e => promotionIds.Contains(e.PromotionId) && e.Status == EnrollmentStatus.Actif)
            .GroupBy(e => e.PromotionId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return sections.Select(s =>
        {
            var mine = promotions.Where(p => p.SectionId == s.Id).Select(p => p.Id).ToList();
            return new SectionDto(s.Id, s.Code, s.Name, s.IsActive, mine.Count, mine.Sum(id => students.GetValueOrDefault(id)));
        }).ToList();
    }

    /// <summary>Supprime une section qu'aucune promotion, de n'importe quelle année, n'utilise ; sinon la désactiver.</summary>
    public Task DeleteSectionAsync(Guid id, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var section = await db.Sections.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Section introuvable.");
        var used = await db.Promotions.CountAsync(p => p.SectionId == id, ct);
        if (used > 0)
            throw new DomainException("section", $"La section {section.Name} regroupe {used} promotion(s), y compris des années passées : désactivez-la au lieu de la supprimer.");
        db.Sections.Remove(section);
        audit.Record("section.supprimee", "Section", id.ToString(), $"Section {section.Name} supprimée.");
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);

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
        return (await SectionsAsync(ct)).First(s => s.Id == section.Id);
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
        p.Fees.OrderBy(f => f.DueDate).ThenBy(f => f.Order).Select(f => new FeeItemDto(f.Id, f.Kind.ToString(), f.Label, f.Amount, f.DueDate, f.Order, f.PeriodId, f.CategoryId)).ToList(),
        p.SchoolCycleId);

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
        if (InstitutionProfiles.Canonicalize(school.InstitutionType) == InstitutionProfiles.ItmIm && input.SchoolCycleId is not null)
            throw new DomainException("schoolCycleId", "Les classes ITM/IM restent indépendantes des nouveaux cycles scolaires.");
        if (input.SchoolCycleId is { } selectedCycle && !await db.SchoolCycles.AnyAsync(c => c.Id == selectedCycle && c.SchoolId == school.Id && c.IsActive, ct))
            throw new DomainException("schoolCycleId", "Le cycle choisi est introuvable ou inactif dans cet établissement.");
        var v = new ValidationBuilder()
            .Require(code.Length == 4 && code.All(char.IsAsciiLetterOrDigit), "code", "Le code doit compter 4 lettres ou chiffres (ex. INF1) : il préfixe les matricules.")
            .Require(!string.IsNullOrWhiteSpace(input.Name) && input.Name.Trim().Length <= 120, "name", "Désignation obligatoire.")
            .Require(input.MonthlyFee > 0, "monthlyFee", "La mensualité est obligatoire : indiquez son montant (supérieur à zéro).")
            .Require(!school.WholeAmountsOnly || input.MonthlyFee == decimal.Truncate(input.MonthlyFee), "monthlyFee", "La mensualité doit être un nombre entier.")
            .Require(input.Capacity is null or > 0, "capacity", "La capacité doit être positive.");
        foreach (var fee in input.OneTimeFees ?? [])
        {
            v.Require(fee.CategoryId is not null || !string.IsNullOrWhiteSpace(fee.Label), "oneTimeFees", "Chaque frais doit désigner une catégorie.");
            v.Require(fee.Amount > 0, "oneTimeFees", $"Montant invalide pour {fee.Label ?? "une catégorie"}.");
            v.Require(!school.WholeAmountsOnly || fee.Amount == decimal.Truncate(fee.Amount), "oneTimeFees", "Les montants doivent être des nombres entiers.");
        }
        v.ThrowIfInvalid();
        // Seules les catégories créées dans le catalogue peuvent figurer dans une promotion.
        var categories = await db.FeeCategories.AsNoTracking().ToListAsync(ct);
        var wanted = new List<(FeeCategory Category, FeeKind Kind, decimal Amount, DateOnly? DueDate)>();
        foreach (var fee in input.OneTimeFees ?? [])
        {
            var category = fee.CategoryId is { } categoryId
                ? categories.FirstOrDefault(c => c.Id == categoryId)
                    ?? throw new DomainException("oneTimeFees", "Une catégorie de frais choisie n'existe plus : actualisez la liste.")
                : categories.FirstOrDefault(c => c.NameKey == TextKey.Normalize(fee.Label))
                    ?? throw new DomainException("oneTimeFees", $"Créez d'abord la catégorie « {fee.Label!.Trim()} » dans Paramètres, Catégories de frais.");
            if (wanted.Any(w => w.Category.Id == category.Id))
                throw new DomainException("oneTimeFees", $"« {category.Name} » est indiquée deux fois.");
            var kind = fee.Kind is { Length: > 0 } k && EnumText.Parse<FeeKind>(k, "oneTimeFees") is var parsed && parsed != FeeKind.Mensualite ? parsed : FeeKind.Autre;
            wanted.Add((category, kind, fee.Amount, fee.DueDate));
        }

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
        // Section : elle doit exister ; une section désactivée ne reçoit plus de nouvelle promotion (l'ancienne reste).
        if (input.SectionId is { } sectionId && sectionId != promotion.SectionId)
        {
            var chosen = await db.Sections.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sectionId, ct)
                ?? throw new DomainException("sectionId", "La section choisie n'existe plus : actualisez la liste.");
            if (!chosen.IsActive) throw new DomainException("sectionId", $"La section {chosen.Name} est désactivée : réactivez-la ou choisissez-en une autre.");
        }
        promotion.Code = code; promotion.Name = input.Name.Trim(); promotion.SectionId = input.SectionId; promotion.Level = input.Level;
        if (input.SchoolCycleId is not null) promotion.SchoolCycleId = input.SchoolCycleId;
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

        // Autres frais : une rubrique par catégorie choisie. Un client récent envoie la liste complète des catégories
        // (ManagesCategories) ; un ancien client ne connaît que l'inscription et l'examen : les autres catégories restent.
        var managed = promotion.Fees.Where(f => f.Kind != FeeKind.Mensualite
            && (input.ManagesCategories || f.Kind is FeeKind.Inscription or FeeKind.Examen)).ToList();
        foreach (var old in managed)
        {
            var match = wanted.FirstOrDefault(w => old.CategoryId == w.Category.Id
                || (old.CategoryId is null && TextKey.Normalize(old.Label) == w.Category.NameKey));
            if (match.Category is null)
            {
                if (paidByItem.GetValueOrDefault(old.Id) > 0) throw new DomainException("oneTimeFees", $"« {old.Label} » a déjà été encaissé et ne peut pas être retiré.");
                if (await db.SponsorshipLines.AnyAsync(l => l.FeeItemId == old.Id, ct))
                    throw new DomainException("oneTimeFees", $"« {old.Label} » figure dans une prise en charge et ne peut pas être retiré.");
                promotion.Fees.Remove(old);
                db.FeeItems.Remove(old);
                continue;
            }
            EnsureNotBelowPaid(old, match.Amount, paidByItem);
            old.Amount = match.Amount;
            old.CategoryId = match.Category.Id;
            old.Label = match.Category.Name;
            old.DueDate = match.DueDate ?? FeeCategoryService.DueDateIn(match.Category, year);
            wanted.Remove(match);
        }
        // Une catégorie déjà présente (laissée de côté par un ancien client) n'est pas ajoutée deux fois.
        wanted.RemoveAll(w => promotion.Fees.Any(f => f.CategoryId == w.Category.Id));
        var order = 100 + promotion.Fees.Count;
        foreach (var fee in wanted)
        {
            var item = new FeeItem
            {
                PromotionId = promotion.Id, Kind = fee.Kind, CategoryId = fee.Category.Id, Label = fee.Category.Name, Amount = fee.Amount,
                DueDate = fee.DueDate ?? FeeCategoryService.DueDateIn(fee.Category, year), Order = order++,
            };
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
        // Une délibération tenue (procès-verbal, décisions) ou des cours enregistrés font partie de l'historique officiel.
        if (await db.DeliberationSessions.AnyAsync(x => x.PromotionId == id, ct))
            throw new DomainException("promotion", "Cette promotion a une délibération enregistrée : désactivez-la au lieu de la supprimer.");
        if (await db.Courses.AnyAsync(c => c.PromotionId == id, ct))
            throw new DomainException("promotion", "Cette promotion a des cours : archivez-les ou désactivez la promotion au lieu de la supprimer.");
        db.FeeItems.RemoveRange(promotion.Fees);
        db.Promotions.Remove(promotion);
        audit.Record("promotion.supprimee", "Promotion", id.ToString(), $"Promotion {promotion.Code} supprimée.");
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
