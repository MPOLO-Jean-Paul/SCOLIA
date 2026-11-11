namespace Scolia.Bureau.Api;

/// <summary>Ce qui a été effacé : reçus, imputations, inscriptions et dossiers. Les champs non concernés valent zéro.</summary>
public sealed record ErasureSummaryDto(int Receipts, int Allocations, int Enrollments, int Students);
