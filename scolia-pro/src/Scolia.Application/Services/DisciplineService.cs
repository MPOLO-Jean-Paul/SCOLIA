using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Common;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.Discipline;
using Scolia.Domain.School;
using Scolia.Domain.Security;
using Scolia.Domain.Students;

namespace Scolia.Application.Services;

/// <summary>
/// Registre disciplinaire des apprenants : renvois temporaires et définitifs, convocations, règle des deux renvois,
/// effets sur le statut, historique et convocation imprimable. Les décisions ne sont jamais effacées ni réécrites.
/// </summary>
public sealed class DisciplineService(IAppDbContext db, IWriteGate gate, IAuditLog audit, IClock clock, ICurrentUser user)
{
    private static readonly IReadOnlyList<(DisciplineMeasure Value, string Label)> MeasureLabels =
    [
        (DisciplineMeasure.RenvoiTemporaire, "Renvoi temporaire"), (DisciplineMeasure.RenvoiDefinitif, "Renvoi définitif"), (DisciplineMeasure.Convocation, "Convocation"),
    ];

    private static readonly IReadOnlyList<(ParentReception Value, string Label)> ReceptionLabels =
    [
        (ParentReception.EnAttente, "En attente"), (ParentReception.Recu, "Parent reçu"), (ParentReception.Absent, "Parent absent"),
    ];

    public static string MeasureText(DisciplineMeasure measure) => MeasureLabels.First(m => m.Value == measure).Label;

    /// <summary>Registre de l'année courante. Les archives se consultent par l'historique d'un apprenant.</summary>
    public async Task<IReadOnlyList<DisciplineRowDto>> RegistryAsync(Guid? promotionId, string? measure, CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var query = db.DisciplineDecisions.AsNoTracking().Where(d => d.AcademicYearId == year.Id);
        if (!string.IsNullOrWhiteSpace(measure))
        {
            var wanted = EnumText.Parse(measure, "measure", MeasureLabels);
            query = query.Where(d => d.Measure == wanted);
        }
        if (promotionId is { } promotion)
        {
            var enrollmentIds = db.Enrollments.AsNoTracking().Where(e => e.PromotionId == promotion).Select(e => e.Id);
            query = query.Where(d => enrollmentIds.Contains(d.EnrollmentId));
        }
        return await RowsAsync(query, ct);
    }

    /// <summary>Historique complet d'un apprenant, toutes années confondues.</summary>
    public async Task<IReadOnlyList<DisciplineRowDto>> HistoryAsync(Guid enrollmentId, CancellationToken ct = default) =>
        await RowsAsync(db.DisciplineDecisions.AsNoTracking().Where(d => d.EnrollmentId == enrollmentId), ct);

