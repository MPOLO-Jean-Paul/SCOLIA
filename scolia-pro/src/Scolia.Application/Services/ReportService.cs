using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.Finance;
using Scolia.Domain.Students;

namespace Scolia.Application.Services;

/// <summary>Tableau de bord, liste des débiteurs, balance âgée et exports.</summary>
public sealed class ReportService(IAppDbContext db, IClock clock, AccountService accounts, IDocumentService documents, ISpreadsheetService spreadsheets)
{
    public async Task<DashboardDto> DashboardAsync(Guid? promotionId, CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var today = clock.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var map = await accounts.ComputeAsync(year.Id, null, today, ct);
        var all = map.Values.Where(x => promotionId is null || x.Promotion.Id == promotionId).ToList();
        var active = all.Where(x => x.Enrollment.Status == EnrollmentStatus.Actif).ToList();

        var receipts = await (from r in db.Receipts.AsNoTracking()
                              join e in db.Enrollments.AsNoTracking() on r.EnrollmentId equals e.Id
                              where r.AcademicYearId == year.Id && r.Status == OperationStatus.Valide && (promotionId == null || e.PromotionId == promotionId)
                              select new { r.Date, r.Amount }).ToListAsync(ct);
        var expensesMonth = (await db.Expenses.AsNoTracking().Where(e => e.Status == OperationStatus.Valide && e.Date >= monthStart && e.Date <= today)
            .Select(e => e.Amount).ToListAsync(ct)).Sum();

        var expectedToDate = all.Sum(x => x.Account.Lines.Where(l => l.DueDate <= today).Sum(l => l.Net));
        var paidOnDue = all.Sum(x => x.Account.Lines.Where(l => l.DueDate <= today).Sum(l => Math.Min(l.Paid, l.Net)));
        var monthly = year.Periods.Select(p =>
        {
            var start = new DateOnly(p.CalendarYear, p.Month, 1);
            var end = start.AddMonths(1).AddDays(-1);
            var collected = receipts.Where(r => r.Date >= start && r.Date <= end).Sum(r => r.Amount);
            var expected = all.Sum(x => x.Account.Lines.Where(l => l.PeriodId == p.Id).Sum(l => l.Net));
            return new SeriesPoint(ShortMonth(p.Name), collected, expected);
        }).ToList();

        var promotions = all.GroupBy(x => x.Promotion).OrderBy(g => g.Key.Code).Select(g =>
        {
            var expected = g.Sum(x => x.Account.Lines.Where(l => l.DueDate <= today).Sum(l => l.Net));
            var collected = g.Sum(x => x.Account.TotalPaid);
            var paid = g.Sum(x => x.Account.Lines.Where(l => l.DueDate <= today).Sum(l => Math.Min(l.Paid, l.Net)));
            return new PromotionStat(g.Key.Id, g.Key.Code, g.Key.Name, g.Count(x => x.Enrollment.Status == EnrollmentStatus.Actif), expected, collected,
                g.Sum(x => x.Account.Balance), g.Sum(x => x.Account.Overdue), expected == 0 ? 100 : decimal.Round(paid * 100 / expected, 1));
        }).ToList();

        var aging = new[] { "0-30 jours", "31-60 jours", "61-90 jours", "Plus de 90 jours" }
            .Select(b => new SeriesPoint(b, all.Where(x => Aging.Bucket(x.Account.OldestOverdueDate, today) == b).Sum(x => x.Account.Overdue),
                all.Count(x => Aging.Bucket(x.Account.OldestOverdueDate, today) == b)))
            .ToList();

        var latest = await (from r in db.Receipts.AsNoTracking()
                            join s in db.Students.AsNoTracking() on r.StudentId equals s.Id
                            where r.AcademicYearId == year.Id
                            orderby r.CreatedAtUtc descending
                            select new ReceiptSummaryDto(r.Id, r.Number, r.Date, r.Amount, r.Method.ToString(), r.Status == OperationStatus.Valide ? "Valide" : "Annulé",
                                r.CashierName, s.Matricule + " · " + s.LastName + " " + s.FirstName)).Take(8).ToListAsync(ct);
        latest = latest.Select(r => r with { Method = Labels.Of(Enum.Parse<PaymentMethod>(r.Method)) }).ToList();

        return new DashboardDto(year.Label, active.Count, active.Count(x => x.Enrollment.EnrolledOn >= monthStart),
            receipts.Sum(r => r.Amount), receipts.Where(r => r.Date >= monthStart).Sum(r => r.Amount), receipts.Where(r => r.Date == today).Sum(r => r.Amount),
            expectedToDate, all.Sum(x => x.Account.Balance), all.Sum(x => x.Account.Overdue),
            expectedToDate == 0 ? 100 : decimal.Round(paidOnDue * 100 / expectedToDate, 1),
            active.Count(x => x.Account.Overdue == 0), active.Count(x => x.Account.Overdue > 0), expensesMonth, monthly, promotions, aging, latest,
            active.Count(x => x.Enrollment.Student!.Gender == Gender.M), active.Count(x => x.Enrollment.Student!.Gender == Gender.F));
    }

    /// <summary>Abréviations françaises usuelles des mois.</summary>
    public static string ShortMonth(string name) => name switch
    {
        "Janvier" => "Janv.", "Février" => "Févr.", "Mars" => "Mars", "Avril" => "Avr.", "Mai" => "Mai", "Juin" => "Juin",
        "Juillet" => "Juil.", "Août" => "Août", "Septembre" => "Sept.", "Octobre" => "Oct.", "Novembre" => "Nov.", "Décembre" => "Déc.",
        _ => name.Length <= 4 ? name : name[..3] + ".",
    };

