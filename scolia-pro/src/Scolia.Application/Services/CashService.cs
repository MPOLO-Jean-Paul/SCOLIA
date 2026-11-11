using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.Finance;
using Scolia.Domain.Security;

namespace Scolia.Application.Services;

/// <summary>Dépenses, journal de caisse et arrêtés de caisse.</summary>
public sealed class CashService(IAppDbContext db, IWriteGate gate, IAuditLog audit, IClock clock, ICurrentUser user)
{
    public static readonly string[] DefaultCategories =
        ["Salaires", "Fournitures", "Entretien et réparations", "Électricité et eau", "Communication", "Transport", "Loyer", "Frais bancaires", "Impôts et taxes", "Divers"];

    public async Task<IReadOnlyList<string>> CategoriesAsync(CancellationToken ct = default)
    {
        var used = await db.Expenses.AsNoTracking().Select(e => e.Category).Distinct().ToListAsync(ct);
        return DefaultCategories.Concat(used).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c).ToList();
    }

    public async Task<PagedResult<ExpenseDto>> ExpensesAsync(DateOnly? from, DateOnly? to, string? category, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var q = db.Expenses.AsNoTracking().AsQueryable();
        if (from is not null) q = q.Where(e => e.Date >= from);
        if (to is not null) q = q.Where(e => e.Date <= to);
        if (!string.IsNullOrWhiteSpace(category)) q = q.Where(e => e.Category == category);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(e => e.Description.Contains(search) || e.Beneficiary.Contains(search) || e.Number.Contains(search));
        var total = await q.CountAsync(ct);
        var sum = (await q.Where(e => e.Status == OperationStatus.Valide).Select(e => e.Amount).ToListAsync(ct)).Sum();
        pageSize = Math.Clamp(pageSize, 1, 1000);
        page = Math.Max(1, page);
        var list = await q.OrderByDescending(e => e.Date).ThenByDescending(e => e.Number).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<ExpenseDto>(list.Select(ToDto).ToList(), total, page, pageSize, sum);
    }

    private static ExpenseDto ToDto(Expense e) => new(e.Id, e.Number, e.Date, e.Category, e.Beneficiary, e.Description, e.Amount, Labels.Of(e.Method),
        e.Reference, e.RecordedByName, e.Status == OperationStatus.Valide ? "Valide" : "Annulé", e.CancellationReason);

    public Task<ExpenseDto> CreateExpenseAsync(ExpenseCreate input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var school = await YearContext.SchoolAsync(db, ct);
        Money.EnsureValid(input.Amount, school.WholeAmountsOnly, "amount");
        var method = PaymentService.ParseMethod(input.Method);
        var date = input.Date ?? clock.Today;
        new ValidationBuilder()
            .Require(!string.IsNullOrWhiteSpace(input.Category) && input.Category.Length <= 80, "category", "Choisissez une catégorie.")
            .Require(!string.IsNullOrWhiteSpace(input.Beneficiary) && input.Beneficiary.Length <= 160, "beneficiary", "Indiquez le bénéficiaire.")
            .Require(!string.IsNullOrWhiteSpace(input.Description) && input.Description.Length <= 500, "description", "Décrivez la dépense.")
            .Require(date <= clock.Today, "date", "La date ne peut pas être dans le futur.")
            .ThrowIfInvalid();
        var lastClosing = await PaymentService.LastClosingAsync(db, ct);
        if (lastClosing is not null && date <= lastClosing) throw new DomainException("date", $"La caisse est arrêtée jusqu'au {lastClosing:dd/MM/yyyy}.");
        if (method == PaymentMethod.Especes)
        {
            var situation = await SituationAsync(date, ct);
            if (input.Amount > situation.ExpectedCash)
                throw new DomainException("amount", $"Espèces disponibles insuffisantes : {Money.Display(situation.ExpectedCash, school.CurrencySymbol)} en caisse.");
        }
        var year = await YearContext.CurrentAsync(db, ct);
        var expense = new Expense
        {
            Number = await PaymentService.NextNumberAsync(db, "DP", year.FirstCalendarYear, ct), Date = date, AcademicYearId = year.Id,
            Category = input.Category.Trim(), Beneficiary = input.Beneficiary.Trim(), Description = input.Description.Trim(), Amount = input.Amount,
            Method = method, Reference = string.IsNullOrWhiteSpace(input.Reference) ? null : input.Reference.Trim(), RecordedByName = user.Name,
        };
        db.Expenses.Add(expense);
        audit.Record("depense.creee", "Depense", expense.Id.ToString(), $"Dépense {expense.Number} : {Money.Display(expense.Amount, school.CurrencySymbol)} ({expense.Category}).");
        await db.SaveChangesAsync(ct);
        return ToDto(expense);
    }, ct);

    public Task<ExpenseDto> CancelExpenseAsync(Guid id, string reason, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5) throw new DomainException("reason", "Le motif d'annulation est obligatoire.");
        var e = await db.Expenses.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Dépense introuvable.");
        if (e.Status == OperationStatus.Annule) throw new ConflictException("Cette dépense est déjà annulée.");
        var lastClosing = await PaymentService.LastClosingAsync(db, ct);
        if (lastClosing is not null && e.Date <= lastClosing) throw new DomainException("date", "Cette dépense appartient à une caisse arrêtée.");
        e.Status = OperationStatus.Annule; e.CancelledAtUtc = clock.UtcNow; e.CancelledBy = user.Name; e.CancellationReason = reason.Trim();
        audit.Record("depense.annulee", "Depense", id.ToString(), $"Dépense {e.Number} annulée.", new { motif = reason });
        await db.SaveChangesAsync(ct);
        return ToDto(e);
    }, ct);

    /// <summary>Situation de caisse depuis le dernier arrêté jusqu'à la date donnée.</summary>
    public async Task<CashSituationDto> SituationAsync(DateOnly? date, CancellationToken ct = default)
    {
        var day = date ?? clock.Today;
        var last = await db.CashClosings.AsNoTracking().OrderByDescending(c => c.Date).FirstOrDefaultAsync(ct);
        var start = last is null ? DateOnly.MinValue : last.Date.AddDays(1);
        var opening = last?.CountedCash ?? 0m;
        var receipts = await (from r in db.Receipts.AsNoTracking()
                              join s in db.Students.AsNoTracking() on r.StudentId equals s.Id
                              where r.Status == OperationStatus.Valide && r.Date >= start && r.Date <= day
                              select new { r.Date, r.Number, Label = s.Matricule + " " + s.LastName + " " + s.FirstName, r.Method, r.Amount }).ToListAsync(ct);
        var expenses = await db.Expenses.AsNoTracking().Where(e => e.Status == OperationStatus.Valide && e.Date >= start && e.Date <= day)
            .Select(e => new { e.Date, e.Number, Label = e.Category + " · " + e.Beneficiary, e.Method, e.Amount }).ToListAsync(ct);
        var cashIn = receipts.Where(r => r.Method == PaymentMethod.Especes).Sum(r => r.Amount);
        var cashOut = expenses.Where(e => e.Method == PaymentMethod.Especes).Sum(e => e.Amount);
        var movements = receipts.Select(r => new CashMovementDto(r.Date, "Recette", r.Number, r.Label, Labels.Of(r.Method), r.Amount, 0))
            .Concat(expenses.Select(e => new CashMovementDto(e.Date, "Dépense", e.Number, e.Label, Labels.Of(e.Method), 0, e.Amount)))
            .OrderBy(m => m.Date).ThenBy(m => m.Number).ToList();
        return new CashSituationDto(last is null ? (movements.Count > 0 ? movements[0].Date : day) : start, day, opening, cashIn, cashOut,
            opening + cashIn - cashOut, receipts.Where(r => r.Method != PaymentMethod.Especes).Sum(r => r.Amount), last?.Date, movements);
    }

    public Task<CashClosingDto> CloseAsync(CashClosingRequest input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        if (!user.HasPermission(Permissions.CashClose)) throw new DomainException("droits", "Vous n'êtes pas autorisé à arrêter la caisse.");
        if (input.Date > clock.Today) throw new DomainException("date", "Impossible d'arrêter la caisse à une date future.");
        if (input.CountedCash < 0) throw new DomainException("countedCash", "Le montant compté ne peut pas être négatif.");
        var situation = await SituationAsync(input.Date, ct);
        if (situation.LastClosingDate is not null && input.Date <= situation.LastClosingDate)
            throw new DomainException("date", $"La caisse est déjà arrêtée au {situation.LastClosingDate:dd/MM/yyyy}.");
        var difference = input.CountedCash - situation.ExpectedCash;
        if (difference != 0 && string.IsNullOrWhiteSpace(input.Note))
            throw new DomainException("note", "Un écart existe entre l'espèce comptée et l'espèce attendue : expliquez-le.");
        var closing = new CashClosing
        {
            Date = input.Date, PeriodStart = situation.PeriodStart, OpeningBalance = situation.OpeningBalance, CashReceipts = situation.CashReceipts,
            CashExpenses = situation.CashExpenses, ExpectedCash = situation.ExpectedCash, CountedCash = input.CountedCash, Difference = difference,
            Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(), ClosedByName = user.Name,
        };
        db.CashClosings.Add(closing);
        audit.Record("caisse.arretee", "ArreteCaisse", closing.Id.ToString(),
            $"Caisse arrêtée au {input.Date:dd/MM/yyyy} : attendu {situation.ExpectedCash:N0}, compté {input.CountedCash:N0}, écart {difference:N0}.");
        await db.SaveChangesAsync(ct);
        return ToDto(closing);
    }, ct);

    public async Task<IReadOnlyList<CashClosingDto>> ClosingsAsync(CancellationToken ct = default) =>
        (await db.CashClosings.AsNoTracking().OrderByDescending(c => c.Date).Take(200).ToListAsync(ct)).Select(ToDto).ToList();

    private static CashClosingDto ToDto(CashClosing c) => new(c.Id, c.Date, c.PeriodStart, c.OpeningBalance, c.CashReceipts, c.CashExpenses, c.ExpectedCash,
        c.CountedCash, c.Difference, c.Note, c.ClosedByName, c.CreatedAtUtc);
}
