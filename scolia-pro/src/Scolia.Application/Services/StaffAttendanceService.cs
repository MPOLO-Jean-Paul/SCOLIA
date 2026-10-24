using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Common;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.Security;
using Scolia.Domain.Staff;

namespace Scolia.Application.Services;

/// <summary>
/// Pointage des enseignants et du personnel (version 79) : signatures d'entrée et de sortie, retards à partir de 08:00:00,
/// justifications, conversion des retards en absences par semaine et bilan pour la paie.
/// </summary>
public sealed class StaffAttendanceService(IAppDbContext db, IWriteGate gate, IAuditLog audit, IClock clock, ICurrentUser user)
{
    private const int MaxRangeDays = 366;

    /// <summary>Signe l'entrée ou la sortie du jour. L'heure est celle du poste, au moment de la signature.</summary>
    public Task<StaffMarkDto> MarkAsync(Guid staffId, string kind, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var member = await db.Staff.FirstOrDefaultAsync(s => s.Id == staffId, ct) ?? throw new NotFoundException("Membre du personnel introuvable.");
        var today = clock.Today;
        if (!StaffAttendanceRules.InActivity(member, today))
            throw new DomainException("pointage", "Ce membre n'est pas en activité aujourd'hui : la signature n'est pas possible.");
        var mark = EnumText.Parse<StaffMark>(kind, "kind");
        var todays = await db.StaffMarks.Where(m => m.StaffMemberId == staffId && m.Date == today).ToListAsync(ct);
        if (mark == StaffMark.Entree)
        {
            if (todays.Any(m => m.Kind == StaffMark.Entree)) throw new ConflictException("L'entrée est déjà signée aujourd'hui.");
        }
        else
        {
            if (todays.All(m => m.Kind != StaffMark.Entree)) throw new DomainException("pointage", "La sortie ne peut être signée qu'après une entrée le même jour.");
            if (todays.Any(m => m.Kind == StaffMark.Sortie)) throw new ConflictException("La sortie est déjà signée aujourd'hui.");
        }

        var now = clock.UtcNow;
        var local = TimeOnly.FromDateTime(now.ToLocalTime());
        var signature = new StaffAttendanceMark
        {
            StaffMemberId = member.Id, Date = today, Kind = mark, Time = local,
            UtcOffsetMinutes = (int)TimeZoneInfo.Local.GetUtcOffset(now).TotalMinutes, AccountName = user.Name,
            IsLate = mark == StaffMark.Entree && StaffAttendanceRules.IsLate(local),
        };
        db.StaffMarks.Add(signature);
        audit.Record("pointage.signature", "Personnel", member.Id.ToString(),
            $"{(mark == StaffMark.Entree ? "Entrée" : "Sortie")} signée à {local:HH\\:mm\\:ss}{(signature.IsLate ? " (en retard)" : string.Empty)}.");
        await db.SaveChangesAsync(ct);
        return new StaffMarkDto(signature.Id, mark.ToString(), local.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), signature.IsLate);
    }, ct);

    /// <summary>Justifie une journée d'absence. Le motif est obligatoire et la date ne peut pas être future.</summary>
    public Task<StaffJustificationDto> JustifyAsync(Guid staffId, DateOnly date, string reason, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var member = await db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.Id == staffId, ct) ?? throw new NotFoundException("Membre du personnel introuvable.");
        if (date > clock.Today) throw new DomainException("pointage", "Une absence future ne se justifie pas.");
        var motif = StaffAttendanceRules.Reason(reason);
        if (await db.StaffJustifications.AnyAsync(j => j.StaffMemberId == staffId && j.Date == date && j.CancelReason == null, ct))
            throw new ConflictException("Cette journée est déjà justifiée.");
        var justification = new StaffDayJustification { StaffMemberId = staffId, Date = date, Reason = motif, AccountName = user.Name };
        db.StaffJustifications.Add(justification);
        audit.Record("pointage.justification", "Personnel", staffId.ToString(), $"Journée du {date:dd/MM/yyyy} justifiée : {motif}");
        await db.SaveChangesAsync(ct);
        return new StaffJustificationDto(justification.Id, date, motif, justification.AccountName, null);
    }, ct);

    /// <summary>Annule une justification. Le motif initial reste dans le registre, avec le motif d'annulation.</summary>
    public Task<StaffJustificationDto> CancelJustificationAsync(Guid id, string reason, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var justification = await db.StaffJustifications.FirstOrDefaultAsync(j => j.Id == id, ct) ?? throw new NotFoundException("Justification introuvable.");
        if (!justification.IsActive) throw new ConflictException("Cette justification est déjà annulée.");
        justification.CancelReason = StaffAttendanceRules.Reason(reason);
        audit.Record("pointage.annulation", "Personnel", justification.StaffMemberId.ToString(), $"Justification du {justification.Date:dd/MM/yyyy} annulée : {justification.CancelReason}");
        await db.SaveChangesAsync(ct);
        return new StaffJustificationDto(justification.Id, justification.Date, justification.Reason, justification.AccountName, justification.CancelReason);
    }, ct);

    /// <summary>Journées d'un membre sur une période, avec leur statut et les conversions de retards.</summary>
    public async Task<IReadOnlyList<StaffDayDto>> DaysAsync(Guid staffId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        CheckRange(from, to);
        var member = await db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.Id == staffId, ct) ?? throw new NotFoundException("Membre du personnel introuvable.");
        var marks = await db.StaffMarks.AsNoTracking().Where(m => m.StaffMemberId == staffId && m.Date >= from && m.Date <= to).ToListAsync(ct);
        var justified = await JustifiedDatesAsync(staffId, from, to, ct);
        return Build(member, marks, justified, from, to, clock.Today).Days;
    }

    /// <summary>Bilan de tous les membres actifs : présences, retards, absences et jours déductibles pour la paie.</summary>
    public async Task<IReadOnlyList<StaffBilanDto>> BilanAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        CheckRange(from, to);
        var members = await db.Staff.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.LastName).ToListAsync(ct);
        var ids = members.Select(m => m.Id).ToList();
        var marks = await db.StaffMarks.AsNoTracking().Where(m => ids.Contains(m.StaffMemberId) && m.Date >= from && m.Date <= to).ToListAsync(ct);
        var justifications = await db.StaffJustifications.AsNoTracking()
            .Where(j => ids.Contains(j.StaffMemberId) && j.Date >= from && j.Date <= to && j.CancelReason == null).ToListAsync(ct);
        var today = clock.Today;
        return members.Select(member =>
        {
            var mine = marks.Where(m => m.StaffMemberId == member.Id).ToList();
            var justified = justifications.Where(j => j.StaffMemberId == member.Id).Select(j => j.Date).ToHashSet();
            var built = Build(member, mine, justified, from, to, today);
            return new StaffBilanDto(member.Id, member.FullName, built.Present, built.Late, built.Absent, built.Justified, built.Conversions, built.Deductible);
        }).ToList();
    }

    private async Task<HashSet<DateOnly>> JustifiedDatesAsync(Guid staffId, DateOnly from, DateOnly to, CancellationToken ct) =>
        (await db.StaffJustifications.AsNoTracking()
            .Where(j => j.StaffMemberId == staffId && j.Date >= from && j.Date <= to && j.CancelReason == null).Select(j => j.Date).ToListAsync(ct)).ToHashSet();

    private static void CheckRange(DateOnly from, DateOnly to)
    {
        if (to < from) throw new DomainException("pointage", "La fin de la période doit suivre son début.");
        if (to.DayNumber - from.DayNumber >= MaxRangeDays) throw new DomainException("pointage", "Une période ne peut pas dépasser un an.");
    }

    /// <summary>Calcule les journées puis les conversions, et en déduit le bilan. Une date n'est déduite qu'une fois.</summary>
    private static Built Build(StaffMember member, IReadOnlyCollection<StaffAttendanceMark> marks, ISet<DateOnly> justified, DateOnly from, DateOnly to, DateOnly today)
    {
        var byDate = marks.GroupBy(m => m.Date).ToDictionary(g => g.Key, g => g.ToList());
        var statuses = new List<(DateOnly Date, StaffDayStatus Status, StaffAttendanceMark? Entry, StaffAttendanceMark? Exit, bool Justified)>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            byDate.TryGetValue(day, out var signed);
            var entry = signed?.FirstOrDefault(m => m.Kind == StaffMark.Entree);
            var exit = signed?.FirstOrDefault(m => m.Kind == StaffMark.Sortie);
            var applicable = StaffAttendanceRules.IsWorkingDay(day) && StaffAttendanceRules.InActivity(member, day);
            var isJustified = justified.Contains(day);
            var status = StaffAttendanceRules.DayStatus(day, today, applicable, entry is not null, exit is not null, entry?.IsLate == true, isJustified);
            statuses.Add((day, status, entry, exit, isJustified));
        }

        // Seuls les retards de journées closes entrent dans la conversion.
        var closedLates = statuses.Where(s => s.Status == StaffDayStatus.PresentEnRetard && s.Date < today).Select(s => s.Date);
        var conversions = StaffAttendanceRules.Conversions(closedLates);
        var conversionDates = conversions.Select(c => c.Date).ToHashSet();
        var unjustifiedConversions = conversions.Where(c => !justified.Contains(c.Date)).Select(c => c.Date);
        var deductible = statuses.Where(s => s.Status == StaffDayStatus.Absent).Select(s => s.Date).Concat(unjustifiedConversions).Distinct().Count();

        var days = statuses.Select(s => new StaffDayDto(
            s.Date, Label(s.Status), s.Entry?.Time.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            s.Exit?.Time.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), s.Entry?.IsLate == true, s.Justified,
            conversionDates.Contains(s.Date), conversionDates.Contains(s.Date) && s.Justified)).ToList();
        return new Built(
            days,
            statuses.Count(s => s.Status is StaffDayStatus.Present),
            statuses.Count(s => s.Status is StaffDayStatus.PresentEnRetard),
            statuses.Count(s => s.Status is StaffDayStatus.Absent),
            statuses.Count(s => s.Status is StaffDayStatus.Justifiee),
            conversions.Count,
            deductible);
    }

    private static string Label(StaffDayStatus status) => status switch
    {
        StaffDayStatus.EnAttente => "En attente",
        StaffDayStatus.Present => "Présent",
        StaffDayStatus.PresentEnRetard => "Présent en retard",
        StaffDayStatus.Absent => "Absent",
        StaffDayStatus.Justifiee => "Absence justifiée",
        _ => "—",
    };

    private sealed record Built(IReadOnlyList<StaffDayDto> Days, int Present, int Late, int Absent, int Justified, int Conversions, int Deductible);
}
