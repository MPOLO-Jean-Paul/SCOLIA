using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Common;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.Finance;
using Scolia.Domain.School;
using Scolia.Domain.Security;

namespace Scolia.Application.Services;

/// <summary>
/// Encaissement par lot (version 79) : plusieurs apprenants, plusieurs mois, en une seule opération.
/// Chaque apprenant reçoit son propre reçu numéroté, validé comme un encaissement isolé. Une ligne en erreur
/// n'arrête pas les autres ; le résultat dit ligne par ligne ce qui est enregistré.
/// </summary>
public sealed class PaymentBatchService(IAppDbContext db, IWriteGate gate, IAuditLog audit, IClock clock, PaymentService payments, AccountService accounts)
{
    public const int MaxItems = 300;

    /// <summary>Apprenants de l'année courante qui ont encore un reste à payer, avec leurs mois ouverts.</summary>
    public async Task<IReadOnlyList<BatchCandidateDto>> CandidatesAsync(Guid? promotionId, string? search, CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var map = await accounts.ComputeAsync(year.Id, null, null, ct);
        var terms = TextKey.Normalize(search).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return map.Values
            .Where(a => a.Account.Balance > 0 && (promotionId is null || a.Promotion.Id == promotionId))
            .Where(a => terms.All(t => a.Enrollment.Student!.SearchText.Contains(t, StringComparison.Ordinal)))
            .OrderBy(a => a.Promotion.Code).ThenBy(a => a.Enrollment.Student!.LastName).ThenBy(a => a.Enrollment.Student!.FirstName)
            .Select(a => new BatchCandidateDto(a.Enrollment.Id, a.Enrollment.Student!.Matricule, a.Enrollment.Student.FullName, a.Promotion.Code,
                a.Account.Balance, a.Account.Overdue,
                a.Account.Lines.Where(l => l.Kind == FeeKind.Mensualite && l.Remaining > 0)
                    .OrderBy(l => l.DueDate).ThenBy(l => l.Order)
                    .Select(l => new BatchMonthDto(l.FeeItemId, l.Label, l.DueDate, l.Remaining, l.IsOverdue)).ToList()))
            .ToList();
    }

    /// <summary>Contrôle avant écriture : montant de chaque ligne et reçus semblables déjà enregistrés. N'écrit rien.</summary>
    public async Task<BatchPreviewDto> PreviewAsync(BatchPaymentRequest input, CancellationToken ct = default)
    {
        var date = EnsureItems(input);
        var lines = new List<BatchPreviewLineDto>(input.Items.Count);
        foreach (var item in input.Items)
        {
            var (matricule, name) = await IdentityAsync(item.EnrollmentId, ct);
            try
            {
                var data = await accounts.ForEnrollmentAsync(item.EnrollmentId, null, ct);
                var plan = PaymentAllocator.Months(data.Account, item.FeeItemIds);
                var total = plan.Sum(p => p.Amount);
                var duplicates = (await db.Receipts.AsNoTracking()
                        .Where(r => r.EnrollmentId == item.EnrollmentId && r.Status == OperationStatus.Valide && r.Date == date)
                        .Select(r => new { r.Number, r.Amount }).ToListAsync(ct))
                    .Where(r => r.Amount == total).Select(r => r.Number).ToList();
                lines.Add(new BatchPreviewLineDto(item.EnrollmentId, matricule, name, total, plan.Count, duplicates, null));
            }
            catch (DomainException ex)
            {
                lines.Add(new BatchPreviewLineDto(item.EnrollmentId, matricule, name, 0, 0, [], Describe(ex)));
            }
        }
        var valid = lines.Where(l => l.Error is null).ToList();
        return new BatchPreviewDto(valid.Count, lines.Count - valid.Count, valid.Sum(l => l.Amount), valid.Sum(l => l.Duplicates.Count), lines);
    }

