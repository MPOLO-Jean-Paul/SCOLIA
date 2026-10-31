using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.Discipline;
using Scolia.Domain.Finance;
using Scolia.Domain.School;
using Scolia.Domain.Students;

namespace Scolia.Application.Services;

/// <summary>Tableau de bord, liste des débiteurs, balance âgée et exports.</summary>
public sealed class ReportService(IAppDbContext db, IClock clock, AccountService accounts, IDocumentService documents, ISpreadsheetService spreadsheets)
{
    /// <summary>
    /// Vue exclusivement agrégée du promoteur : aucune ligne nominative n'est renvoyée.
    /// Les encaissements par mois se fondent sur la DATE du reçu validé (flux de trésorerie).
    /// La dette par mois suit la PÉRIODE de frais et son solde net (comptabilité scolaire).
    /// Ces deux dimensions ne doivent jamais être confondues pour les paiements en retard.
    /// </summary>
    public async Task<PromoterDashboardDto> PromoterDashboardAsync(CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var today = clock.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var accountsByEnrollment = await accounts.ComputeAsync(year.Id, null, today, ct);
        var accountRows = accountsByEnrollment.Values.ToList();
        var active = accountRows.Where(a => a.Enrollment.Status == EnrollmentStatus.Actif).ToList();
        var periods = year.Periods.OrderBy(p => p.Order).ToList();
        var promotions = await db.Promotions.AsNoTracking().Where(p => p.AcademicYearId == year.Id)
            .OrderBy(p => p.Code).ToListAsync(ct);

        // Ne sélectionner ni les noms ni les contacts, mais seulement les montants nécessaires.
        var receipts = await (from receipt in db.Receipts.AsNoTracking()
                              join enrollment in db.Enrollments.AsNoTracking()
                                  on receipt.EnrollmentId equals enrollment.Id
                              where receipt.AcademicYearId == year.Id && receipt.Status == OperationStatus.Valide
                              select new { enrollment.PromotionId, receipt.Date, receipt.Amount }).ToListAsync(ct);

        var summaries = promotions.Select(p =>
        {
            var members = accountRows.Where(a => a.Promotion.Id == p.Id).ToList();
            var currentMembers = members.Where(a => a.Enrollment.Status == EnrollmentStatus.Actif).ToList();
            var money = receipts.Where(r => r.PromotionId == p.Id).ToList();

            var periodsStats = periods.Select(period =>
            {
                var first = new DateOnly(period.CalendarYear, period.Month, 1);
                var last = first.AddMonths(1).AddDays(-1);
                var lines = members.SelectMany(a => a.Account.Lines).Where(l => l.PeriodId == period.Id).ToList();
                return new PromoterPeriodDto(period.Id, $"{period.Name} {period.CalendarYear}",
                    money.Where(r => r.Date >= first && r.Date <= last).Sum(r => r.Amount),
                    lines.Sum(l => l.Net), lines.Sum(l => l.Paid), lines.Sum(l => l.Remaining));
            }).ToList();

            return new PromoterPromotionDto(p.Id, p.Code, p.Name, currentMembers.Count,
                members.Count(a => a.Account.Balance > 0), members.Count(a => a.Account.Overdue > 0),
                money.Where(r => r.Date == today).Sum(r => r.Amount),
                money.Where(r => r.Date >= monthStart && r.Date <= today).Sum(r => r.Amount),
                money.Sum(r => r.Amount), members.Sum(a => a.Account.Balance),
                members.Sum(a => a.Account.Overdue), periodsStats);
        }).ToList();

        // Toujours calculer la synthèse globale à partir des mêmes groupes : pas de divergence entre écran et totaux.
        return new PromoterDashboardDto(year.Label, today, active.Count,
            summaries.Sum(p => p.StudentsWithBalance), summaries.Sum(p => p.StudentsOverdue),
            summaries.Sum(p => p.CollectedToday), summaries.Sum(p => p.CollectedThisMonth),
            summaries.Sum(p => p.CollectedTotal), summaries.Sum(p => p.Outstanding),
            summaries.Sum(p => p.Overdue), summaries);
    }

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

