using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.Finance;
using Scolia.Domain.School;
using Scolia.Domain.Students;

namespace Scolia.Application.Services;

/// <summary>Calcul des comptes apprenants. Toutes les données d'une année sont chargées en
/// quelques requêtes puis calculées en mémoire : le coût reste linéaire avec le volume.</summary>
public sealed class AccountService(IAppDbContext db, IClock clock)
{
    public sealed record EnrollmentAccount(Enrollment Enrollment, Promotion Promotion, StudentAccount Account);

    public async Task<IReadOnlyDictionary<Guid, EnrollmentAccount>> ComputeAsync(Guid yearId, IReadOnlyCollection<Guid>? enrollmentIds = null,
        DateOnly? asOf = null, CancellationToken ct = default)
    {
        var school = await YearContext.SchoolAsync(db, ct);
        var day = asOf ?? clock.Today;
        var enrollmentsQuery = db.Enrollments.AsNoTracking().Include(e => e.Student).Where(e => e.AcademicYearId == yearId);
        if (enrollmentIds is not null) enrollmentsQuery = enrollmentsQuery.Where(e => enrollmentIds.Contains(e.Id));
        var enrollments = await enrollmentsQuery.ToListAsync(ct);
        if (enrollments.Count == 0) return new Dictionary<Guid, EnrollmentAccount>();

        var promotionIds = enrollments.Select(e => e.PromotionId).Distinct().ToList();
        var promotions = await db.Promotions.AsNoTracking().Include(p => p.Fees).Where(p => promotionIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var ids = enrollments.Select(e => e.Id).ToList();

        var coverage = await (from l in db.SponsorshipLines.AsNoTracking()
                              join s in db.Sponsorships.AsNoTracking() on l.SponsorshipId equals s.Id
                              where s.IsActive && ids.Contains(s.EnrollmentId)
                              select new { s.EnrollmentId, l.FeeItemId, l.Percent }).ToListAsync(ct);
        var coverageMap = coverage.GroupBy(c => c.EnrollmentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<Guid, decimal>)g.GroupBy(x => x.FeeItemId).ToDictionary(x => x.Key, x => x.Sum(y => y.Percent)));

        var paid = await (from a in db.ReceiptAllocations.AsNoTracking()
                          join r in db.Receipts.AsNoTracking() on a.ReceiptId equals r.Id
                          where r.Status == OperationStatus.Valide && ids.Contains(r.EnrollmentId)
                          select new { r.EnrollmentId, a.FeeItemId, a.Amount }).ToListAsync(ct);
        var paidMap = paid.GroupBy(p => p.EnrollmentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<Guid, decimal>)g.GroupBy(x => x.FeeItemId).ToDictionary(x => x.Key, x => x.Sum(y => y.Amount)));