    public async Task<IReadOnlyList<DebtorDto>> DebtorsAsync(DebtorQuery q, CancellationToken ct = default)
    {
        var school = await YearContext.SchoolAsync(db, ct);
        var year = await YearContext.CurrentAsync(db, ct);
        var today = clock.Today;
        var map = await accounts.ComputeAsync(year.Id, null, today, ct);
        var terms = TextKey.Normalize(q.Search).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return map.Values
            .Where(x => x.Enrollment.Status is EnrollmentStatus.Actif or EnrollmentStatus.Suspendu)
            .Where(x => x.Account.Overdue > 0)
            .Where(x => q.PromotionId is null || x.Promotion.Id == q.PromotionId)
            .Where(x => q.MinOverdueMonths is null || x.Account.OverdueCount >= q.MinOverdueMonths)
            .Where(x => string.IsNullOrWhiteSpace(q.Bucket) || Aging.Bucket(x.Account.OldestOverdueDate, today) == q.Bucket)
            .Where(x => terms.All(t => x.Enrollment.Student!.SearchText.Contains(t, StringComparison.Ordinal)))
            .OrderByDescending(x => x.Account.Overdue)
            .Select(x =>
            {
                var s = x.Enrollment.Student!;
                return new DebtorDto(s.Id, x.Enrollment.Id, s.Matricule, s.FullName, x.Promotion.Code, x.Account.Balance, x.Account.Overdue, x.Account.OverdueCount,
                    string.Join(", ", x.Account.Lines.Where(l => l.IsOverdue).Select(l => l.Label)), Aging.Bucket(x.Account.OldestOverdueDate, today),
                    s.GuardianName, s.GuardianPhone ?? s.Phone, Reminders.WhatsApp(school, s, x.Account), Reminders.Sms(school, s, x.Account));
            }).ToList();
    }

    // ---------- Exports (PDF ou Excel) ----------
    public async Task<TableDocument> ClassListAsync(Guid promotionId, CancellationToken ct = default)
    {
        var school = await YearContext.SchoolAsync(db, ct);
        var promotion = await db.Promotions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == promotionId, ct) ?? throw new NotFoundException("Promotion introuvable.");
        var year = await db.AcademicYears.AsNoTracking().FirstAsync(y => y.Id == promotion.AcademicYearId, ct);
        var rows = await (from e in db.Enrollments.AsNoTracking()
                          join s in db.Students.AsNoTracking() on e.StudentId equals s.Id
                          where e.PromotionId == promotionId && e.Status == EnrollmentStatus.Actif
                          orderby s.LastName, s.MiddleName, s.FirstName
                          select s).ToListAsync(ct);
        var n = 0;
        return new TableDocument(PaymentService.Header(school), $"LISTE D'ENREGISTREMENT — {promotion.Code} {promotion.Name}", $"Année scolaire {year.Label} · {rows.Count} apprenant(s)",
            ["N°", "Matricule", "Nom", "Post-nom", "Prénom", "Sexe", "Naissance", "Contact parent"],
            rows.Select(s => (IReadOnlyList<string>)[(++n).ToString(), s.Matricule, s.LastName, s.MiddleName, s.FirstName, s.Gender.ToString(),
                s.BirthDate?.ToString("dd/MM/yyyy") ?? "", s.GuardianPhone ?? ""]).ToList());
    }

    public async Task<TableDocument> DebtorsTableAsync(DebtorQuery q, CancellationToken ct = default)
    {
        var school = await YearContext.SchoolAsync(db, ct);
        var list = await DebtorsAsync(q, ct);
        return new TableDocument(PaymentService.Header(school), "Apprenants en retard de paiement", $"Situation au {clock.Today:dd/MM/yyyy} · {list.Count} dossier(s)",
            ["Matricule", "Nom complet", "Promotion", "Périodes en retard", "Ancienneté", "Reste exigible", "Contact"],
            list.Select(d => (IReadOnlyList<string>)[d.Matricule, d.FullName, d.PromotionCode, d.Periods, d.AgingBucket, Money.Display(d.Overdue, school.CurrencySymbol), d.GuardianPhone ?? ""]).ToList(),
            ["", "Total", "", "", "", Money.Display(list.Sum(d => d.Overdue), school.CurrencySymbol), ""], Landscape: true);
    }

    public async Task<TableDocument> ReceiptsTableAsync(PaymentService payments, ReceiptQuery q, CancellationToken ct = default)
    {
        var school = await YearContext.SchoolAsync(db, ct);
        var page = await payments.ListAsync(q with { Page = 1, PageSize = 1000 }, ct);
        var period = q.From is null && q.To is null ? "Toutes dates" : $"Du {q.From:dd/MM/yyyy} au {q.To:dd/MM/yyyy}";
        return new TableDocument(PaymentService.Header(school), "Journal des encaissements", $"{period} · {page.Total} reçu(s)",
            ["Date", "N° reçu", "Matricule", "Apprenant", "Promotion", "Mode", "Référence", "Statut", "Montant"],
            page.Items.Select(r => (IReadOnlyList<string>)[r.Date.ToString("dd/MM/yyyy"), r.Number, r.Matricule, r.StudentName, r.PromotionCode, r.Method, r.Reference ?? "", r.Status,
                Money.Display(r.Amount, school.CurrencySymbol)]).ToList(),
            ["", "", "", "", "", "", "", "Total valide", Money.Display(page.Sum ?? 0, school.CurrencySymbol)], Landscape: true);
    }

    public byte[] Pdf(TableDocument table) => documents.Table(table);

    public byte[] Excel(TableDocument table) => spreadsheets.Export(table);
}
