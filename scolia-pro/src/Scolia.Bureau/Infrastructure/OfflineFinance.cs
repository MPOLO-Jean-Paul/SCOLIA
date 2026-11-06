using System.Text.Json;
using Scolia.Bureau.Api;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Journal comptable local pour un poste temporairement autonome. Les versements
/// restent chiffrés dans l'outbox existante, et non enregistrés une seconde fois.
/// Un numéro LOCAL n'est jamais présenté comme un reçu officiel vérifiable.
/// </summary>
public static class OfflineFinance
{
    public sealed record LocalPayment(
        Guid Id, Guid EnrollmentId, decimal Amount, DateOnly Date, string Method,
        string? Reference, string? PayerPhone, string? Note,
        IReadOnlyList<Guid> FeeItemIds, string User, DateTime CreatedAt, string Key)
    {
        public string TemporaryNumber => "LOCAL-" + Id.ToString("N")[..8].ToUpperInvariant();
    }

    public static IReadOnlyList<LocalPayment> Pending(OfflineStore store)
    {
        var result = new List<LocalPayment>();
        foreach (var item in store.Outbox().Where(x => x.Method == "POST" && x.Path == "api/paiements"))
        {
            try
            {
                using var doc = JsonDocument.Parse(item.Body);
                var obj = doc.RootElement;
                if (!Guid.TryParse(item.Id, out var id) ||
                    !obj.TryGetProperty("enrollmentId", out var enrollment) ||
                    !enrollment.TryGetGuid(out var enrollmentId) ||
                    !obj.TryGetProperty("amount", out var amountNode) ||
                    !amountNode.TryGetDecimal(out var amount) || amount <= 0 ||
                    !obj.TryGetProperty("feeItemIds", out var fees) || fees.ValueKind != JsonValueKind.Array)
                    continue;
                var feeIds = fees.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String && x.TryGetGuid(out _))
                    .Select(x => x.GetGuid()).Distinct().ToList();
                if (feeIds.Count == 0) continue;
                var date = obj.TryGetProperty("date", out var day) &&
                    day.ValueKind == JsonValueKind.String &&
                    DateOnly.TryParse(day.GetString(), out var value)
                    ? value : DateOnly.FromDateTime(item.CreatedAt);
                string? Text(string property) => obj.TryGetProperty(property, out var element) &&
                    element.ValueKind == JsonValueKind.String ? element.GetString() : null;
                result.Add(new LocalPayment(id, enrollmentId, amount, date,
                    Text("method") ?? "Especes", Text("reference"), Text("payerPhone"),
                    Text("note"), feeIds, item.User, item.CreatedAt, item.Key));
            }
            catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException) { }
        }
        return result.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToList();
    }

    /// <summary>Plan local de paiement partiel, du mois le plus ancien au plus récent.</summary>
    public static PaymentPreviewDto Preview(AccountDto account, IReadOnlyList<Guid> feeItemIds, decimal amount)
    {
        if (amount <= 0 || feeItemIds.Count == 0 || feeItemIds.Count != feeItemIds.Distinct().Count())
            throw new ArgumentException("Sélectionnez des mois distincts et un montant positif.");
        var selected = account.Lines
            .Where(l => feeItemIds.Contains(l.FeeItemId) && l.Kind == "Mensualite")
            .OrderBy(l => l.DueDate).ThenBy(l => l.Label, StringComparer.Ordinal).ToList();
        if (selected.Count != feeItemIds.Count || selected.Any(l => l.Remaining <= 0) ||
            amount > selected.Sum(l => l.Remaining))
            throw new ArgumentException("Montant ou mois indisponible dans le compte local. Actualisez l'apprenant.");
        var remaining = amount;
        var result = new List<AllocationDto>();
        foreach (var line in selected)
        {
            if (remaining <= 0) break;
            var allocated = Math.Min(line.Remaining, remaining);
            result.Add(new AllocationDto(line.FeeItemId, line.Label, allocated, line.Remaining - allocated));
            remaining -= allocated;
        }
        if (remaining != 0) throw new ArgumentException("Le montant excède les échéances sélectionnées.");
        return new PaymentPreviewDto(result, Math.Max(0, account.Balance - amount));
    }

    /// <summary>
    /// Calcule immédiatement le compte à partir du dernier cliché serveur et
    /// des encaissements non encore confirmés. Les écritures sont lues dans l'ordre.
    /// Le serveur contrôle de nouveau les montants, clôtures et droits au rejeu.
    /// </summary>
    public static AccountDto ProjectAccount(AccountDto account, OfflineStore store)
    {
        var lines = account.Lines.ToList();
        var receipts = account.Receipts.ToList();
        decimal total = 0;
        foreach (var payment in Pending(store).Where(x => x.EnrollmentId == account.EnrollmentId))
        {
            var current = account with { Lines = lines, Balance = Math.Max(0, account.Balance - total) };
            PaymentPreviewDto preview;
            try { preview = Preview(current, payment.FeeItemIds, payment.Amount); }
            catch (ArgumentException) { continue; } // Conflit potentiel, pas de faux solde.
            foreach (var allocation in preview.Allocations)
            {
                var index = lines.FindIndex(x => x.FeeItemId == allocation.FeeItemId);
                if (index < 0) continue;
                var line = lines[index];
                lines[index] = line with
                {
                    Paid = line.Paid + allocation.Amount,
                    Remaining = allocation.RemainingAfter,
                    Status = allocation.RemainingAfter == 0 ? "Payé" : line.Status,
                };
            }
            total += payment.Amount;
            receipts.Add(new ReceiptSummaryDto(payment.Id, payment.TemporaryNumber, payment.Date,
                payment.Amount, payment.Method, "En attente", payment.User,
                string.Join(", ", preview.Allocations.Select(a => a.Label))));
        }
        var overdue = lines.Where(x => x.IsOverdue).Sum(x => x.Remaining);
        return account with
        {
            Lines = lines, Receipts = receipts,
            TotalPaid = account.TotalPaid + total,
            Balance = Math.Max(0, account.Balance - total),
            Overdue = overdue,
            OverdueCount = lines.Count(x => x.IsOverdue && x.Remaining > 0),
        };
    }

    public static AccountDto? CachedAccount(OfflineStore store, Guid enrollmentId)
    {
        var cached = store.ReadPage($"api/inscriptions/{enrollmentId}/compte");
        if (cached is null) return null;
        try { return JsonSerializer.Deserialize<AccountDto>(cached.Value.Json, Json.Options); }
        catch (JsonException) { return null; }
    }

    public static ReceiptDto? PendingReceipt(OfflineStore store, Guid localId)
    {
        var payments = Pending(store);
        var payment = payments.FirstOrDefault(p => p.Id == localId);
        if (payment is null) return null;
        var original = CachedAccount(store, payment.EnrollmentId);
        if (original is null) return null;
        var prior = original;
        foreach (var pending in payments.Where(x => x.EnrollmentId == payment.EnrollmentId))
        {
            PaymentPreviewDto preview;
            try { preview = Preview(prior, pending.FeeItemIds, pending.Amount); }
            catch (ArgumentException) { continue; }
            if (pending.Id == payment.Id)
                return new ReceiptDto(payment.Id, payment.TemporaryNumber, string.Empty,
                    payment.Date, original.StudentId, original.Matricule, original.FullName,
                    original.PromotionCode, original.AcademicYear, payment.Amount,
                    payment.Method, payment.Reference, null, payment.PayerPhone,
                    payment.Note, payment.User, "En attente", null, null, null,
                    preview.Allocations, pending.CreatedAt.ToUniversalTime());
            // Construire une vue antérieure au prochain paiement de la même inscription.
            var lines = prior.Lines.ToList();
            foreach (var allocation in preview.Allocations)
            {
                var index = lines.FindIndex(x => x.FeeItemId == allocation.FeeItemId);
                var line = lines[index];
                lines[index] = line with { Paid = line.Paid + allocation.Amount, Remaining = allocation.RemainingAfter };
            }
            prior = prior with { Lines = lines, Balance = Math.Max(0, prior.Balance - pending.Amount) };
        }
        return null;
    }

    public static IReadOnlyList<ReceiptRowDto> PendingRows(OfflineStore store)
    {
        var results = new List<ReceiptRowDto>();
        foreach (var payment in Pending(store))
        {
            var account = CachedAccount(store, payment.EnrollmentId);
            if (account is null) continue;
            results.Add(new ReceiptRowDto(payment.Id, payment.TemporaryNumber, payment.Date,
                account.Matricule, account.FullName, account.PromotionCode,
                payment.Amount, payment.Method, "En attente", payment.User, payment.Reference));
        }
        return results;
    }
}
