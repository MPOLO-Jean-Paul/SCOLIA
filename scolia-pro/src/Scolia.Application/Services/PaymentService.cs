using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Common;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.Finance;
using Scolia.Domain.School;
using Scolia.Domain.Security;

namespace Scolia.Application.Services;

/// <summary>Encaissement, reçus, annulations et vérification d'authenticité.</summary>
public sealed class PaymentService(IAppDbContext db, IWriteGate gate, IAuditLog audit, IClock clock, ICurrentUser user, AccountService accounts, IDocumentService documents)
{
    private static readonly (PaymentMethod, string)[] MethodLabels =
        [(PaymentMethod.Especes, "Espèces"), (PaymentMethod.Especes, "Cash"), (PaymentMethod.MobileMoney, "Mobile Money"), (PaymentMethod.Cheque, "Chèque")];

    public static PaymentMethod ParseMethod(string? value) => EnumText.Parse(value, "method", MethodLabels);

    /// <summary>Aperçu d'un règlement : les mois choisis. Sans mois choisi, aucune répartition n'est proposée.</summary>
    public async Task<PaymentPreview> PreviewAsync(Guid enrollmentId, IReadOnlyList<Guid>? items, CancellationToken ct = default)
    {
        var data = await accounts.ForEnrollmentAsync(enrollmentId, null, ct);
        if (items is not { Count: > 0 }) return new PaymentPreview([], data.Account.Balance);
        return BuildPreview(data.Account, PaymentAllocator.Months(data.Account, items));
    }

    private static PaymentPreview BuildPreview(StudentAccount account, IReadOnlyList<(Guid FeeItemId, decimal Amount)> plan)
    {
        var allocations = plan.Select(p =>
        {
            var line = account.Lines.First(l => l.FeeItemId == p.FeeItemId);
            return new AllocationPreview(p.FeeItemId, line.Label, p.Amount, line.Remaining - p.Amount);
        }).ToList();
        return new PaymentPreview(allocations, account.Balance - plan.Sum(p => p.Amount));
    }

    public static async Task<DateOnly?> LastClosingAsync(IAppDbContext db, CancellationToken ct) =>
        await db.CashClosings.AsNoTracking().OrderByDescending(c => c.Date).Select(c => (DateOnly?)c.Date).FirstOrDefaultAsync(ct);

    public static async Task<string> NextNumberAsync(IAppDbContext db, string prefix, int year, CancellationToken ct)
    {
        var name = $"{prefix}-{year}";
        var counter = await db.Counters.FirstOrDefaultAsync(c => c.Name == name, ct);
        if (counter is null)
        {
            counter = new Counter { Name = name };
            db.Counters.Add(counter);
        }
        counter.Value++;
        return DocumentNumbers.Format(prefix, year, counter.Value);
    }

