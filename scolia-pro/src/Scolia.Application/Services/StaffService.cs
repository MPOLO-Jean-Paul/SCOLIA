using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Common;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.Finance;
using Scolia.Domain.Staff;
using Scolia.Domain.Students;

namespace Scolia.Application.Services;

/// <summary>Personnel et paie mensuelle. Le paiement d'un bulletin crée la dépense correspondante.</summary>
public sealed class StaffService(IAppDbContext db, IWriteGate gate, IAuditLog audit, IClock clock, ICurrentUser user)
{
    public async Task<IReadOnlyList<StaffDto>> ListAsync(CancellationToken ct = default) =>
        (await db.Staff.AsNoTracking().OrderBy(s => s.LastName).ToListAsync(ct)).Select(ToDto).ToList();

    private static StaffDto ToDto(StaffMember s) => new(s.Id, s.Number, s.LastName, s.MiddleName, s.FirstName, s.Gender.ToString(), s.Function, s.Phone, s.Email,
        s.HireDate, s.BaseSalary, s.IsActive);

    public Task<StaffDto> UpsertAsync(Guid? id, StaffUpsert input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var v = new ValidationBuilder()
            .Require(!string.IsNullOrWhiteSpace(input.LastName), "lastName", "Nom obligatoire.")
            .Require(!string.IsNullOrWhiteSpace(input.FirstName), "firstName", "Prénom obligatoire.")
            .Require(!string.IsNullOrWhiteSpace(input.Function), "function", "Fonction obligatoire.")
            .Require(input.BaseSalary >= 0, "baseSalary", "Le salaire de base ne peut pas être négatif.");
        string? phone = null;
        try { phone = IdentityRules.NormalizePhone(input.Phone); } catch (DomainException e) { v.Add("phone", e.Message); }
        v.ThrowIfInvalid();
        StaffMember member;
        if (id is null)
        {
            var count = await db.Staff.CountAsync(ct);
            member = new StaffMember { Number = $"PER-{count + 1:000}" };
            while (await db.Staff.AnyAsync(s => s.Number == member.Number, ct)) member.Number = $"PER-{++count + 1:000}";
            db.Staff.Add(member);
        }
        else member = await db.Staff.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Membre du personnel introuvable.");
        member.LastName = input.LastName.Trim().ToUpperInvariant(); member.MiddleName = (input.MiddleName ?? "").Trim().ToUpperInvariant();
        member.FirstName = TextKey.ProperName(input.FirstName); member.Gender = EnumText.Parse<Gender>(input.Gender, "gender");
        member.Function = input.Function.Trim(); member.Phone = phone; member.Email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim();
        member.HireDate = input.HireDate; member.BaseSalary = input.BaseSalary; member.IsActive = input.IsActive;
        audit.Record(id is null ? "personnel.cree" : "personnel.modifie", "Personnel", member.Id.ToString(), $"{member.Number} {member.FullName} ({member.Function}).");
        await db.SaveChangesAsync(ct);
        return ToDto(member);
    }, ct);

    public static string NormalizePeriod(string period)
    {
        if (period is not { Length: 7 } || period[4] != '-' || !int.TryParse(period[..4], out var y) || !int.TryParse(period[5..], out var m) || m is < 1 or > 12 || y < 2000)
            throw new DomainException("period", "Mois de paie attendu au format AAAA-MM.");
        return period;
    }

    public async Task<IReadOnlyList<PayrollDto>> PayrollAsync(string period, CancellationToken ct = default)
    {
        period = NormalizePeriod(period);
        var rows = await (from p in db.Payroll.AsNoTracking()
                          join s in db.Staff.AsNoTracking() on p.StaffMemberId equals s.Id
                          where p.Period == period
                          orderby s.LastName
                          select new { p, s }).ToListAsync(ct);
        return rows.Select(x => ToDto(x.p, x.s)).ToList();
    }

    private static PayrollDto ToDto(PayrollEntry p, StaffMember s) => new(p.Id, s.Id, s.Number, s.FullName, s.Function, p.Period, p.BaseSalary, p.Allowances,
        p.Deductions, p.Advances, p.Net, p.Status.ToString(), p.PaidOn, p.Note);

    /// <summary>Prépare les bulletins du mois pour tout le personnel actif (sans doublon).</summary>
    public Task<int> GenerateAsync(string period, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        period = NormalizePeriod(period);
        var existing = await db.Payroll.Where(p => p.Period == period).Select(p => p.StaffMemberId).ToListAsync(ct);
        var staff = await db.Staff.Where(s => s.IsActive && !existing.Contains(s.Id)).ToListAsync(ct);
        foreach (var s in staff)
        {
            var entry = new PayrollEntry { StaffMemberId = s.Id, Period = period, BaseSalary = s.BaseSalary };
            entry.Recompute();
            db.Payroll.Add(entry);
        }
        audit.Record("paie.preparee", "Paie", period, $"{staff.Count} bulletin(s) préparé(s) pour {period}.");
        await db.SaveChangesAsync(ct);
        return staff.Count;
    }, ct);

    public Task<PayrollDto> UpdateEntryAsync(Guid id, PayrollUpdate input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var entry = await db.Payroll.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Bulletin introuvable.");
        if (entry.Status == PayrollStatus.Paye) throw new DomainException("paie", "Un bulletin payé ne peut plus être modifié.");
        entry.Allowances = input.Allowances; entry.Deductions = input.Deductions; entry.Advances = input.Advances; entry.Note = input.Note;
        entry.Recompute();
        entry.Status = PayrollStatus.Valide;
        var s = await db.Staff.AsNoTracking().FirstAsync(x => x.Id == entry.StaffMemberId, ct);
        audit.Record("paie.modifiee", "Paie", entry.Id.ToString(), $"Bulletin de {s.FullName} modifié : net {entry.Net:N0} FC.");
        await db.SaveChangesAsync(ct);
        return ToDto(entry, s);
    }, ct);

    public Task<PayrollDto> PayAsync(Guid id, string method, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var entry = await db.Payroll.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Bulletin introuvable.");
        if (entry.Status == PayrollStatus.Paye) throw new ConflictException("Ce bulletin est déjà payé.");
        var s = await db.Staff.AsNoTracking().FirstAsync(x => x.Id == entry.StaffMemberId, ct);
        var year = await YearContext.CurrentAsync(db, ct);
        var lastClosing = await PaymentService.LastClosingAsync(db, ct);
        if (lastClosing is not null && clock.Today <= lastClosing) throw new DomainException("date", "La caisse du jour est déjà arrêtée.");
        entry.Status = PayrollStatus.Paye;
        entry.PaidOn = clock.Today;
        if (entry.Net > 0)
            db.Expenses.Add(new Expense
            {
                Number = await PaymentService.NextNumberAsync(db, "DP", year.FirstCalendarYear, ct), Date = clock.Today, AcademicYearId = year.Id,
                Category = "Salaires", Beneficiary = s.FullName, Description = $"Salaire {entry.Period} ({s.Function})", Amount = entry.Net,
                Method = PaymentService.ParseMethod(method), RecordedByName = user.Name,
            });
        audit.Record("paie.payee", "Paie", id.ToString(), $"Salaire {entry.Period} de {s.FullName} payé : {entry.Net:N0}.");
        await db.SaveChangesAsync(ct);
        return ToDto(entry, s);
    }, ct);
}
