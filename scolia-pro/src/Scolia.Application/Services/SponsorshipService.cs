using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.Finance;
using Scolia.Domain.School;
using Scolia.Domain.Students;

namespace Scolia.Application.Services;

/// <summary>
/// Couvertures de frais. Les pourcentages historiques restent lisibles ; les nouvelles
/// prises en charge se configurent par type, sans saisir de pourcentage par mois.
/// </summary>
public sealed class SponsorshipService(IAppDbContext db, IWriteGate gate, IAuditLog audit, IClock clock, AccountService accounts)
{
    private static string Label(SponsorshipMode mode) => mode switch
    {
        SponsorshipMode.Complete => "Complète",
        SponsorshipMode.PartielleMensuelle => "Partielle",
        _ => "Historique"
    };

    public async Task<IReadOnlyList<SponsorshipDto>> ListAsync(Guid enrollmentId, CancellationToken ct = default)
    {
        var data = await accounts.ForEnrollmentAsync(enrollmentId, null, ct);
        var school = await YearContext.SchoolAsync(db, ct);
        var paid = data.Account.Lines.ToDictionary(l => l.FeeItemId, l => l.Paid);
        var fees = data.Promotion.Fees.ToDictionary(f => f.Id);
        var list = await db.Sponsorships.AsNoTracking().Include(s => s.Lines)
            .Where(s => s.EnrollmentId == enrollmentId).OrderByDescending(s => s.EffectiveFrom).ToListAsync(ct);
        return list.Select(s =>
        {
            var lines = fees.Values.Select(fee =>
            {
                var configured = SponsorshipCoverage.AmountFor(fee, s, school.WholeAmountsOnly);
                // Les paiements déjà enregistrés ne sont jamais annulés par une
                // prise en charge complète : le garant assume seulement le reste.
                var amount = s.Mode == SponsorshipMode.Complete
                    ? Math.Max(0m, fee.Amount - paid.GetValueOrDefault(fee.Id))
                    : Math.Min(fee.Amount, configured);
                var percentage = s.Mode == SponsorshipMode.Historique
                    ? s.Lines.FirstOrDefault(l => l.FeeItemId == fee.Id)?.Percent ?? 0m
                    : fee.Amount > 0 ? decimal.Round(amount * 100m / fee.Amount, 2) : 0m;
                return new SponsorshipLineDto(fee.Id, fee.Label, percentage, amount);
            }).Where(l => l.Amount > 0).OrderBy(l => l.Label).ToList();
            return new SponsorshipDto(s.Id, s.EnrollmentId, s.SponsorName, s.Reason,
                s.EffectiveFrom, s.IsActive, lines, lines.Sum(l => l.Amount), Label(s.Mode), s.MonthlyAmount);
        }).ToList();
    }

    public async Task<IReadOnlyList<SponsorshipListItem>> AllAsync(CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var school = await YearContext.SchoolAsync(db, ct);
        var rows = await (from s in db.Sponsorships.AsNoTracking()
                          join e in db.Enrollments.AsNoTracking() on s.EnrollmentId equals e.Id
                          join st in db.Students.AsNoTracking() on e.StudentId equals st.Id
                          join p in db.Promotions.AsNoTracking() on e.PromotionId equals p.Id
                          where e.AcademicYearId == year.Id
                          select new { SponsorshipId = s.Id, EnrollmentId = e.Id, st.Matricule, st.LastName, st.MiddleName,
                              st.FirstName, p.Code, s.SponsorName, s.Reason, s.EffectiveFrom, s.IsActive })
                          .ToListAsync(ct);
        if (rows.Count == 0) return [];
        var ids = rows.Select(r => r.SponsorshipId).ToList();
        var sponsors = await db.Sponsorships.AsNoTracking().Include(s => s.Lines)
            .Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var map = await accounts.ComputeAsync(year.Id, rows.Select(r => r.EnrollmentId).Distinct().ToList(), null, ct);
        return rows.Select(r =>
        {
            var sponsor = sponsors[r.SponsorshipId];
            var data = map[r.EnrollmentId];
            var paid = data.Account.Lines.ToDictionary(l => l.FeeItemId, l => l.Paid);
            var amount = data.Promotion.Fees.Sum(f =>
            {
                var configured = SponsorshipCoverage.AmountFor(f, sponsor, school.WholeAmountsOnly);
                return sponsor.Mode == SponsorshipMode.Complete
                    ? Math.Max(0m, f.Amount - paid.GetValueOrDefault(f.Id))
                    : Math.Min(f.Amount, configured);
            });
            return new SponsorshipListItem(r.SponsorshipId, r.EnrollmentId, r.Matricule,
                $"{r.LastName} {r.MiddleName} {r.FirstName}", r.Code, r.SponsorName, r.Reason,
                r.EffectiveFrom, r.IsActive, amount, Label(sponsor.Mode), sponsor.MonthlyAmount);
        }).ToList();
    }