    public Task<ReceiptDto> CreateAsync(PaymentRequest input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        if (!string.IsNullOrWhiteSpace(input.IdempotencyKey))
        {
            var existing = await db.Receipts.AsNoTracking().Where(r => r.IdempotencyKey == input.IdempotencyKey).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
            if (existing is not null) return await GetAsync(existing.Value, ct);
        }
        var school = await YearContext.SchoolAsync(db, ct);
        var method = ParseMethod(input.Method);
        var date = input.Date ?? clock.Today;
        var v = new ValidationBuilder()
            .Require(date <= clock.Today, "date", "La date du reçu ne peut pas être dans le futur.")
            .Require(method == PaymentMethod.Especes || !string.IsNullOrWhiteSpace(input.Reference), "reference",
                "Indiquez la référence de la transaction (Mobile Money, banque ou chèque).")
            .Require(input.Note is null || input.Note.Length <= 500, "note", "Observation trop longue.");
        v.ThrowIfInvalid();
        var lastClosing = await LastClosingAsync(db, ct);
        if (lastClosing is not null && date <= lastClosing)
            throw new DomainException("date", $"La caisse est arrêtée jusqu'au {lastClosing:dd/MM/yyyy} : choisissez une date postérieure.");

        var data = await accounts.ForEnrollmentAsync(input.EnrollmentId, null, ct);
        var year = await db.AcademicYears.AsNoTracking().FirstAsync(y => y.Id == data.Enrollment.AcademicYearId, ct);
        if (year.Status == AcademicYearStatus.Closed) throw new DomainException("annee", $"L'année {year.Label} est clôturée.");
        // Un paiement se fait par mois complet : sans mois choisi, il n'est pas enregistré.
        if (input.FeeItemIds is not { Count: > 0 })
            throw new DomainException("rubriques", "Sélectionnez les mois payés : un paiement se fait par mois complet.");
        var plan = PaymentAllocator.Months(data.Account, input.FeeItemIds);
        var total = plan.Sum(p => p.Amount);
        if (input.Amount != total)
            throw new DomainException("montant", $"Les mois choisis valent {Money.Display(total, school.CurrencySymbol)}. La liste a changé : actualisez-la avant d'encaisser.");
        Money.EnsureValid(total, school.WholeAmountsOnly, "amount");

        var receipt = new Receipt
        {
            Number = await NextNumberAsync(db, "RC", year.FirstCalendarYear, ct),
            VerificationCode = DocumentNumbers.NewVerificationCode(),
            Date = date,
            StudentId = data.Enrollment.StudentId,
            EnrollmentId = data.Enrollment.Id,
            AcademicYearId = year.Id,
            Amount = total,
            Method = method,
            Reference = Clean(input.Reference),
            PayerName = Clean(input.PayerName),
            PayerPhone = Clean(input.PayerPhone),
            Note = Clean(input.Note),
            CashierName = user.Name,
            IdempotencyKey = Clean(input.IdempotencyKey),
        };
        foreach (var (feeItemId, amount) in plan) receipt.Allocations.Add(new ReceiptAllocation { ReceiptId = receipt.Id, FeeItemId = feeItemId, Amount = amount });
        db.Receipts.Add(receipt);
        audit.Record("paiement.encaisse", "Recu", receipt.Id.ToString(),
            $"Reçu {receipt.Number} : {Money.Display(receipt.Amount, school.CurrencySymbol)} pour {data.Enrollment.Student!.Matricule} ({Labels.Of(method)}).",
            new { receipt.Number, receipt.Amount, Affectations = plan.Select(p => new { p.FeeItemId, p.Amount }) });
        await db.SaveChangesAsync(ct);
        return await GetAsync(receipt.Id, ct);
    }, ct);

    public Task<ReceiptDto> CancelAsync(Guid id, string reason, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        if (!user.HasPermission(Permissions.PaymentsCancel)) throw new DomainException("droits", "Vous n'êtes pas autorisé à annuler un reçu.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5) throw new DomainException("reason", "Le motif d'annulation est obligatoire (5 caractères minimum).");
        var receipt = await db.Receipts.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Reçu introuvable.");
        if (receipt.Status == OperationStatus.Annule) throw new ConflictException("Ce reçu est déjà annulé.");
        var lastClosing = await LastClosingAsync(db, ct);
        if (lastClosing is not null && receipt.Date <= lastClosing)
            throw new DomainException("date", $"Ce reçu appartient à une caisse arrêtée le {lastClosing:dd/MM/yyyy}. Enregistrez une correction comptable.");
        receipt.Status = OperationStatus.Annule;
        receipt.CancelledAtUtc = clock.UtcNow;
        receipt.CancelledBy = user.Name;
        receipt.CancellationReason = reason.Trim();
        audit.Record("paiement.annule", "Recu", id.ToString(), $"Reçu {receipt.Number} annulé.", new { motif = reason.Trim(), receipt.Amount });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }, ct);

    public async Task<ReceiptDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var r = await db.Receipts.AsNoTracking().Include(x => x.Allocations).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Reçu introuvable.");
        var student = await db.Students.AsNoTracking().FirstAsync(s => s.Id == r.StudentId, ct);
        var info = await (from e in db.Enrollments.AsNoTracking()
                          join p in db.Promotions.AsNoTracking() on e.PromotionId equals p.Id
                          join y in db.AcademicYears.AsNoTracking() on e.AcademicYearId equals y.Id
                          where e.Id == r.EnrollmentId
                          select new { p.Code, y.Label }).FirstAsync(ct);
        var itemIds = r.Allocations.Select(a => a.FeeItemId).ToList();
        var labels = await db.FeeItems.AsNoTracking().Where(f => itemIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => (f.Label, f.DueDate), ct);
        var allocations = r.Allocations.OrderBy(a => labels.GetValueOrDefault(a.FeeItemId).DueDate)
            .Select(a => new AllocationPreview(a.FeeItemId, labels.GetValueOrDefault(a.FeeItemId).Label ?? "?", a.Amount, 0)).ToList();
        // Le versement garde le payeur saisi à l'époque. À défaut, le reçu montre le tuteur actuel de l'apprenant :
        // une correction du dossier se répercute donc aussi sur les reçus déjà enregistrés.
        var payerName = r.PayerName ?? student.GuardianName;
        var payerPhone = r.PayerPhone ?? student.GuardianPhone;
        return new ReceiptDto(r.Id, r.Number, r.VerificationCode, r.Date, r.StudentId, student.Matricule, student.FullName, info.Code, info.Label, r.Amount,
            Labels.Of(r.Method), r.Reference, payerName, payerPhone, r.Note, r.CashierName, r.Status == OperationStatus.Valide ? "Valide" : "Annulé",
            r.CancelledAtUtc, r.CancelledBy, r.CancellationReason, allocations, r.CreatedAtUtc);
    }

    public async Task<PagedResult<ReceiptSummaryRow>> ListAsync(ReceiptQuery q, CancellationToken ct = default)
    {
        var query = from r in db.Receipts.AsNoTracking()
                    join s in db.Students.AsNoTracking() on r.StudentId equals s.Id
                    join e in db.Enrollments.AsNoTracking() on r.EnrollmentId equals e.Id
                    join p in db.Promotions.AsNoTracking() on e.PromotionId equals p.Id
                    select new { r, s, p };
        if (q.From is not null) query = query.Where(x => x.r.Date >= q.From);
        if (q.To is not null) query = query.Where(x => x.r.Date <= q.To);
        if (q.PromotionId is not null) query = query.Where(x => x.p.Id == q.PromotionId);
        if (!string.IsNullOrWhiteSpace(q.Method)) { var m = ParseMethod(q.Method); query = query.Where(x => x.r.Method == m); }
        if (!string.IsNullOrWhiteSpace(q.Status))
        {
            var st = TextKey.Normalize(q.Status).StartsWith("ANNUL", StringComparison.Ordinal) ? OperationStatus.Annule : OperationStatus.Valide;
            query = query.Where(x => x.r.Status == st);
        }
        foreach (var term in TextKey.Normalize(q.Search).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = term;
            query = query.Where(x => x.s.SearchText.Contains(t) || x.r.Number.Contains(t));
        }
        var total = await query.CountAsync(ct);
        var validAmounts = await query.Where(x => x.r.Status == OperationStatus.Valide).Select(x => x.r.Amount).ToListAsync(ct);
        var pageSize = Math.Clamp(q.PageSize, 1, 1000);
        var page = Math.Max(1, q.Page);
        var rows = await query.OrderByDescending(x => x.r.Date).ThenByDescending(x => x.r.Number).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new ReceiptSummaryRow(x.r.Id, x.r.Number, x.r.Date, x.s.Matricule, x.s.LastName + " " + x.s.MiddleName + " " + x.s.FirstName, x.p.Code,
                x.r.Amount, x.r.Method, x.r.Status, x.r.CashierName, x.r.Reference))
            .ToListAsync(ct);
        return new PagedResult<ReceiptSummaryRow>(rows, total, page, pageSize, validAmounts.Sum());
    }

    /// <summary>Vérification publique (QR code) : n'expose que le strict nécessaire.</summary>
    public async Task<ReceiptVerification> VerifyAsync(string code, CancellationToken ct = default)
    {
        var normalized = TextKey.Normalize(code);
        if (normalized.Length is < 8 or > 16) return new ReceiptVerification(false, null, null, null, null, null, null, null);
        var r = await db.Receipts.AsNoTracking().FirstOrDefaultAsync(x => x.VerificationCode == normalized, ct);
        if (r is null) return new ReceiptVerification(false, null, null, null, null, null, null, null);
        var s = await db.Students.AsNoTracking().FirstAsync(x => x.Id == r.StudentId, ct);
        var school = await YearContext.SchoolAsync(db, ct);
        var masked = $"{s.LastName} {(s.FirstName.Length > 0 ? s.FirstName[0] + "." : string.Empty)}";
        return new ReceiptVerification(true, r.Number, r.Date, r.Amount, masked, school.Name, r.Status == OperationStatus.Valide ? "Valide" : "Annulé", school.CurrencySymbol);
    }

    public async Task<byte[]> ReceiptPdfAsync(Guid id, string verificationBaseUrl, CancellationToken ct = default)
    {
        var receipt = await GetAsync(id, ct);
        var school = await YearContext.SchoolAsync(db, ct);
        var full = await db.Receipts.AsNoTracking().FirstAsync(r => r.Id == id, ct);
        var account = await accounts.ForEnrollmentAsync(full.EnrollmentId, null, ct);
        return documents.Receipt(new ReceiptDocument(Header(school), receipt.Number, receipt.Date, receipt.StudentName, receipt.Matricule, receipt.PromotionCode,
            receipt.AcademicYear, receipt.Amount, FrenchNumbers.Amount(receipt.Amount, FrenchNumbers.CurrencyName(school.CurrencyCode)), receipt.Method,
            receipt.Reference, receipt.PayerName, receipt.Cashier, receipt.Allocations.Select(a => (a.Label, a.Amount)).ToList(), account.Account.Balance,
            $"{verificationBaseUrl.TrimEnd('/')}/verifier/{receipt.VerificationCode}", receipt.Status != "Valide", receipt.CancellationReason));
    }

    public async Task<byte[]> StatementPdfAsync(Guid enrollmentId, CancellationToken ct = default)
    {
        var school = await YearContext.SchoolAsync(db, ct);
        var dto = await accounts.AccountDtoAsync(enrollmentId, ct);
        return documents.Statement(new StatementDocument(Header(school), dto.FullName, dto.Matricule, dto.PromotionCode, dto.AcademicYear, clock.Today,
            dto.Lines, dto.Receipts, dto.TotalNet, dto.TotalPaid, dto.Balance, dto.Overdue));
    }

    public static SchoolHeader Header(SchoolProfile s) =>
        new(s.Name, s.Motto, string.Join(" · ", new[] { s.Address, s.City }.Where(x => !string.IsNullOrWhiteSpace(x))), s.Phone, s.Email, s.Logo, s.CurrencySymbol, s.ReceiptFooter);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ReceiptSummaryRow(Guid Id, string Number, DateOnly Date, string Matricule, string StudentName, string PromotionCode, decimal Amount,
    PaymentMethod MethodCode, OperationStatus StatusCode, string Cashier, string? Reference)
{
    public string Method => Labels.Of(MethodCode);
    public string Status => StatusCode == OperationStatus.Valide ? "Valide" : "Annulé";
}