        var empty = new Dictionary<Guid, decimal>();
        var result = new Dictionary<Guid, EnrollmentAccount>(enrollments.Count);
        foreach (var e in enrollments)
        {
            var promotion = promotions[e.PromotionId];
            // Le suivi de l'apprenant ne porte que sur la scolarité : inscription et examen sont des frais de l'institution.
            var fees = promotion.Fees.Where(f => f.Kind == FeeKind.Mensualite).Select(f => new FeeLineInput(f.Id, f.Label, f.Amount, f.DueDate, f.Order, f.PeriodId, f.Kind));
            var account = StudentAccount.Compute(fees, coverageMap.GetValueOrDefault(e.Id, empty), paidMap.GetValueOrDefault(e.Id, empty), day, school.WholeAmountsOnly);
            result[e.Id] = new EnrollmentAccount(e, promotion, account);
        }
        return result;
    }

    public async Task<EnrollmentAccount> ForEnrollmentAsync(Guid enrollmentId, DateOnly? asOf = null, CancellationToken ct = default)
    {
        var yearId = await db.Enrollments.AsNoTracking().Where(e => e.Id == enrollmentId).Select(e => (Guid?)e.AcademicYearId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Inscription introuvable.");
        var map = await ComputeAsync(yearId, [enrollmentId], asOf, ct);
        return map[enrollmentId];
    }

    public async Task<AccountDto> AccountDtoAsync(Guid enrollmentId, CancellationToken ct = default)
    {
        var school = await YearContext.SchoolAsync(db, ct);
        var data = await ForEnrollmentAsync(enrollmentId, null, ct);
        var year = await db.AcademicYears.AsNoTracking().FirstAsync(y => y.Id == data.Enrollment.AcademicYearId, ct);
        var receipts = await ReceiptsForAsync(enrollmentId, ct);
        var s = data.Enrollment.Student!;
        var phone = s.GuardianPhone ?? s.Phone;
        return new AccountDto(s.Id, enrollmentId, s.Matricule, s.FullName, data.Promotion.Code, year.Label, Labels.Of(data.Enrollment.Status),
            data.Account.Lines.Select(ToDto).ToList(), data.Account.TotalFees, data.Account.TotalCovered, data.Account.TotalNet,
            data.Account.TotalPaid, data.Account.Balance, data.Account.Overdue, data.Account.OverdueCount,
            Aging.Bucket(data.Account.OldestOverdueDate, clock.Today), receipts, phone,
            Reminders.WhatsApp(school, s, data.Account));
    }

    public static AccountLineDto ToDto(AccountLine l) =>
        new(l.FeeItemId, l.Label, l.DueDate, l.Amount, l.Covered, l.Net, l.Paid, l.Remaining, Labels.Of(l.Status), l.IsOverdue, l.Kind.ToString());

    public async Task<IReadOnlyList<ReceiptSummaryDto>> ReceiptsForAsync(Guid enrollmentId, CancellationToken ct = default)
    {
        var receipts = await db.Receipts.AsNoTracking().Include(r => r.Allocations).Where(r => r.EnrollmentId == enrollmentId).ToListAsync(ct);
        var itemIds = receipts.SelectMany(r => r.Allocations).Select(a => a.FeeItemId).Distinct().ToList();
        var labels = await db.FeeItems.AsNoTracking().Where(f => itemIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Label, ct);
        return receipts.OrderByDescending(r => r.Date).ThenByDescending(r => r.Number)
            .Select(r => new ReceiptSummaryDto(r.Id, r.Number, r.Date, r.Amount, Labels.Of(r.Method), r.Status == OperationStatus.Valide ? "Valide" : "Annulé", r.CashierName,
                string.Join(", ", r.Allocations.Select(a => labels.GetValueOrDefault(a.FeeItemId, "?")))))
            .ToList();
    }
}

/// <summary>Messages de rappel prêts à envoyer (WhatsApp / SMS), sans service payant.</summary>
public static class Reminders
{
    public static string Message(SchoolProfile school, Student student, StudentAccount account)
    {
        var periods = string.Join(", ", account.Lines.Where(l => l.IsOverdue).Select(l => l.Label));
        return $"Bonjour, {school.ShortName} vous rappelle que le dossier de {student.FullName} (matricule {student.Matricule}) présente un reste exigible de "
             + $"{Money.Display(account.Overdue, school.CurrencySymbol)}" + (periods.Length > 0 ? $" ({periods})" : string.Empty)
             + ". Merci de régulariser auprès de la caisse. Ce message est automatique.";
    }

    public static string? WhatsApp(SchoolProfile school, Student student, StudentAccount account)
    {
        if (account.Overdue <= 0) return null;
        var phone = IdentityRules.ToInternational(student.GuardianPhone ?? student.Phone, school.PhoneCountryCode);
        return phone is null ? null : $"https://wa.me/{phone}?text={Uri.EscapeDataString(Message(school, student, account))}";
    }

    public static string? Sms(SchoolProfile school, Student student, StudentAccount account)
    {
        if (account.Overdue <= 0) return null;
        var phone = student.GuardianPhone ?? student.Phone;
        return string.IsNullOrWhiteSpace(phone) ? null : $"sms:{phone}?body={Uri.EscapeDataString(Message(school, student, account))}";
    }
}
