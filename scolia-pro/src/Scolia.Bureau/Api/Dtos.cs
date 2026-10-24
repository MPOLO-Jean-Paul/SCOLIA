using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scolia.Bureau.Api;

/// <summary>Session ouverte sur le serveur.</summary>
public sealed record SessionDto(
    string UserName, string DisplayName, string RoleLabel, string[] Permissions, bool MustChangePassword,
    string SchoolName, string? SchoolShortName, string? CurrentYear, string CurrencySymbol);

/// <summary>Indicateurs du tableau de bord, tels que servis par le serveur.</summary>
public sealed record DashboardDto(
    string AcademicYear, int ActiveStudents, decimal CollectedTotal, decimal CollectedThisMonth, decimal ExpectedToDate,
    decimal Outstanding, decimal Overdue, decimal RecoveryRate, int StudentsUpToDate, int StudentsLate,
    decimal ExpensesThisMonth, IReadOnlyList<ChartPoint> MonthlyCollections);

public sealed record ChartPoint(string Label, decimal Value, decimal Secondary);

/// <summary>Ligne de la liste des apprenants.</summary>
public sealed record StudentListItem(
    Guid Id, string Matricule, string LastName, string MiddleName, string FirstName, string PromotionCode,
    string Status, string? GuardianPhone, decimal Balance, decimal Overdue);

/// <summary>Ligne du journal des reçus.</summary>
public sealed record ReceiptRow(
    Guid Id, string Number, DateOnly Date, string Matricule, string StudentName, string PromotionCode,
    decimal Amount, string Method, string Status, string Cashier);

/// <summary>Page de résultats renvoyée par l'API.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <summary>Erreur renvoyée par le serveur (format RFC 9457).</summary>
public sealed class ApiException(string message) : Exception(message);

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