    /// <summary>
    /// Enregistre chaque ligne comme un encaissement distinct. Une clé d'idempotence par ligne : un envoi répété
    /// (réseau coupé) ne crée pas de second reçu.
    /// </summary>
    public async Task<BatchPaymentResultDto> RecordAsync(BatchPaymentRequest input, CancellationToken ct = default)
    {
        EnsureItems(input);
        var lines = new List<BatchPaymentLineDto>(input.Items.Count);
        for (var i = 0; i < input.Items.Count; i++)
        {
            var item = input.Items[i];
            var (matricule, name) = await IdentityAsync(item.EnrollmentId, ct);
            var key = string.IsNullOrWhiteSpace(input.IdempotencyKey) ? null : $"{input.IdempotencyKey.Trim()}:{i}";
            try
            {
                // Un envoi déjà traité renvoie son reçu : les mois sont alors déjà réglés et ne seraient plus planifiables.
                var previous = key is null ? null : await db.Receipts.AsNoTracking().Where(r => r.IdempotencyKey == key)
                    .Select(r => new { r.Id, r.Number, r.Amount }).FirstOrDefaultAsync(ct);
                if (previous is not null)
                {
                    lines.Add(new BatchPaymentLineDto(item.EnrollmentId, matricule, name, true, previous.Id, previous.Number, previous.Amount, null));
                    continue;
                }
                var data = await accounts.ForEnrollmentAsync(item.EnrollmentId, null, ct);
                var amount = PaymentAllocator.Months(data.Account, item.FeeItemIds).Sum(p => p.Amount);
                var receipt = await payments.CreateAsync(new PaymentRequest(item.EnrollmentId, amount, input.Date, item.Method, item.Reference,
                    item.PayerName, item.PayerPhone, null, item.FeeItemIds, key), ct);
                lines.Add(new BatchPaymentLineDto(item.EnrollmentId, matricule, name, true, receipt.Id, receipt.Number, receipt.Amount, null));
            }
            catch (DomainException ex)
            {
                lines.Add(new BatchPaymentLineDto(item.EnrollmentId, matricule, name, false, null, null, 0, Describe(ex)));
            }
        }

        var ok = lines.Where(l => l.Ok).ToList();
        var total = ok.Sum(l => l.Amount);
        var failed = lines.Count - ok.Count;
        var school = await YearContext.SchoolAsync(db, ct);
        await gate.RunAsync(() =>
        {
            audit.Record("paiement.lot", "Recu", null,
                $"Lot de {ok.Count} paiement(s) pour {Money.Display(total, school.CurrencySymbol)}, {failed} ligne(s) en erreur.",
                new { ok.Count, Total = total, Receipts = ok.Select(l => l.ReceiptNumber) });
            return db.SaveChangesAsync(ct);
        }, ct);
        return new BatchPaymentResultDto(ok.Count, failed, total, lines);
    }

    /// <summary>Taille du lot et date du reçu : aujourd'hui par défaut, jamais dans le futur.</summary>
    private DateOnly EnsureItems(BatchPaymentRequest input)
    {
        if (input.Items.Count == 0) throw new DomainException("lot", "Cochez au moins un apprenant.");
        if (input.Items.Count > MaxItems) throw new DomainException("lot", $"Un lot ne dépasse pas {MaxItems} paiements : découpez la saisie en plusieurs lots.");
        var date = input.Date ?? clock.Today;
        if (date > clock.Today) throw new DomainException("date", "La date du reçu ne peut pas être dans le futur.");
        return date;
    }

    /// <summary>Le message d'une erreur de validation tient dans ses champs : on les affiche, pas le titre générique.</summary>
    private static string Describe(DomainException ex) =>
        ex.Errors is { Count: > 0 } errors ? string.Join(' ', errors.Values.SelectMany(v => v)) : ex.Message;

    private async Task<(string Matricule, string Name)> IdentityAsync(Guid enrollmentId, CancellationToken ct)
    {
        var enrollment = await db.Enrollments.AsNoTracking().Include(e => e.Student).FirstOrDefaultAsync(e => e.Id == enrollmentId, ct);
        return enrollment?.Student is null ? ("?", "Inscription introuvable") : (enrollment.Student.Matricule, enrollment.Student.FullName);
    }
}

public sealed record BatchPaymentItem(Guid EnrollmentId, IReadOnlyList<Guid> FeeItemIds, string Method, string? Reference, string? PayerName, string? PayerPhone);

public sealed record BatchPaymentRequest(DateOnly? Date, IReadOnlyList<BatchPaymentItem> Items, string? IdempotencyKey);

public sealed record BatchPaymentLineDto(Guid EnrollmentId, string Matricule, string FullName, bool Ok, Guid? ReceiptId, string? ReceiptNumber, decimal Amount, string? Error);

public sealed record BatchPaymentResultDto(int Succeeded, int Failed, decimal Total, IReadOnlyList<BatchPaymentLineDto> Lines);

public sealed record BatchMonthDto(Guid FeeItemId, string Label, DateOnly DueDate, decimal Remaining, bool IsOverdue);

public sealed record BatchCandidateDto(Guid EnrollmentId, string Matricule, string FullName, string PromotionCode, decimal Balance, decimal Overdue,
    IReadOnlyList<BatchMonthDto> Months);

public sealed record BatchPreviewLineDto(Guid EnrollmentId, string Matricule, string FullName, decimal Amount, int MonthCount, IReadOnlyList<string> Duplicates,
    string? Error);

public sealed record BatchPreviewDto(int Count, int Errors, decimal Total, int DuplicateCount, IReadOnlyList<BatchPreviewLineDto> Lines);