    public Task<SponsorshipDto> UpsertAsync(Guid enrollmentId, Guid? id, SponsorshipUpsert input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        if (!Enum.TryParse<SponsorshipMode>(input.Type ?? nameof(SponsorshipMode.Historique), true, out var mode) ||
            !Enum.IsDefined(mode))
            throw new DomainException("type", "Choisissez une prise en charge complète ou partielle.");

        var v = new ValidationBuilder()
            .Require(!string.IsNullOrWhiteSpace(input.SponsorName) && input.SponsorName.Length <= 160, "sponsorName", "Indiquez le garant ou l'organisme (160 caractères maximum).")
            .Require(!string.IsNullOrWhiteSpace(input.Reason) && input.Reason.Length <= 1000, "reason", "Le motif est obligatoire.");
        if (mode == SponsorshipMode.Historique)
        {
            v.Require(input.Lines is { Count: > 0 }, "lines", "Choisissez au moins une rubrique couverte.");
            foreach (var line in input.Lines ?? [])
                v.Require(line.Percent is > 0 and <= 100, "lines", "Chaque pourcentage doit être compris entre 1 et 100.");
            v.Require(input.Lines is null || input.Lines.Select(l => l.FeeItemId).Distinct().Count() == input.Lines.Count,
                "lines", "Une rubrique figure deux fois.");
        }
        else
        {
            v.Require(input.Lines is null || input.Lines.Count == 0, "lines", "Ce mode ne demande aucun pourcentage.");
            if (mode == SponsorshipMode.PartielleMensuelle)
                v.Require(input.MonthlyAmount is > 0, "monthlyAmount", "Précisez le montant pris en charge par mois.");
        }
        v.ThrowIfInvalid();

        var data = await accounts.ForEnrollmentAsync(enrollmentId, null, ct);
        var school = await YearContext.SchoolAsync(db, ct);
        var fees = data.Promotion.Fees.ToDictionary(f => f.Id);
        var monthly = fees.Values.Where(f => f.Kind == FeeKind.Mensualite && f.Amount > 0).ToList();
        var monthlyAmount = mode == SponsorshipMode.PartielleMensuelle ? input.MonthlyAmount!.Value : 0m;
        if (mode == SponsorshipMode.PartielleMensuelle)
        {
            if (monthly.Count == 0) throw new DomainException("monthlyAmount", "Aucune mensualité n'est configurée pour cette promotion.");
            if (monthly.Any(f => monthlyAmount > f.Amount))
                throw new DomainException("monthlyAmount", "Le montant mensuel pris en charge ne peut dépasser la plus petite mensualité.");
            if (school.WholeAmountsOnly && monthlyAmount != decimal.Truncate(monthlyAmount))
                throw new DomainException("monthlyAmount", "Saisissez un montant entier dans la monnaie de l'établissement.");
        }
        foreach (var l in input.Lines ?? [])
            if (!fees.ContainsKey(l.FeeItemId)) throw new DomainException("lines", "Une rubrique n'appartient pas à cette promotion.");

        Sponsorship sponsorship;
        if (id is null)
        {
            sponsorship = new Sponsorship { EnrollmentId = enrollmentId };
            db.Sponsorships.Add(sponsorship);
        }
        else
        {
            sponsorship = await db.Sponsorships.Include(s => s.Lines)
                .FirstOrDefaultAsync(s => s.Id == id && s.EnrollmentId == enrollmentId, ct)
                ?? throw new NotFoundException("Prise en charge introuvable.");
        }

        var others = await db.Sponsorships.AsNoTracking().Include(s => s.Lines)
            .Where(s => s.EnrollmentId == enrollmentId && s.IsActive && s.Id != sponsorship.Id).ToListAsync(ct);
        if (mode == SponsorshipMode.Complete && others.Count > 0 ||
            others.Any(s => s.Mode == SponsorshipMode.Complete))
            throw new DomainException("type", "Une couverture complète ne peut se superposer à une autre prise en charge active.");

        // Conflits : ne jamais effacer un paiement existant, dépasser le coût d'une
        // mensualité ni réécrire les montants d'autres garants.
        var provisional = new Sponsorship
        {
            Mode = mode,
            MonthlyAmount = monthlyAmount,
            IsActive = true,
            Lines = (input.Lines ?? []).Select(l => new SponsorshipLine { FeeItemId = l.FeeItemId, Percent = l.Percent }).ToList()
        };
        foreach (var fee in fees.Values)
        {
            if (mode == SponsorshipMode.Complete) continue;
            var paid = data.Account.Lines.FirstOrDefault(l => l.FeeItemId == fee.Id)?.Paid ?? 0m;
            var alreadyCovered = others.Sum(s => SponsorshipCoverage.AmountFor(fee, s, school.WholeAmountsOnly));
            var proposed = SponsorshipCoverage.AmountFor(fee, provisional, school.WholeAmountsOnly);
            if (alreadyCovered + proposed > Math.Max(0m, fee.Amount - paid))
                throw new DomainException("monthlyAmount", $"« {fee.Label} » : prise en charge supérieure au montant restant non payé.");
        }

        sponsorship.SponsorName = input.SponsorName.Trim();
        sponsorship.Reason = input.Reason.Trim();
        sponsorship.EffectiveFrom = input.EffectiveFrom;
        sponsorship.IsActive = true;
        sponsorship.Mode = mode;
        sponsorship.MonthlyAmount = monthlyAmount;
        foreach (var old in sponsorship.Lines.ToList()) db.SponsorshipLines.Remove(old);
        sponsorship.Lines.Clear();
        if (mode == SponsorshipMode.Historique)
            foreach (var line in input.Lines!)
                sponsorship.Lines.Add(new SponsorshipLine
                { SponsorshipId = sponsorship.Id, FeeItemId = line.FeeItemId, Percent = line.Percent });
        audit.Record(id is null ? "priseencharge.creee" : "priseencharge.modifiee", "PriseEnCharge", sponsorship.Id.ToString(),
            $"{data.Enrollment.Student!.Matricule} : {Label(mode).ToLowerInvariant()} par {sponsorship.SponsorName}.",
            new { motif = input.Reason, type = Label(mode), montantMensuel = monthlyAmount });
        await db.SaveChangesAsync(ct);
        return (await ListAsync(enrollmentId, ct)).First(s => s.Id == sponsorship.Id);
    }, ct);

    public Task EndAsync(Guid enrollmentId, Guid id, string reason, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("reason", "Précisez le motif de fin de prise en charge.");
        var s = await db.Sponsorships.FirstOrDefaultAsync(x => x.Id == id && x.EnrollmentId == enrollmentId, ct)
            ?? throw new NotFoundException("Prise en charge introuvable.");
        s.IsActive = false;
        audit.Record("priseencharge.terminee", "PriseEnCharge", id.ToString(),
            $"Prise en charge par {s.SponsorName} terminée le {clock.Today:dd/MM/yyyy}.", new { motif = reason });
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);
}
