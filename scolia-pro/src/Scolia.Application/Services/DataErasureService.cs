using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Domain.Academics;
using Scolia.Domain.Common;
using Scolia.Domain.Discipline;
using Scolia.Domain.Finance;
using Scolia.Domain.School;
using Scolia.Domain.Students;

namespace Scolia.Application.Services;

/// <summary>
/// Effacement volontaire, par la secrétaire ou l'administrateur : les paiements, ou les dossiers des apprenants.
/// Portée : les apprenants sélectionnés, ou tous. Chaque effacement exige le mot de passe du compte connecté,
/// est précédé d'une sauvegarde (faite par l'appelant) et laisse une trace au journal.
/// </summary>
public sealed class DataErasureService(IAppDbContext db, IWriteGate gate, IAuditLog audit, ICurrentUser user, IPasswordService passwords)
{
    public const string Selection = "selection";
    public const string All = "tous";

    /// <summary>Vérifie le mot de passe du compte connecté. Un échec est journalisé ; rien n'est effacé.</summary>
    public Task<bool> ConfirmAsync(string? password, string action, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var id = user.Id ?? throw new DomainException("session", "Session expirée : reconnectez-vous.");
        var account = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("Utilisateur introuvable.");
        if (passwords.Verify(account.PasswordHash, password ?? string.Empty, out _)) return true;
        audit.Record("donnees.effacement.refuse", "Utilisateur", id.ToString(), $"Effacement refusé (mot de passe incorrect) : {action}.");
        await db.SaveChangesAsync(ct);
        return false;
    }, ct);

    /// <summary>Efface les reçus et leurs imputations, pour l'année courante. Les dossiers et les soldes restent.</summary>
    public Task<ErasureSummary> ErasePaymentsAsync(ErasureRequest input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        EnsureScope(input);
        var year = await YearContext.CurrentAsync(db, ct);
        if (year.Status == AcademicYearStatus.Closed)
            throw new DomainException("annee", $"L'année {year.Label} est clôturée : ses paiements ne peuvent plus être effacés.");

        var ids = input.Scope == All ? null : input.StudentIds!.ToList();
        IQueryable<Receipt> receipts = db.Receipts.Where(r => r.AcademicYearId == year.Id);
        if (ids is not null) receipts = receipts.Where(r => ids.Contains(r.StudentId));
        var receiptCount = await receipts.CountAsync(ct);
        // Les imputations partent avant leurs reçus.
        var allocations = await db.ReceiptAllocations.Where(a => receipts.Select(r => r.Id).Contains(a.ReceiptId)).ExecuteDeleteAsync(ct);
        await receipts.ExecuteDeleteAsync(ct);

        audit.Record("donnees.paiements.effaces", "Recu", null,
            $"{receiptCount} reçu(s) et {allocations} imputation(s) effacés pour {Describe(input)}, année {year.Label}.",
            new { receiptCount, allocations, input.Scope });
        await db.SaveChangesAsync(ct);
        return new ErasureSummary(receiptCount, allocations, 0, 0);
    }, ct);

    /// <summary>
    /// Efface les dossiers choisis, avec tout ce qui s'y rattache : inscriptions, reçus, notes, présences,
    /// discipline et prises en charge. Les autres apprenants et les réglages de l'établissement ne bougent pas.
    /// </summary>
    public Task<ErasureSummary> EraseStudentsAsync(ErasureRequest input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        EnsureScope(input);
        var ids = input.Scope == All ? null : input.StudentIds!.ToList();
        IQueryable<Student> students = db.Students;
        if (ids is not null) students = students.Where(s => ids.Contains(s.Id));
        IQueryable<Guid> studentIds = students.Select(s => s.Id);
        IQueryable<Enrollment> enrollments = db.Enrollments.Where(e => studentIds.Contains(e.StudentId));
        IQueryable<Guid> enrollmentIds = enrollments.Select(e => e.Id);
        IQueryable<Receipt> receipts = db.Receipts.Where(r => enrollmentIds.Contains(r.EnrollmentId) || studentIds.Contains(r.StudentId));
        IQueryable<Guid> receiptIds = receipts.Select(r => r.Id);
        IQueryable<Sponsorship> sponsorships = db.Sponsorships.Where(s => enrollmentIds.Contains(s.EnrollmentId));
        IQueryable<Guid> sponsorshipIds = sponsorships.Select(s => s.Id);

        var studentCount = await students.CountAsync(ct);
        var enrollmentCount = await enrollments.CountAsync(ct);
        var receiptCount = await receipts.CountAsync(ct);

        // Du plus dépendant au moins dépendant : aucune ligne ne reste orpheline, et les dossiers partent en dernier.
        await db.SponsorshipLines.Where(l => sponsorshipIds.Contains(l.SponsorshipId)).ExecuteDeleteAsync(ct);
        await sponsorships.ExecuteDeleteAsync(ct);
        await db.Grades.Where(g => enrollmentIds.Contains(g.EnrollmentId)).ExecuteDeleteAsync(ct);
        await db.Attendance.Where(a => enrollmentIds.Contains(a.EnrollmentId)).ExecuteDeleteAsync(ct);
        await db.DisciplineDecisions.Where(d => enrollmentIds.Contains(d.EnrollmentId)).ExecuteDeleteAsync(ct);
        await db.Incidents.Where(i => enrollmentIds.Contains(i.EnrollmentId)).ExecuteDeleteAsync(ct);
        var allocations = await db.ReceiptAllocations.Where(a => receiptIds.Contains(a.ReceiptId)).ExecuteDeleteAsync(ct);
        await receipts.ExecuteDeleteAsync(ct);
        await enrollments.ExecuteDeleteAsync(ct);
        await students.ExecuteDeleteAsync(ct);

        audit.Record("donnees.apprenants.effaces", "Apprenant", null,
            $"{studentCount} dossier(s) d'apprenant effacés ({Describe(input)}) : {enrollmentCount} inscription(s), {receiptCount} reçu(s), {allocations} imputation(s).",
            new { studentCount, enrollmentCount, receiptCount, allocations, input.Scope });
        await db.SaveChangesAsync(ct);
        return new ErasureSummary(receiptCount, allocations, enrollmentCount, studentCount);
    }, ct);

    private static void EnsureScope(ErasureRequest input)
    {
        if (input.Scope is not (Selection or All)) throw new DomainException("portee", "Choisissez les apprenants concernés, ou tous les apprenants.");
        if (input.Scope == Selection && input.StudentIds is not { Count: > 0 }) throw new DomainException("apprenants", "Sélectionnez au moins un apprenant.");
    }

    private static string Describe(ErasureRequest input) =>
        input.Scope == All ? "tous les apprenants" : $"{input.StudentIds!.Count} apprenant(s) sélectionné(s)";
}

/// <summary>Demande d'effacement : portée (« selection » ou « tous »), apprenants visés et mot de passe de confirmation.</summary>
public sealed record ErasureRequest(string Scope, IReadOnlyList<Guid>? StudentIds, string? Password);

/// <summary>Ce qui a été effacé.</summary>
public sealed record ErasureSummary(int Receipts, int Allocations, int Enrollments, int Students);