    /// <summary>
    /// La journée en un coup d'œil : reçus et dépenses du jour, arrêté de caisse, appel, renvois en cours,
    /// convocations de parents prévues et anniversaires des apprenants actifs.
    /// </summary>
    public async Task<TodayDto> TodayAsync(CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var today = clock.Today;

        var receipts = await db.Receipts.AsNoTracking().Where(r => r.Date == today && r.Status == OperationStatus.Valide)
            .Select(r => r.Amount).ToListAsync(ct);
        var expenses = (await db.Expenses.AsNoTracking().Where(e => e.Date == today && e.Status == OperationStatus.Valide)
            .Select(e => e.Amount).ToListAsync(ct)).Sum();
        var closed = await db.CashClosings.AsNoTracking().AnyAsync(c => c.Date == today, ct);
        var marked = await db.Attendance.AsNoTracking().CountAsync(a => a.Date == today, ct);

        var active = await (from e in db.Enrollments.AsNoTracking()
                            join s in db.Students.AsNoTracking() on e.StudentId equals s.Id
                            join p in db.Promotions.AsNoTracking() on e.PromotionId equals p.Id
                            where e.AcademicYearId == year.Id && e.Status == EnrollmentStatus.Actif
                            select new { EnrollmentId = e.Id, StudentId = s.Id, s.LastName, s.MiddleName, s.FirstName, s.BirthDate, p.Code }).ToListAsync(ct);
        var byEnrollment = active.ToDictionary(a => a.EnrollmentId);
        static string Name(string last, string middle, string first) => string.Join(' ', new[] { last, middle, first }.Where(x => x.Length > 0));

        var decisions = await db.DisciplineDecisions.AsNoTracking()
            .Where(d => d.AcademicYearId == year.Id && d.State == DecisionState.Validee && d.Measure != DisciplineMeasure.RenvoiDefinitif)
            .ToListAsync(ct);
        var suspended = decisions.Where(d => d.Measure == DisciplineMeasure.RenvoiTemporaire && d.FirstDay <= today && d.LastDay >= today)
            .Select(d => d.EnrollmentId).Distinct().Count();
        var appointments = decisions
            .Where(d => d.Measure == DisciplineMeasure.Convocation && d.AppointmentAt is { } at && DateOnly.FromDateTime(at) == today
                && byEnrollment.ContainsKey(d.EnrollmentId))
            .OrderBy(d => d.AppointmentAt)
            .Select(d =>
            {
                var a = byEnrollment[d.EnrollmentId];
                var state = d.Reception switch { ParentReception.Recu => "parent reçu", ParentReception.Absent => "parent absent", _ => "en attente" };
                return new TodayPersonDto(a.StudentId, Name(a.LastName, a.MiddleName, a.FirstName), a.Code, $"{d.AppointmentAt:HH:mm} · {state}");
            }).ToList();

        var birthdays = active.Where(a => a.BirthDate is { } b && b.Month == today.Month && b.Day == today.Day)
            .OrderBy(a => a.LastName).ThenBy(a => a.FirstName)
            .Select(a => new TodayPersonDto(a.StudentId, Name(a.LastName, a.MiddleName, a.FirstName), a.Code, $"{today.Year - a.BirthDate!.Value.Year} ans"))
            .ToList();

        return new TodayDto(today, receipts.Count, receipts.Sum(), expenses, closed, marked, suspended, appointments, birthdays);
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
                    s.GuardianName, s.ParentPhone, Reminders.WhatsApp(school, s, x.Account), Reminders.Sms(school, s, x.Account));
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
                s.BirthDate?.ToString("dd/MM/yyyy") ?? "", s.ParentPhone ?? ""]).ToList());
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

    /// <summary>Situation financière par promotion : effectif, attendu à ce jour, encaissé, reste et taux de recouvrement.</summary>
    public async Task<TableDocument> PromotionsTableAsync(CancellationToken ct = default)
    {
        var school = await YearContext.SchoolAsync(db, ct);
        var d = await DashboardAsync(null, ct);
        string M(decimal v) => Money.Display(v, school.CurrencySymbol);
        return new TableDocument(PaymentService.Header(school), "Situation financière par promotion", $"Année scolaire {d.AcademicYear} · situation au {clock.Today:dd/MM/yyyy}",
            ["Promotion", "Nom", "Effectif", "Attendu à ce jour", "Encaissé", "Reste à percevoir", "Reste exigible", "Recouvrement"],
            d.Promotions.Select(p => (IReadOnlyList<string>)[p.Code, p.Name, p.Students.ToString(), M(p.Expected), M(p.Collected), M(p.Outstanding), M(p.Overdue),
                p.RecoveryRate.ToString("0.0") + " %"]).ToList(),
            ["Total", "", d.Promotions.Sum(p => p.Students).ToString(), M(d.Promotions.Sum(p => p.Expected)), M(d.Promotions.Sum(p => p.Collected)),
                M(d.Promotions.Sum(p => p.Outstanding)), M(d.Promotions.Sum(p => p.Overdue)), d.RecoveryRate.ToString("0.0") + " %"], Landscape: true);
    }

    /// <summary>Liste générale des apprenants de l'année, toutes promotions, avec leur solde.</summary>
    public async Task<TableDocument> StudentsTableAsync(CancellationToken ct = default)
    {
        var school = await YearContext.SchoolAsync(db, ct);
        var year = await YearContext.CurrentAsync(db, ct);
        var map = await accounts.ComputeAsync(year.Id, null, clock.Today, ct);
        var rows = map.Values
            .OrderBy(x => x.Promotion.Code).ThenBy(x => x.Enrollment.Student!.LastName).ThenBy(x => x.Enrollment.Student!.FirstName)
            .ToList();
        string M(decimal v) => Money.Display(v, school.CurrencySymbol);
        return new TableDocument(PaymentService.Header(school), "Liste générale des apprenants", $"Année scolaire {year.Label} · {rows.Count} apprenant(s)",
            ["Matricule", "Nom complet", "Sexe", "Promotion", "Statut", "Contact parent", "Payé", "Solde", "Reste exigible"],
            rows.Select(x => (IReadOnlyList<string>)[x.Enrollment.Student!.Matricule, x.Enrollment.Student!.FullName, x.Enrollment.Student!.Gender.ToString(),
                x.Promotion.Code, x.Enrollment.Status.ToString(), x.Enrollment.Student!.ParentPhone ?? "", M(x.Account.TotalPaid), M(x.Account.Balance), M(x.Account.Overdue)]).ToList(),
            ["", "Total", "", "", "", "", M(rows.Sum(x => x.Account.TotalPaid)), M(rows.Sum(x => x.Account.Balance)), M(rows.Sum(x => x.Account.Overdue))], Landscape: true);
    }

    /// <summary>Registre des dépenses de la période, annulées comprises, avec le total des dépenses valides.</summary>
    public async Task<TableDocument> ExpensesTableAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var school = await YearContext.SchoolAsync(db, ct);
        var q = db.Expenses.AsNoTracking().AsQueryable();
        if (from is not null) q = q.Where(e => e.Date >= from);
        if (to is not null) q = q.Where(e => e.Date <= to);
        var list = await q.OrderBy(e => e.Date).ThenBy(e => e.Number).ToListAsync(ct);
        var period = from is null && to is null ? "Toutes dates" : $"Du {from:dd/MM/yyyy} au {to:dd/MM/yyyy}";
        string M(decimal v) => Money.Display(v, school.CurrencySymbol);
        return new TableDocument(PaymentService.Header(school), "Registre des dépenses", $"{period} · {list.Count} dépense(s)",
            ["Date", "N°", "Catégorie", "Bénéficiaire", "Motif", "Mode", "Statut", "Montant"],
            list.Select(e => (IReadOnlyList<string>)[e.Date.ToString("dd/MM/yyyy"), e.Number, e.Category, e.Beneficiary, e.Description, Labels.Of(e.Method),
                e.Status == OperationStatus.Valide ? "Valide" : "Annulée", M(e.Amount)]).ToList(),
            ["", "", "", "", "", "", "Total valide", M(list.Where(e => e.Status == OperationStatus.Valide).Sum(e => e.Amount))], Landscape: true);
    }

    /// <summary>États proposés par le filtre du suivi mensuel, tels que l'écran les envoie.</summary>
    private static string? StatusFilter(string? etat) => etat?.Trim().ToLowerInvariant() switch
    {
        "paye" or "payes" => "Payé",
        "nonpaye" or "non-paye" or "impaye" => "Non payé",
        "retard" => "En retard",
        "partiel" => "Partiel",
        "couvert" => "Pris en charge",
        _ => null,
    };

    /// <summary>
    /// Suivi des paiements d'un mois : chaque apprenant inscrit (sorti exclu) avec le montant du mois, ce qu'il a payé,
    /// son état et le dernier reçu qui a réglé ce mois. Les totaux portent sur tous les apprenants de la promotion choisie,
    /// le filtre d'état et la recherche ne réduisent que la liste.
    /// </summary>
    public async Task<MonthlyFollowUpDto> MonthlyAsync(MonthlyQuery q, CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var school = await YearContext.SchoolAsync(db, ct);
        var today = clock.Today;
        var periods = year.Periods.OrderBy(p => p.Order).ToList();
        if (periods.Count == 0) throw new NotFoundException("Aucun mois n'est défini pour l'année scolaire en cours.");
        var current = periods.LastOrDefault(p => new DateOnly(p.CalendarYear, p.Month, 1) <= today) ?? periods[0];
        var period = q.PeriodeId is { } id ? periods.FirstOrDefault(p => p.Id == id) ?? throw new NotFoundException("Mois introuvable.") : current;
        string Name(AcademicPeriod p) => $"{p.Name} {p.CalendarYear}";

        var map = await accounts.ComputeAsync(year.Id, null, today, ct);
        var rows = map.Values
            .Where(x => x.Enrollment.Status != EnrollmentStatus.Sorti && (q.PromotionId is null || x.Promotion.Id == q.PromotionId))
            .Select(x => (Entry: x, Line: x.Account.Lines.FirstOrDefault(l => l.PeriodId == period.Id)))
            .Where(x => x.Line is not null)
            .ToList();

        // Dernier reçu valide qui a réglé la mensualité de ce mois, pour chaque inscription.
        var feeIds = rows.Select(x => x.Line!.FeeItemId).Distinct().ToList();
        var enrollmentIds = rows.Select(x => x.Entry.Enrollment.Id).ToList();
        var receipts = await (from a in db.ReceiptAllocations.AsNoTracking()
                              join r in db.Receipts.AsNoTracking() on a.ReceiptId equals r.Id
                              where r.Status == OperationStatus.Valide && feeIds.Contains(a.FeeItemId) && enrollmentIds.Contains(r.EnrollmentId)
                              select new { r.EnrollmentId, r.Number, r.Date, r.CreatedAtUtc }).ToListAsync(ct);
        var lastReceipt = receipts.GroupBy(r => r.EnrollmentId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Date).ThenByDescending(r => r.CreatedAtUtc).First());

        static string Status(AccountLine line) => line.Status switch
        {
            LineStatus.Paye => "Payé",
            LineStatus.Couvert => "Pris en charge",
            LineStatus.Partiel => "Partiel",
            _ => line.IsOverdue ? "En retard" : "Non payé",
        };

        var all = rows.Select(x =>
        {
            var s = x.Entry.Enrollment.Student!;
            var line = x.Line!;
            var receipt = lastReceipt.GetValueOrDefault(x.Entry.Enrollment.Id);
            return new MonthlyRowDto(s.Id, x.Entry.Enrollment.Id, s.Matricule, s.FullName, x.Entry.Promotion.Code, line.Net, line.Paid, line.Remaining,
                Status(line), receipt?.Number, receipt?.Date, s.ParentPhone,
                Reminders.MonthWhatsApp(school, s, Name(period), line.Remaining), Reminders.MonthSms(school, s, Name(period), line.Remaining));
        }).OrderBy(r => r.PromotionCode).ThenBy(r => r.FullName).ToList();

        var wanted = StatusFilter(q.Etat);
        var search = TextKey.Normalize(q.Q ?? string.Empty);
        var list = all
            .Where(r => wanted is null || r.Status == wanted || (wanted == "Non payé" && r.Status is "En retard" or "Partiel"))
            .Where(r => search.Length == 0 || TextKey.Normalize($"{r.Matricule} {r.FullName}").Contains(search, StringComparison.Ordinal))
            .ToList();

        return new MonthlyFollowUpDto(period.Id, Name(period), periods.Select(p => new PeriodChoiceDto(p.Id, Name(p), p.Id == current.Id)).ToList(),
            all.Count(r => r.Status == "Payé"), all.Count(r => r.Status is "Non payé" or "En retard" or "Partiel"), all.Count(r => r.Status == "En retard"),
            all.Count(r => r.Status == "Pris en charge"), all.Sum(r => r.Amount), all.Sum(r => r.Paid), all.Sum(r => r.Remaining), list);
    }

    /// <summary>Le suivi d'un mois en document : liste filtrée et totaux.</summary>
    public async Task<TableDocument> MonthlyTableAsync(MonthlyQuery q, CancellationToken ct = default)
    {
        var school = await YearContext.SchoolAsync(db, ct);
        var d = await MonthlyAsync(q, ct);
        string M(decimal v) => Money.Display(v, school.CurrencySymbol);
        var scope = q.PromotionId is { } promotionId
            ? $"promotion {await db.Promotions.AsNoTracking().Where(p => p.Id == promotionId).Select(p => p.Code).FirstOrDefaultAsync(ct)}"
            : "toutes les promotions";
        return new TableDocument(PaymentService.Header(school), $"Suivi des paiements · {d.Period}",
            $"{scope} · {d.PaidCount} payé(s), {d.UnpaidCount} non payé(s) dont {d.LateCount} en retard · situation au {clock.Today:dd/MM/yyyy}",
            ["Matricule", "Nom complet", "Promotion", "Montant du mois", "Payé", "Reste", "État", "Reçu", "Payé le", "Contact"],
            d.Rows.Select(r => (IReadOnlyList<string>)[r.Matricule, r.FullName, r.PromotionCode, M(r.Amount), M(r.Paid), M(r.Remaining), r.Status,
                r.ReceiptNumber ?? "", r.PaidOn?.ToString("dd/MM/yyyy") ?? "", r.GuardianPhone ?? ""]).ToList(),
            ["", "Total", "", M(d.Rows.Sum(r => r.Amount)), M(d.Rows.Sum(r => r.Paid)), M(d.Rows.Sum(r => r.Remaining)), "", "", "", ""], Landscape: true);
    }

    public byte[] Pdf(TableDocument table) => documents.Table(table);

    public byte[] Excel(TableDocument table) => spreadsheets.Export(table);
}