    public Task<DisciplineRowDto> RegisterAsync(DisciplineRequest input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var year = await OpenYearAsync(ct);
        var measure = EnumText.Parse(input.Measure, "measure", MeasureLabels);
        var reason = DisciplineRules.Reason(input.Reason);
        var today = clock.Today;
        var decisionDate = input.DecisionDate ?? today;
        var enrollment = await db.Enrollments.Include(e => e.Student).FirstOrDefaultAsync(e => e.Id == input.EnrollmentId, ct)
            ?? throw new NotFoundException("Inscription introuvable.");
        if (enrollment.AcademicYearId != year.Id)
            throw new DomainException("discipline", "Cette inscription n'appartient pas à l'année scolaire courante.");
        var history = await db.DisciplineDecisions.Where(d => d.EnrollmentId == enrollment.Id).ToListAsync(ct);

        // Répétition d'un même enregistrement : on renvoie la décision existante, sans nouvelle sanction.
        if (input.RequestKey is { Length: > 0 } key && history.FirstOrDefault(d => d.RequestKey == key) is { } repeated)
            return await RowAsync(repeated.Id, ct);
        if (history.FirstOrDefault(d => d.State == DecisionState.Validee && d.Measure == measure && d.DecisionDate == decisionDate
                && d.FirstDay == input.FirstDay && d.LastDay == input.LastDay && d.Reason == reason) is { } same)
            return await RowAsync(same.Id, ct);

        var decision = new DisciplineDecision
        {
            EnrollmentId = enrollment.Id, AcademicYearId = year.Id, Measure = measure, Reason = reason, DecisionDate = decisionDate,
            RecordedByName = user.Name, RequestKey = string.IsNullOrWhiteSpace(input.RequestKey) ? null : input.RequestKey,
            ParentName = enrollment.Student!.GuardianName, ParentPhone = enrollment.Student.GuardianPhone,
        };

        switch (measure)
        {
            case DisciplineMeasure.Convocation:
                if (input.AppointmentAt is not { } appointment)
                    throw new DomainException("discipline", "Indiquez la date et l'heure du rendez-vous.");
                if (DateOnly.FromDateTime(appointment) < decisionDate)
                    throw new DomainException("discipline", "Le rendez-vous ne peut pas précéder la décision.");
                decision.AppointmentAt = appointment;
                break;

            case DisciplineMeasure.RenvoiTemporaire:
            case DisciplineMeasure.RenvoiDefinitif:
                if (history.Any(DisciplineRules.IsDefinitive))
                    throw new ConflictException("Cet apprenant est déjà renvoyé définitivement : seule une convocation reste possible.");
                var definitiveRequested = measure == DisciplineMeasure.RenvoiDefinitif;
                if (definitiveRequested)
                {
                    DisciplineRules.ValidateDecisionDate(decisionDate, today);
                    if (history.Any(d => DisciplineRules.IsExclusion(d) && d.EffectiveFrom > decisionDate))
                        throw new DomainException("discipline", "Un renvoi plus récent existe déjà : ce renvoi, saisi avant lui, est refusé pour garder la chronologie.");
                    if (!input.ConfirmDefinitive)
                        throw new ConflictException("Le renvoi définitif est irréversible sans annulation explicite. Confirmez pour l'enregistrer.");
                    break;
                }
                if (input.FirstDay is not { } first || input.LastDay is not { } last)
                    throw new DomainException("discipline", "Indiquez le premier et le dernier jour du renvoi.");
                DisciplineRules.ValidateExclusion(decisionDate, first, last, year, today, history);
                // Le compteur des renvois temporaires est propre à l'année scolaire.
                var validTemporary = history.Count(d => DisciplineRules.IsExclusion(d) && d.Measure == DisciplineMeasure.RenvoiTemporaire && d.AcademicYearId == year.Id);
                if (DisciplineRules.BecomesDefinitive(measure, validTemporary))
                {
                    if (!input.ConfirmDefinitive)
                        throw new ConflictException("Ce deuxième renvoi temporaire de l'année entraîne un renvoi définitif automatique. Confirmez pour l'enregistrer.");
                    decision.Measure = DisciplineMeasure.RenvoiDefinitif;
                    decision.IsAutomatic = true;
                    decision.Reason = $"Deuxième renvoi temporaire de l'année : renvoi définitif automatique. {reason}";
                }
                else
                {
                    decision.FirstDay = first;
                    decision.LastDay = last;
                }
                break;
        }

        db.DisciplineDecisions.Add(decision);
        audit.Record("discipline.decision", "RegistreDiscipline", decision.Id.ToString(),
            $"{MeasureText(decision.Measure)} pour {enrollment.Student.Matricule}" + (decision.IsAutomatic ? " (automatique)" : string.Empty));
        await db.SaveChangesAsync(ct);
        return await RowAsync(decision.Id, ct);
    }, ct);

    /// <summary>Enregistre la réception du parent ou son absence, avec un compte rendu.</summary>
    public Task<DisciplineRowDto> ReceptionAsync(Guid id, DisciplineReceptionInput input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var year = await OpenYearAsync(ct);
        var decision = await LoadInYearAsync(id, year, ct);
        var reception = EnumText.Parse(input.Reception, "reception", ReceptionLabels);
        var note = input.Note?.Trim();
        DisciplineRules.ValidateReception(decision, reception, note, clock.UtcNow.ToLocalTime());
        decision.Reception = reception;
        decision.ReceptionNote = note;
        decision.ReceptionAtUtc = clock.UtcNow;
        audit.Record("discipline.reception", "RegistreDiscipline", id.ToString(), $"Convocation : {reception}.");
        await db.SaveChangesAsync(ct);
        return await RowAsync(id, ct);
    }, ct);

    /// <summary>Annule une décision en gardant sa trace. Réservé au secrétariat ; le motif est obligatoire.</summary>
    public Task<DisciplineRowDto> CancelAsync(Guid id, DisciplineCancelInput input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var year = await OpenYearAsync(ct);
        var decision = await LoadInYearAsync(id, year, ct);
        if (decision.State == DecisionState.Annulee) throw new ConflictException("Cette décision est déjà annulée.");
        decision.State = DecisionState.Annulee;
        decision.CancelReason = DisciplineRules.Reason(input.Reason);
        decision.CancelledAtUtc = clock.UtcNow;
        decision.CancelledByName = user.Name;
        audit.Record("discipline.annulation", "RegistreDiscipline", id.ToString(), $"Décision annulée : {decision.CancelReason}");
        await db.SaveChangesAsync(ct);
        return await RowAsync(id, ct);
    }, ct);

    /// <summary>Convocation à remettre au parent. Toutes les valeurs saisies sont échappées.</summary>
    public async Task<string> ConvocationHtmlAsync(Guid id, CancellationToken ct = default)
    {
        var row = await RowAsync(id, ct);
        if (row.Measure != MeasureText(DisciplineMeasure.Convocation)) throw new DomainException("discipline", "Seule une convocation s'imprime.");
        var school = await db.Schools.AsNoTracking().Select(s => s.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
        var appointment = row.AppointmentAt is { } at ? at.ToString("dddd dd MMMM yyyy 'à' HH:mm", new System.Globalization.CultureInfo("fr-FR")) : "À fixer";
        return $$"""
            <!doctype html>
            <html lang="fr"><head><meta charset="utf-8"><title>Convocation {{H(row.FullName)}}</title>
            <style>
              body { font-family: Arial, sans-serif; color: #1d2733; margin: 40px; line-height: 1.5; }
              h1 { font-size: 20px; text-align: center; margin-bottom: 4px; }
              .etab { text-align: center; color: #5b6878; margin-bottom: 28px; }
              .ligne { margin: 8px 0; }
              .motif { border-left: 3px solid #2f6fd6; padding: 8px 12px; margin: 16px 0; background: #f4f7fc; }
              .signatures { display: flex; justify-content: space-between; margin-top: 60px; }
              .signatures div { width: 42%; border-top: 1px solid #1d2733; padding-top: 6px; text-align: center; }
              @media print { button { display: none; } }
            </style></head>
            <body>
              <p class="etab">{{H(school)}}</p>
              <h1>CONVOCATION DU PARENT</h1>
              <p class="ligne">Le parent de <strong>{{H(row.FullName)}}</strong> (matricule {{H(row.Matricule)}}, promotion {{H(row.PromotionCode)}}) est invité à se présenter.</p>
              <p class="ligne">Parent : {{H(row.ParentName ?? "non renseigné")}} {{H(row.ParentPhone)}}</p>
              <p class="ligne">Rendez-vous : <strong>{{H(appointment)}}</strong></p>
              <div class="motif"><strong>Motif :</strong> {{H(row.Reason)}}</div>
              <p class="ligne">Décision du {{H(row.DecisionDate.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture))}}.</p>
              <div class="signatures"><div>Le parent</div><div>La direction</div></div>
              <p><button onclick="window.print()">Imprimer / enregistrer en PDF</button></p>
            </body></html>
            """;
    }

    /// <summary>Export CSV des lignes affichées, séparateur point-virgule pour Excel en français.</summary>
    public async Task<string> CsvAsync(Guid? promotionId, string? measure, CancellationToken ct = default)
    {
        var rows = await RegistryAsync(promotionId, measure, ct);
        var sb = new StringBuilder();
        sb.AppendLine("Matricule;Nom complet;Promotion;Mesure;Motif;Date de décision;Premier jour;Dernier jour;Durée;Retour autorisé;Rendez-vous;Réception;Statut;Statut effectif");
        foreach (var r in rows)
        {
            string[] cells =
            [
                r.Matricule, r.FullName, r.PromotionCode, r.Measure, r.Reason, Fr(r.DecisionDate), Fr(r.FirstDay), Fr(r.LastDay),
                r.Days?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, Fr(r.ReturnFrom),
                r.AppointmentAt?.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                r.Reception, r.State, r.EffectiveStatus,
            ];
            sb.AppendLine(string.Join(';', cells.Select(Csv)));
        }
        return sb.ToString();
    }

    private static string Fr(DateOnly? date) => date is { } d ? d.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

    private static string Csv(string value) => value.Contains(';') || value.Contains('"') || value.Contains('\n')
        ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
        : value;

    private async Task<AcademicYear> OpenYearAsync(CancellationToken ct)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        if (year.Status == AcademicYearStatus.Closed)
            throw new ConflictException("L'année est clôturée : le registre disciplinaire se consulte sans modification.");
        return year;
    }

    private async Task<DisciplineDecision> LoadInYearAsync(Guid id, AcademicYear year, CancellationToken ct)
    {
        var decision = await db.DisciplineDecisions.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Décision introuvable.");
        if (decision.AcademicYearId != year.Id)
            throw new DomainException("discipline", "Les décisions des années précédentes sont consultables uniquement.");
        return decision;
    }

    private async Task<DisciplineRowDto> RowAsync(Guid id, CancellationToken ct) =>
        (await RowsAsync(db.DisciplineDecisions.AsNoTracking().Where(d => d.Id == id), ct)).Single();

    private async Task<List<DisciplineRowDto>> RowsAsync(IQueryable<DisciplineDecision> query, CancellationToken ct)
    {
        var items = await (from d in query
                           join e in db.Enrollments.AsNoTracking() on d.EnrollmentId equals e.Id
                           join s in db.Students.AsNoTracking() on e.StudentId equals s.Id
                           join p in db.Promotions.AsNoTracking() on e.PromotionId equals p.Id
                           join y in db.AcademicYears.AsNoTracking() on d.AcademicYearId equals y.Id
                           orderby d.CreatedAtUtc descending
                           select new RowSource(d, e.Status, s.Matricule, s.LastName + " " + s.MiddleName + " " + s.FirstName, p.Code, y.Label))
                          .Take(500).ToListAsync(ct);
        var enrollmentIds = items.Select(i => i.Decision.EnrollmentId).Distinct().ToList();
        var allDecisions = await db.DisciplineDecisions.AsNoTracking().Where(x => enrollmentIds.Contains(x.EnrollmentId)).ToListAsync(ct);
        var today = clock.Today;
        var now = clock.UtcNow.ToLocalTime();
        return items.Select(i =>
        {
            var d = i.Decision;
            var history = allDecisions.Where(x => x.EnrollmentId == d.EnrollmentId).ToList();
            var effective = DisciplineRules.EffectiveStatus(i.BaseStatus, history, today);
            var isConvocation = d.Measure == DisciplineMeasure.Convocation;
            var awaiting = isConvocation && d.State == DecisionState.Validee && d.Reception == ParentReception.EnAttente;
            return new DisciplineRowDto(
                d.Id, d.EnrollmentId, i.Matricule, i.FullName.Trim(), i.PromotionCode, MeasureText(d.Measure), d.Reason, d.DecisionDate,
                d.FirstDay, d.LastDay, d.FirstDay is { } f && d.LastDay is { } l ? DisciplineRules.Days(f, l) : null,
                d.Measure == DisciplineMeasure.RenvoiTemporaire && d.LastDay is { } last ? DisciplineRules.ReturnFrom(last) : null,
                d.AppointmentAt, d.IsAutomatic, d.ParentName, d.ParentPhone,
                ReceptionLabels.First(r => r.Value == d.Reception).Label, d.ReceptionNote,
                d.State == DecisionState.Validee ? "Validée" : "Annulée", d.CancelReason, d.RecordedByName,
                Labels.Of(effective), i.YearLabel,
                awaiting, awaiting && d.AppointmentAt is { } at && now >= at, d.State == DecisionState.Validee);
        }).ToList();
    }

    private sealed record RowSource(DisciplineDecision Decision, EnrollmentStatus BaseStatus, string Matricule, string FullName, string PromotionCode, string YearLabel);
}
