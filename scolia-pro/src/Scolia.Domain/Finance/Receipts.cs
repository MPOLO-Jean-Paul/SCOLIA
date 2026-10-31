using System.Security.Cryptography;
using Scolia.Domain.Common;

namespace Scolia.Domain.Finance;

public enum PaymentMethod { Especes = 0, MobileMoney = 1, Banque = 2, Cheque = 3, Autre = 4 }

public enum OperationStatus { Valide = 0, Annule = 1 }

/// <summary>Reçu de paiement. Jamais supprimé : une erreur se corrige par annulation motivée.</summary>
public sealed class Receipt : Entity
{
    /// <summary>Numéro séquentiel sans trou par année (norme comptable), ex. RC-2026-000123.</summary>
    public string Number { get; set; } = string.Empty;
    /// <summary>Code aléatoire imprimé dans le QR code pour vérifier l'authenticité.</summary>
    public string VerificationCode { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public Guid StudentId { get; set; }
    public Guid EnrollmentId { get; set; }
    public Guid AcademicYearId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    public string? PayerName { get; set; }
    public string? PayerPhone { get; set; }
    public string? Note { get; set; }
    public string CashierName { get; set; } = string.Empty;
    public OperationStatus Status { get; set; } = OperationStatus.Valide;
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancelledBy { get; set; }
    public string? CancellationReason { get; set; }
    /// <summary>Clé fournie par le poste de saisie pour empêcher un double enregistrement.</summary>
    public string? IdempotencyKey { get; set; }
    public List<ReceiptAllocation> Allocations { get; set; } = [];
}

public sealed class ReceiptAllocation : Entity
{
    public Guid ReceiptId { get; set; }
    public Guid FeeItemId { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>Dépense de caisse ou de banque.</summary>
public sealed class Expense : Entity
{
    public string Number { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public Guid AcademicYearId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Beneficiary { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    public string RecordedByName { get; set; } = string.Empty;
    public OperationStatus Status { get; set; } = OperationStatus.Valide;
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancelledBy { get; set; }
    public string? CancellationReason { get; set; }
}

/// <summary>Arrêté de caisse : comparaison entre l'espèce attendue et l'espèce comptée.
/// Après un arrêté, aucune opération ne peut être ajoutée ou annulée à une date antérieure ou égale.</summary>
public sealed class CashClosing : Entity
{
    public DateOnly Date { get; set; }
    public DateOnly PeriodStart { get; set; }
    public decimal CashReceipts { get; set; }
    public decimal CashExpenses { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal CountedCash { get; set; }
    public decimal Difference { get; set; }
    public string? Note { get; set; }
    public string ClosedByName { get; set; } = string.Empty;
}

/// <summary>Compteur transactionnel pour les numérotations séquentielles.</summary>
public sealed class Counter
{
    public string Name { get; set; } = string.Empty;
    public long Value { get; set; }
}

public static class DocumentNumbers
{
    private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Format(string prefix, int year, long sequence) =>
        $"{prefix}-{year}-{sequence.ToString("000000", System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>Code de vérification aléatoire (alphabet sans caractères ambigus).</summary>
    public static string NewVerificationCode(int length = 10)
    {
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[length];
        for (var i = 0; i < length; i++) chars[i] = Crockford[bytes[i] % Crockford.Length];
        return new string(chars);
    }
}

public static class Money
{
    public static void EnsureValid(decimal amount, bool wholeOnly, string field = "montant")
    {
        if (amount <= 0) throw new DomainException(field, "Le montant doit être supérieur à zéro.");
        if (wholeOnly && amount != decimal.Truncate(amount)) throw new DomainException(field, "Le montant doit être un nombre entier.");
        if (decimal.Round(amount, 2) != amount) throw new DomainException(field, "Le montant ne peut pas avoir plus de deux décimales.");
        if (amount > 1_000_000_000_000m) throw new DomainException(field, "Montant hors limites.");
    }

    public static string Display(decimal amount, string symbol) =>
        amount.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " " + symbol;
}
