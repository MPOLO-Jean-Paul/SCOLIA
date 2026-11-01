using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Academics;
using Scolia.Domain.Common;
using Scolia.Domain.Finance;
using Scolia.Domain.Students;

namespace Scolia.Application.Services;

/// <summary>Chiffres clés du rapport d'une journée : ce qui s'affiche dans la bannière et le message d'envoi.</summary>
public sealed record DailyReportSummary(DateOnly Date, int Receipts, decimal Collected, decimal Expenses, int Absences, string Currency, string SchoolName)
{
    public string Text => $"{Receipts} reçu(s), {Money.Display(Collected, Currency)} encaissés, {Money.Display(Expenses, Currency)} de dépenses, {Absences} absence(s).";
}

/// <summary>
/// Rapport journalier : encaissements, dépenses, inscriptions et présences d'une journée, détaillés par promotion,
/// en un PDF prêt à envoyer. Le même rapport sert au téléchargement, à l'envoi manuel et à l'envoi automatique.
/// </summary>
public sealed class DailyReportService(IAppDbContext db, AccountService accounts, IDocumentService documents)
{
    public static string FileName(DateOnly day) => $"rapport-journalier-{day:yyyy-MM-dd}.pdf";

    public async Task<(byte[] Pdf, DailyReportSummary Summary)> BuildAsync(DateOnly day, CancellationToken ct = default)
    {
        var school = await YearContext.SchoolAsync(db, ct);
        var year = await YearContext.CurrentAsync(db, ct);
        var cur = school.CurrencySymbol;
        string M(decimal v) => Money.Display(v, cur);

        var receipts = await (from r in db.Receipts.AsNoTracking()
                              join e in db.Enrollments.AsNoTracking() on r.EnrollmentId equals e.Id
                              join p in db.Promotions.AsNoTracking() on e.PromotionId equals p.Id
                              join s in db.Students.AsNoTracking() on r.StudentId equals s.Id
                              where r.Date == day
                              orderby r.Number
                              select new
                              {
                                  r.Number, r.Amount, r.Method, r.Status, r.CashierName, r.StudentId, PromotionId = p.Id, p.Code,
                                  s.Matricule, s.LastName, s.MiddleName, s.FirstName,
                              }).ToListAsync(ct);
        var valid = receipts.Where(r => r.Status == OperationStatus.Valide).ToList();
        var expenses = await db.Expenses.AsNoTracking().Where(e => e.Date == day && e.Status == OperationStatus.Valide)
            .OrderBy(e => e.Number).Select(e => new { e.Number, e.Category, e.Beneficiary, e.Amount }).ToListAsync(ct);
        var closed = await db.CashClosings.AsNoTracking().AnyAsync(c => c.Date == day, ct);
        var attendance = await (from a in db.Attendance.AsNoTracking()
                                join e in db.Enrollments.AsNoTracking() on a.EnrollmentId equals e.Id
                                where a.Date == day
                                select new { e.PromotionId, a.Status }).ToListAsync(ct);
        var enrolled = await (from e in db.Enrollments.AsNoTracking()
                              join s in db.Students.AsNoTracking() on e.StudentId equals s.Id
                              join p in db.Promotions.AsNoTracking() on e.PromotionId equals p.Id
                              where e.AcademicYearId == year.Id && e.EnrolledOn == day
                              orderby p.Code, s.LastName
                              select new { s.Matricule, s.LastName, s.MiddleName, s.FirstName, p.Code }).ToListAsync(ct);
        var map = await accounts.ComputeAsync(year.Id, null, day, ct);
        static string Name(string last, string middle, string first) => string.Join(' ', new[] { last, middle, first }.Where(x => x.Length > 0));

        // Par promotion : effectif actif, reçus du jour, apprenants qui ont payé, encaissé, reste échu à cette date, absences.
        var promotions = map.Values.GroupBy(x => x.Promotion).OrderBy(g => g.Key.Code).Select(g =>
        {
            var mine = valid.Where(r => r.PromotionId == g.Key.Id).ToList();
            var absences = attendance.Count(a => a.PromotionId == g.Key.Id && a.Status is AttendanceStatus.Absent);
            return new
            {
                g.Key.Code, g.Key.Name,
                Active = g.Count(x => x.Enrollment.Status == EnrollmentStatus.Actif),
                Receipts = mine.Count, Payers = mine.Select(r => r.StudentId).Distinct().Count(), Collected = mine.Sum(r => r.Amount),
                Overdue = g.Sum(x => x.Account.Overdue), Absences = absences,
            };
        }).ToList();

        var collected = valid.Sum(r => r.Amount);
        var spent = expenses.Sum(e => e.Amount);
        var absent = attendance.Count(a => a.Status == AttendanceStatus.Absent);
        var late = attendance.Count(a => a.Status == AttendanceStatus.Retard);
        var cancelled = receipts.Count - valid.Count;

        var sections = new List<ReportSection>
        {
            new("Encaissements par promotion",
                ["Promotion", "Effectif", "Reçus", "Payeurs", "Encaissé", "Reste échu", "Absents"],
                promotions.Select(p => (IReadOnlyList<string>)[$"{p.Code} · {p.Name}", p.Active.ToString(), p.Receipts.ToString(), p.Payers.ToString(),
                    M(p.Collected), M(p.Overdue), p.Absences.ToString()]).ToList(),
                ["Total", promotions.Sum(p => p.Active).ToString(), valid.Count.ToString(), valid.Select(r => r.StudentId).Distinct().Count().ToString(),
                    M(collected), M(promotions.Sum(p => p.Overdue)), absent.ToString()],
                "Aucune promotion pour l'année en cours."),
            new("Par mode de paiement", ["Mode", "Reçus", "Montant"],
                valid.GroupBy(r => r.Method).OrderByDescending(g => g.Sum(r => r.Amount))
                    .Select(g => (IReadOnlyList<string>)[Labels.Of(g.Key), g.Count().ToString(), M(g.Sum(r => r.Amount))]).ToList(),
                valid.Count > 0 ? ["Total", valid.Count.ToString(), M(collected)] : null,
                "Aucun encaissement ce jour."),
            new("Détail des reçus", ["N°", "Apprenant", "Promotion", "Mode", "Caissier", "État", "Montant"],
                receipts.Select(r => (IReadOnlyList<string>)[r.Number, $"{r.Matricule} · {Name(r.LastName, r.MiddleName, r.FirstName)}", r.Code,
                    Labels.Of(r.Method), r.CashierName, r.Status == OperationStatus.Valide ? "Valide" : "Annulé", M(r.Amount)]).ToList(),
                receipts.Count > 0 ? ["Total valide", "", "", "", "", "", M(collected)] : null,
                "Aucun reçu ce jour."),
            new("Dépenses", ["N°", "Catégorie", "Bénéficiaire", "Montant"],
                expenses.Select(e => (IReadOnlyList<string>)[e.Number, e.Category, e.Beneficiary, M(e.Amount)]).ToList(),
                expenses.Count > 0 ? ["Total", "", "", M(spent)] : null,
                "Aucune dépense ce jour."),
            new("Nouvelles inscriptions", ["Matricule", "Apprenant", "Promotion"],
                enrolled.Select(e => (IReadOnlyList<string>)[e.Matricule, Name(e.LastName, e.MiddleName, e.FirstName), e.Code]).ToList(),
                null, "Aucune inscription ce jour."),
        };

        var figures = new List<(string, string)>
        {
            ("Encaissé", M(collected)),
            ("Reçus valides", valid.Count.ToString()),
            ("Dépenses", M(spent)),
            ("Solde du jour", M(collected - spent)),
            ("Reçus annulés", cancelled.ToString()),
            ("Inscriptions", enrolled.Count.ToString()),
            ("Absences", late > 0 ? $"{absent} · {late} retard(s)" : absent.ToString()),
            ("Caisse", closed ? "Arrêtée" : "Non arrêtée"),
        };
        var culture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        var date = day.ToString("dddd d MMMM yyyy", culture);
        var document = new ReportDocument(PaymentService.Header(school), "Rapport journalier",
            $"{char.ToUpper(date[0], culture)}{date[1..]} · année scolaire {year.Label}", figures, sections,
            "Rapport établi automatiquement par SCOLIA à partir des opérations validées de la journée.");
        return (documents.Report(document), new DailyReportSummary(day, valid.Count, collected, spent, absent, cur, school.Name));
    }
}
