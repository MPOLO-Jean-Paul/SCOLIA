namespace Scolia.Bureau.Api;

/// <summary>Mois ouvert d'un apprenant : son libellé, son échéance et ce qui reste à payer.</summary>
public sealed record BatchMonthDto(Guid FeeItemId, string Label, DateOnly DueDate, decimal Remaining, bool IsOverdue);

/// <summary>Apprenant à régler dans le lot, avec ses mois ouverts.</summary>
public sealed record BatchCandidateDto(Guid EnrollmentId, string Matricule, string FullName, string PromotionCode, decimal Balance, decimal Overdue,
    List<BatchMonthDto> Months);

/// <summary>Contrôle d'une ligne : montant calculé, reçus semblables déjà enregistrés, ou erreur à corriger.</summary>
public sealed record BatchPreviewLineDto(Guid EnrollmentId, string Matricule, string FullName, decimal Amount, int MonthCount, List<string> Duplicates,
    string? Error);

public sealed record BatchPreviewDto(int Count, int Errors, decimal Total, int DuplicateCount, List<BatchPreviewLineDto> Lines);

/// <summary>Résultat d'une ligne après enregistrement : son reçu, ou la raison du refus.</summary>
public sealed record BatchPaymentLineDto(Guid EnrollmentId, string Matricule, string FullName, bool Ok, Guid? ReceiptId, string? ReceiptNumber, decimal Amount,
    string? Error);

public sealed record BatchPaymentResultDto(int Succeeded, int Failed, decimal Total, List<BatchPaymentLineDto> Lines);
