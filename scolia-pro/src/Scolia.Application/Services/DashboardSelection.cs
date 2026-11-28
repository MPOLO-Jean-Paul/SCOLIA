using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.Finance;
using Scolia.Domain.School;
using Scolia.Domain.Students;

namespace Scolia.Application.Services;

public sealed partial class ReportService
{
    public async Task<DashboardFilterOptions> DashboardFiltersAsync(CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var sections = await db.Sections.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new DashboardSectionOption(s.Id, s.Name)).ToListAsync(ct);
        var promotions = await db.Promotions.AsNoTracking().Where(p => p.AcademicYearId == year.Id).OrderBy(p => p.Code)
            .Select(p => new DashboardPromotionOption(p.Id, p.Code, p.Name, p.SectionId)).ToListAsync(ct);
        return new DashboardFilterOptions(sections, promotions);
    }

    private static bool MatchesDashboardPromotion(Promotion promotion, DashboardQuery query)
    {
        if (query.PromotionId is { } id && promotion.Id != id || query.SectionId is { } section && promotion.SectionId != section) return false;
        return string.IsNullOrWhiteSpace(query.Search) || CultureInfo.GetCultureInfo("fr-FR").CompareInfo
            .IndexOf(promotion.Code + " " + promotion.Name, query.Search.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    }

    private static List<AccountService.EnrollmentAccount> SelectDashboardAccounts(IEnumerable<AccountService.EnrollmentAccount> entries, DashboardQuery query)
    {
        if (!string.IsNullOrEmpty(query.Gender) && query.Gender is not ("M" or "F"))
            throw new DomainException("gender", "Choisissez Tous les sexes, Masculin ou Féminin.");
        if (!string.IsNullOrEmpty(query.State) && query.State is not ("paying" or "sponsored" or "partial" or "uptodate" or "late" or "balance"))
            throw new DomainException("state", "Situation de paiement invalide.");
        if (!string.IsNullOrEmpty(query.Sort) && query.Sort is not ("code" or "code-desc" or "students" or "collected" or "outstanding" or "overdue"))
            throw new DomainException("sort", "Ordre de tri invalide.");
        if (query.Search?.Length > 120) throw new DomainException("search", "Limitez la recherche à 120 caractères.");
        return entries.Where(a => MatchesDashboardPromotion(a.Promotion, query))
            .Where(a => string.IsNullOrEmpty(query.Gender) || a.Enrollment.Student?.Gender.ToString() == query.Gender)
            .Where(a => query.State switch
            {
                "paying" => a.IsPaying,
                "sponsored" => a.IsSponsored,
                "partial" => a.Coverage == Coverage.Partielle,
                "uptodate" => a.IsPaying && a.Account.Overdue == 0,
                "late" => a.IsPaying && a.Account.Overdue > 0,
                "balance" => a.Account.Balance > 0,
                _ => true
            }).ToList();
    }

    private static List<T> OrderDashboardRows<T>(IEnumerable<T> rows, DashboardQuery query, Func<T, string> code,
        Func<T, int> students, Func<T, decimal> collected, Func<T, decimal> outstanding, Func<T, decimal> overdue)
    {
        var ordered = query.Sort switch
        {
            "code-desc" => rows.OrderByDescending(code, StringComparer.OrdinalIgnoreCase),
            "students" => rows.OrderByDescending(students),
            "collected" => rows.OrderByDescending(collected),
            "outstanding" => rows.OrderByDescending(outstanding),
            "overdue" => rows.OrderByDescending(overdue),
            _ => rows.OrderBy(code, StringComparer.OrdinalIgnoreCase)
        };
        return ordered.ThenBy(code, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string DashboardStamp<T>(DashboardQuery query, T data) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { query, data }))).ToLowerInvariant();

    private async Task<string> DashboardCaptionAsync(DashboardQuery query, CancellationToken ct)
    {
        var options = await DashboardFiltersAsync(ct);
        var parts = new List<string>();
        parts.Add(query.SectionId is { } section ? options.Sections.FirstOrDefault(s => s.Id == section)?.Label ?? "Section sélectionnée" : "Toutes les sections");
        parts.Add(query.PromotionId is { } promotion ? options.Promotions.FirstOrDefault(p => p.Id == promotion)?.Name ?? "Promotion sélectionnée" : "Toutes les promotions");
        if (!string.IsNullOrEmpty(query.Gender)) parts.Add(query.Gender == "M" ? "Masculin" : "Féminin");
        parts.Add(query.State switch { "paying" => "Payants", "sponsored" => "Pris en charge", "partial" => "Prises en charge partielles", "uptodate" => "Payants à jour", "late" => "Payants en retard", "balance" => "Solde restant", _ => "Toutes les situations" });
        if (!string.IsNullOrWhiteSpace(query.Search)) parts.Add("Recherche : " + query.Search.Trim());
        parts.Add(query.Sort switch { "students" => "Effectif décroissant", "collected" => "Encaissements décroissants", "outstanding" => "Reste décroissant", "overdue" => "Retard décroissant", "code-desc" => "Promotion décroissante", _ => "Promotion croissante" });
        return string.Join(" · ", parts);
    }

    private static void CheckDashboardSelection(string? expected, string? actual)
    {
        if (string.IsNullOrEmpty(expected) || expected != actual)
            throw new DomainException("selection", "Les données ou les filtres ont changé. Actualisez le tableau de bord avant l'export.");
    }

    public async Task<TableDocument> DashboardTableAsync(DashboardQuery query, string expectedSelection, CancellationToken ct = default)
    {
        var data = await FilteredDashboardAsync(query, ct);
        CheckDashboardSelection(expectedSelection, data.SelectionKey);
        var school = await YearContext.SchoolAsync(db, ct);
        string M(decimal value) => Money.Display(value, school.CurrencySymbol);
        return new TableDocument(PaymentService.Header(school), "Tableau de bord — sélection", await DashboardCaptionAsync(query, ct),
            ["Promotion", "Nom", "Effectif", "Payants", "À jour", "En retard", "Pris en charge", "Attendu à ce jour", "Encaissé", "Reste", "Exigible"],
            data.Promotions.Select(p => (IReadOnlyList<string>)[p.Code, p.Name, p.Students.ToString(CultureInfo.InvariantCulture), p.Paying.ToString(CultureInfo.InvariantCulture), p.UpToDate.ToString(CultureInfo.InvariantCulture), p.Late.ToString(CultureInfo.InvariantCulture), p.Sponsored.ToString(CultureInfo.InvariantCulture), M(p.Expected), M(p.Collected), M(p.Outstanding), M(p.Overdue)]).ToList(),
            ["Total sélection", "", data.ActiveStudents.ToString(CultureInfo.InvariantCulture), data.PayingStudents.ToString(CultureInfo.InvariantCulture), data.StudentsUpToDate.ToString(CultureInfo.InvariantCulture), data.StudentsLate.ToString(CultureInfo.InvariantCulture), data.SponsoredStudents.ToString(CultureInfo.InvariantCulture), M(data.ExpectedToDate), M(data.CollectedTotal), M(data.Outstanding), M(data.Overdue)], Landscape: true);
    }

    public async Task<TableDocument> PromoterDashboardTableAsync(DashboardQuery query, string expectedSelection, CancellationToken ct = default)
    {
        var data = await FilteredPromoterDashboardAsync(query, ct);
        CheckDashboardSelection(expectedSelection, data.SelectionKey);
        var school = await YearContext.SchoolAsync(db, ct);
        string M(decimal value) => Money.Display(value, school.CurrencySymbol);
        return new TableDocument(PaymentService.Header(school), "Pilotage — sélection", await DashboardCaptionAsync(query, ct),
            ["Promotion", "Nom", "Effectif", "Payants", "Pris en charge", "Encaissé aujourd'hui", "Encaissé ce mois", "Encaissé cette année", "Reste", "Exigible"],
            data.Promotions.Select(p => (IReadOnlyList<string>)[p.Code, p.Name, p.ActiveStudents.ToString(CultureInfo.InvariantCulture), p.PayingStudents.ToString(CultureInfo.InvariantCulture), p.SponsoredStudents.ToString(CultureInfo.InvariantCulture), M(p.CollectedToday), M(p.CollectedThisMonth), M(p.CollectedTotal), M(p.Outstanding), M(p.Overdue)]).ToList(),
            ["Total sélection", "", data.ActiveStudents.ToString(CultureInfo.InvariantCulture), data.PayingStudents.ToString(CultureInfo.InvariantCulture), data.SponsoredStudents.ToString(CultureInfo.InvariantCulture), M(data.CollectedToday), M(data.CollectedThisMonth), M(data.CollectedTotal), M(data.Outstanding), M(data.Overdue)], Landscape: true);
    }

    public async Task<byte[]> DashboardExportAsync(DashboardQuery query, string expectedSelection, string format, bool promoter, CancellationToken ct = default)
    {
        if (format is not ("pdf" or "xlsx")) throw new DomainException("format", "Choisissez Excel ou PDF.");
        var summary = promoter ? await PromoterDashboardTableAsync(query, expectedSelection, ct)
            : await DashboardTableAsync(query, expectedSelection, ct);
        if (format == "pdf") return documents.Table(summary);
        var sheets = new List<(string SheetName, TableDocument Document)> { ("Synthèse", summary) };
        string M(decimal value) => Money.Display(value, summary.School.CurrencySymbol);
        if (promoter)
        {
            var data = await FilteredPromoterDashboardAsync(query, ct);
            CheckDashboardSelection(expectedSelection, data.SelectionKey);
            var months = data.Promotions.SelectMany(p => p.Periods).GroupBy(p => p.PeriodId).ToList();
            sheets.Add(("Mensualités", new TableDocument(summary.School, "Mensualités de la sélection", summary.Subtitle,
                ["Période", "Dû net", "Réglé sur la période", "Reste sur la période", "Encaissé aux dates du mois"],
                months.Select(g => (IReadOnlyList<string>)[g.First().Label, M(g.Sum(p => p.DueNet)), M(g.Sum(p => p.PaidAgainstDue)), M(g.Sum(p => p.RemainingDue)), M(g.Sum(p => p.CollectedOnDates))]).ToList(),
                ["Total mensualités", M(months.Sum(g => g.Sum(p => p.DueNet))), M(months.Sum(g => g.Sum(p => p.PaidAgainstDue))), M(months.Sum(g => g.Sum(p => p.RemainingDue))), M(months.Sum(g => g.Sum(p => p.CollectedOnDates)))])));
        }
        else
        {
            var data = await FilteredDashboardAsync(query, ct);
            CheckDashboardSelection(expectedSelection, data.SelectionKey);
            sheets.Add(("Par mois", new TableDocument(summary.School, "Encaissements par mois — sélection", summary.Subtitle,
                ["Période", "Encaissé aux dates du mois", "Dû net des mensualités"],
                data.MonthlyCollections.Select(p => (IReadOnlyList<string>)[p.Label, M(p.Value), M(p.Secondary ?? 0)]).ToList(),
                ["Total des périodes", M(data.MonthlyCollections.Sum(p => p.Value)), M(data.MonthlyCollections.Sum(p => p.Secondary ?? 0))])));
            sheets.Add(("Retards", new TableDocument(summary.School, "Ancienneté des retards — sélection", summary.Subtitle,
                ["Ancienneté", "Montant exigible", "Comptes concernés"],
                data.Aging.Select(p => (IReadOnlyList<string>)[p.Label, M(p.Value), (p.Secondary ?? 0).ToString(CultureInfo.InvariantCulture)]).ToList(),
                ["Total", M(data.Aging.Sum(p => p.Value)), data.Aging.Sum(p => p.Secondary ?? 0).ToString(CultureInfo.InvariantCulture)])));
            sheets.Add(("Derniers reçus", new TableDocument(summary.School, "Derniers reçus — sélection", summary.Subtitle,
                ["Numéro", "Date", "Montant", "Mode", "Statut"],
                data.LatestReceipts.Select(p => (IReadOnlyList<string>)[p.Number, p.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), M(p.Amount), p.Method, p.Status]).ToList())));
        }
        return spreadsheets.ExportTables(sheets);
    }
}
