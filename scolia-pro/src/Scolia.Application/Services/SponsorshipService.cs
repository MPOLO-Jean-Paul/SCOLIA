using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.Students;

namespace Scolia.Application.Services;

/// <summary>Prises en charge (bourses, sponsors, exonérations).</summary>
public sealed class SponsorshipService(IAppDbContext db, IWriteGate gate, IAuditLog audit, IClock clock, AccountService accounts)
{
    public async Task<IReadOnlyList<SponsorshipDto>> ListAsync(Guid enrollmentId, CancellationToken ct = default)
    {
        var data = await accounts.ForEnrollmentAsync(enrollmentId, null, ct);
        var labels = data.Promotion.Fees.ToDictionary(f => f.Id, f => (f.Label, f.Amount));
        var list = await db.Sponsorships.AsNoTracking().Include(s => s.Lines).Where(s => s.EnrollmentId == enrollmentId).OrderByDescending(s => s.EffectiveFrom).ToListAsync(ct);
        return list.Select(s =>
        {
            var lines = s.Lines.Select(l =>
            {
                var (label, amount) = labels.GetValueOrDefault(l.FeeItemId, ("?", 0m));
                return new SponsorshipLineDto(l.FeeItemId, label, l.Percent, decimal.Round(amount * l.Percent / 100m, 0));
            }).OrderBy(l => l.Label).ToList();
            return new SponsorshipDto(s.Id, s.EnrollmentId, s.SponsorName, s.Reason, s.EffectiveFrom, s.IsActive, lines, lines.Sum(l => l.Amount));
        }).ToList();
    }

    public async Task<IReadOnlyList<SponsorshipListItem>> AllAsync(CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var rows = await (from s in db.Sponsorships.AsNoTracking()
                          join e in db.Enrollments.AsNoTracking() on s.EnrollmentId equals e.Id
                          join st in db.Students.AsNoTracking() on e.StudentId equals st.Id
                          join p in db.Promotions.AsNoTracking() on e.PromotionId equals p.Id
                          where e.AcademicYearId == year.Id
                          select new { SponsorshipId = s.Id, EnrollmentId = e.Id, st.Matricule, st.LastName, st.MiddleName, st.FirstName, p.Code, s.SponsorName, s.Reason, s.EffectiveFrom, s.IsActive })
                          .ToListAsync(ct);
        var map = await accounts.ComputeAsync(year.Id, rows.Select(r => r.EnrollmentId).Distinct().ToList(), null, ct);
        return rows.Select(r => new SponsorshipListItem(r.SponsorshipId, r.EnrollmentId, r.Matricule, $"{r.LastName} {r.MiddleName} {r.FirstName}", r.Code,
            r.SponsorName, r.Reason, r.EffectiveFrom, r.IsActive, map.TryGetValue(r.EnrollmentId, out var a) ? a.Account.TotalCovered : 0)).ToList();
    }

    public Task<SponsorshipDto> UpsertAsync(Guid enrollmentId, Guid? id, SponsorshipUpsert input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var v = new ValidationBuilder()
            .Require(!string.IsNullOrWhiteSpace(input.SponsorName) && input.SponsorName.Length <= 160, "sponsorName", "Indiquez le garant ou l'organisme (160 caractères maximum).")
            .Require(!string.IsNullOrWhiteSpace(input.Reason) && input.Reason.Length <= 1000, "reason", "Le motif est obligatoire.")
            .Require(input.Lines is { Count: > 0 }, "lines", "Choisissez au moins une rubrique couverte.");
        foreach (var l in input.Lines ?? []) v.Require(l.Percent is > 0 and <= 100, "lines", "Chaque pourcentage doit être compris entre 1 et 100.");
        v.Require(input.Lines is null || input.Lines.Select(l => l.FeeItemId).Distinct().Count() == input.Lines.Count, "lines", "Une rubrique figure deux fois.");
        v.ThrowIfInvalid();

        var data = await accounts.ForEnrollmentAsync(enrollmentId, null, ct);
        var fees = data.Promotion.Fees.ToDictionary(f => f.Id);
        foreach (var l in input.Lines!) if (!fees.ContainsKey(l.FeeItemId)) throw new DomainException("lines", "Une rubrique n'appartient pas à la promotion de l'apprenant.");

        Sponsorship sponsorship;
        if (id is null)
        {
            sponsorship = new Sponsorship { EnrollmentId = enrollmentId };
            db.Sponsorships.Add(sponsorship);
        }
        else
        {
            sponsorship = await db.Sponsorships.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == id && s.EnrollmentId == enrollmentId, ct)
                ?? throw new NotFoundException("Prise en charge introuvable.");
        }

        // Couverture des autres prises en charge actives + nouvelle couverture : 100 % maximum,
        // et le net restant ne peut pas descendre sous ce qui a déjà été payé.
        var others = await db.SponsorshipLines.AsNoTracking()
            .Where(l => db.Sponsorships.Any(s => s.Id == l.SponsorshipId && s.EnrollmentId == enrollmentId && s.IsActive && s.Id != sponsorship.Id))
            .ToListAsync(ct);
        foreach (var l in input.Lines!)
        {
            var total = others.Where(o => o.FeeItemId == l.FeeItemId).Sum(o => o.Percent) + l.Percent;
            var fee = fees[l.FeeItemId];
            if (total > 100) throw new DomainException("lines", $"« {fee.Label} » serait couvert à plus de 100 %.");
            var line = data.Account.Lines.First(x => x.FeeItemId == l.FeeItemId);
            var net = fee.Amount - decimal.Round(fee.Amount * total / 100m, 0, MidpointRounding.AwayFromZero);
            if (line.Paid > net)
                throw new DomainException("lines", $"« {fee.Label} » a déjà été payé ({line.Paid:N0}) : la couverture ne peut pas dépasser le reste non payé.");
        }

        sponsorship.SponsorName = input.SponsorName.Trim();
        sponsorship.Reason = input.Reason.Trim();
        sponsorship.EffectiveFrom = input.EffectiveFrom;
        sponsorship.IsActive = true;
        foreach (var old in sponsorship.Lines.ToList()) db.SponsorshipLines.Remove(old);
        sponsorship.Lines.Clear();
        foreach (var l in input.Lines!) sponsorship.Lines.Add(new SponsorshipLine { SponsorshipId = sponsorship.Id, FeeItemId = l.FeeItemId, Percent = l.Percent });
        audit.Record(id is null ? "priseencharge.creee" : "priseencharge.modifiee", "PriseEnCharge", sponsorship.Id.ToString(),
            $"{data.Enrollment.Student!.Matricule} : prise en charge par {sponsorship.SponsorName} ({input.Lines!.Count} rubrique(s)).", new { motif = input.Reason });
        await db.SaveChangesAsync(ct);
        return (await ListAsync(enrollmentId, ct)).First(s => s.Id == sponsorship.Id);
    }, ct);

    public Task EndAsync(Guid enrollmentId, Guid id, string reason, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("reason", "Précisez le motif de fin de prise en charge.");
        var s = await db.Sponsorships.FirstOrDefaultAsync(x => x.Id == id && x.EnrollmentId == enrollmentId, ct) ?? throw new NotFoundException("Prise en charge introuvable.");
        s.IsActive = false;
        audit.Record("priseencharge.terminee", "PriseEnCharge", id.ToString(), $"Prise en charge par {s.SponsorName} terminée le {clock.Today:dd/MM/yyyy}.", new { motif = reason });
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);
}
