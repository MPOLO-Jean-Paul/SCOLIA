using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.School;
using Scolia.Domain.Students;

namespace Scolia.Application.Services;

/// <summary>
/// Finalistes des examens nationaux (ENAFEP, fin du CTEB, Examen d'État) et saisie
/// des résultats communiqués par l'autorité. SCOLIA ne calcule jamais ces résultats.
/// </summary>
public sealed class NationalExamService(IAppDbContext db, IWriteGate gate, IAuditLog audit)
{
    private static NationalExam Parse(string? exam) =>
        Enum.TryParse<NationalExam>(exam, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed : throw new NotFoundException("Examen national inconnu.");

    public async Task<NationalExamDto> CandidatesAsync(string exam, CancellationToken ct = default)
    {
        var target = Parse(exam);
        var school = await YearContext.SchoolAsync(db, ct);
        if (!InstitutionProfiles.UsesSchoolCycles(school.InstitutionType))
            throw new DomainException("examenNational", "Les examens nationaux concernent le primaire et le secondaire.");
        var year = await YearContext.CurrentAsync(db, ct);
        var classes = await (from p in db.Promotions.AsNoTracking()
                             join c in db.SchoolCycles.AsNoTracking() on p.SchoolCycleId equals c.Id
                             where p.AcademicYearId == year.Id && p.IsActive && c.SchoolId == school.Id
                             select new { p.Id, p.Name, p.Level, c.Kind }).ToListAsync(ct);
        var ids = classes.Where(c => NationalExams.For(c.Kind, c.Level) == target).Select(c => c.Id).ToList();
        var names = classes.ToDictionary(c => c.Id, c => c.Name);
        var enrollments = await db.Enrollments.AsNoTracking().Include(e => e.Student)
            .Where(e => ids.Contains(e.PromotionId) && e.AcademicYearId == year.Id && e.Status == EnrollmentStatus.Actif)
            .ToListAsync(ct);
        var enrollmentIds = enrollments.Select(e => e.Id).ToList();
        var results = await db.NationalExamResults.AsNoTracking()
            .Where(r => r.Exam == target && enrollmentIds.Contains(r.EnrollmentId))
            .ToDictionaryAsync(r => r.EnrollmentId, ct);
        var rows = enrollments
            .OrderBy(e => names[e.PromotionId]).ThenBy(e => e.Student!.FullName, StringComparer.CurrentCultureIgnoreCase)
            .Select(e =>
            {
                var s = e.Student!;
                results.TryGetValue(e.Id, out var r);
                return new NationalExamCandidateDto(e.Id, names[e.PromotionId], s.Matricule, s.FullName,
                    s.Gender == Gender.F ? "F" : s.Gender == Gender.M ? "M" : "", s.BirthDate, s.BirthPlace,
                    r?.Passed, r?.Percent, r?.Reference);
            }).ToList();
        return new NationalExamDto(target.ToString(), NationalExams.Label(target), rows);
    }

    public async Task<TableDocument> TableAsync(string exam, CancellationToken ct = default)
    {
        var data = await CandidatesAsync(exam, ct);
        var school = await YearContext.SchoolAsync(db, ct);
        var year = await YearContext.CurrentAsync(db, ct);
        var rows = data.Candidates.Select((c, i) => (IReadOnlyList<string>)
        [
            (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), c.ClassName, c.Matricule, c.FullName,
            c.Gender, c.BirthDate?.ToString("dd/MM/yyyy") ?? "", c.BirthPlace ?? "", c.Reference ?? "",
            c.Passed switch { true => "Réussi", false => "Échec", _ => "" },
            c.Percent?.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) ?? "",
        ]).ToList();
        return new TableDocument(PaymentService.Header(school), $"Candidats — {data.Label}",
            $"Année scolaire {year.Label} · {data.Candidates.Count} candidat(s)",
            ["N°", "Classe", "Matricule", "Nom et prénom", "Sexe", "Né(e) le", "À", "N° candidat", "Résultat", "%"],
            rows, Landscape: true);
    }

    public Task<NationalExamCandidateDto> SaveAsync(string exam, Guid enrollmentId, NationalExamResultInput input,
        CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var target = Parse(exam);
        var data = await CandidatesAsync(exam, ct);
        if (data.Candidates.All(c => c.EnrollmentId != enrollmentId))
            throw new DomainException("examenNational", "Cet élève n'est pas candidat à cet examen cette année.");
        var row = await db.NationalExamResults.FirstOrDefaultAsync(r => r.EnrollmentId == enrollmentId && r.Exam == target, ct);
        if (row is null)
        {
            row = new NationalExamResult { EnrollmentId = enrollmentId, Exam = target };
            db.NationalExamResults.Add(row);
        }
        row.Passed = input.Passed;
        row.Percent = input.Percent;
        row.Reference = string.IsNullOrWhiteSpace(input.Reference) ? null : input.Reference.Trim();
        NationalExams.Validate(row);
        audit.Record("examenNational.resultat", "Inscription", enrollmentId.ToString(),
            $"{NationalExams.Label(target)} : {(row.Passed switch { true => "réussi", false => "échec", _ => "non communiqué" })}" +
            (row.Percent is { } p ? $", {p} %" : "") + ".");
        await db.SaveChangesAsync(ct);
        var candidate = data.Candidates.First(c => c.EnrollmentId == enrollmentId);
        return candidate with { Passed = row.Passed, Percent = row.Percent, Reference = row.Reference };
    }, ct);
}
