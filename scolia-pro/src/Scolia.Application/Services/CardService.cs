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

/// <summary>Résultat d'un export de cartes : le fichier, son nom, et les compteurs affichés à l'utilisateur.</summary>
public sealed record CardExportResult(byte[] Bytes, string FileName, int Cards, int Pages, int Skipped);

/// <summary>
/// Cartes d'accès aux examens (version 79) : sélection des apprenants, diagnostic des identités, export PDF
/// et historique des lots. Les sortis et suspendus sont exclus par défaut.
/// </summary>
public sealed class CardService(IAppDbContext db, IWriteGate gate, IAuditLog audit, IClock clock, ICurrentUser user, IDocumentService documents)
{
    private sealed record CandidateRow(Guid EnrollmentId, string Matricule, string LastName, string MiddleName, string FirstName, string Promotion,
        EnrollmentStatus Effective, bool Duplicate);

    public async Task<IReadOnlyList<CardCandidateDto>> CandidatesAsync(Guid? promotionId, string? search, bool includeLeft, CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var rows = await CandidateRowsAsync(year, promotionId, null, ct);
        var term = TextKey.Normalize(search);
        return rows
            .Where(r => term.Length == 0 || TextKey.Normalize($"{r.Matricule} {r.LastName} {r.MiddleName} {r.FirstName}").Contains(term, StringComparison.Ordinal))
            .OrderBy(r => r.Promotion).ThenBy(r => r.LastName).ThenBy(r => r.FirstName)
            .Select(r => new CardCandidateDto(r.EnrollmentId, r.Matricule, Name(r), r.Promotion, Labels.Of(r.Effective),
                StudentCardRules.Eligible(r.Effective, includeLeft), StudentCardRules.IdentityComplete(r.LastName, r.FirstName), r.Duplicate))
            .ToList();
    }

    /// <summary>
    /// Exporte les cartes des apprenants choisis. Les identités incomplètes, les matricules répétés et, sauf demande,
    /// les sortis et suspendus sont écartés et comptés. Un export réussi est inscrit dans l'historique.
    /// </summary>
    public Task<CardExportResult> ExportAsync(CardExportRequest input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var copies = StudentCardRules.Copies(input.Copies);
        var session = StudentCardRules.SessionLabel(input.Month, input.Year);
        var requested = input.EnrollmentIds.Distinct().ToList();
        if (requested.Count == 0) throw new DomainException("cartes", "Sélectionnez au moins un apprenant à imprimer.");

        var year = await YearContext.CurrentAsync(db, ct);
        var rows = await CandidateRowsAsync(year, null, requested, ct);
        var selected = rows
            .Where(r => StudentCardRules.Eligible(r.Effective, input.IncludeLeft) && StudentCardRules.IdentityComplete(r.LastName, r.FirstName) && !r.Duplicate)
            .OrderBy(r => r.Promotion).ThenBy(r => r.LastName).ThenBy(r => r.FirstName)
            .ToList();
        var skipped = requested.Count - selected.Count;
        if (selected.Count == 0)
            throw new DomainException("cartes", "Aucune carte à imprimer : les apprenants choisis sont sortis ou suspendus, ou leur identité est incomplète.");
        foreach (var r in selected)
        {
            StudentCardRules.EnsureFits(r.LastName, "nom");
            StudentCardRules.EnsureFits(r.MiddleName, "post-nom");
            StudentCardRules.EnsureFits(r.FirstName, "prénom");
        }

        var school = await db.Schools.AsNoTracking().OrderBy(x => x.CreatedAtUtc).FirstAsync(ct);
        if (school.Logo is not { Length: > 0 })
            throw new DomainException("cartes",
                "Le logo de votre établissement n'est pas configuré. Ajoutez-le dans Paramètres > Établissement avant d'imprimer les cartes d'accès.");
        // Les cartes reprennent l'identité propre à l'établissement actif, jamais un nom ou un sceau ITM imposé.
        var header = new SchoolCardHeader(
            CardHeaderDefaults.Or(school.Country, CardHeaderDefaults.Country),
            CardHeaderDefaults.Or(school.CardMinistry, CardHeaderDefaults.Ministry),
            CardHeaderDefaults.Or(school.CardDirection, CardHeaderDefaults.Direction),
            CardHeaderDefaults.Or(school.CardDivision, CardHeaderDefaults.Division),
            CardHeaderDefaults.Or(school.CardZone, CardHeaderDefaults.CityZone),
            CardHeaderDefaults.Or(school.CardInstitute, school.Name),
            CardHeaderDefaults.Or(school.CardAddress, school.Address ?? string.Empty),
            school.Logo);
        var cards = new List<CardItem>();
        foreach (var r in selected)
        {
            var qr = $"{school.Name} | {session} | {year.Label} | {r.Matricule} | {Name(r)} | {r.Promotion}";
            for (var i = 0; i < copies; i++)
                cards.Add(new CardItem(r.LastName, r.MiddleName, r.FirstName, r.Promotion, StudentCardRules.ColorOf(r.Promotion), qr, r.Matricule));
        }

        var word = string.Equals(input.Format, "docx", StringComparison.OrdinalIgnoreCase);
        var document = new CardDocument(header, session, year.Label, cards);
        var bytes = word ? documents.StudentCardsWord(document) : documents.StudentCards(document);
        var promotions = selected.Select(r => r.Promotion).Distinct().ToList();
        var label = promotions.Count == 1 ? promotions[0] : "Plusieurs promotions";
        var pages = StudentCardRules.PagesFor(cards.Count);
        var extension = word ? "docx" : "pdf";
        var fileName = $"cartes-{input.Year}-{input.Month:00}-{label.Replace(' ', '-')}.{extension}";
        var batch = new CardBatch
        {
            Session = session, PromotionLabel = label, StudentCount = selected.Count, CardCount = cards.Count, PageCount = pages,
            Format = word ? "Word" : "PDF", FileName = fileName, CreatedByName = user.Name,
        };
        db.CardBatches.Add(batch);
        audit.Record("cartes.export", "CarteAcces", batch.Id.ToString(), $"{cards.Count} carte(s) sur {pages} page(s), session {session}, {label}.");
        await db.SaveChangesAsync(ct);
        return new CardExportResult(bytes, fileName, cards.Count, pages, skipped);
    }, ct);

    /// <summary>Derniers exports réussis. L'historique ne contient pas la liste nominative.</summary>
    public async Task<IReadOnlyList<CardBatchDto>> BatchesAsync(CancellationToken ct = default) =>
        (await db.CardBatches.AsNoTracking().OrderByDescending(b => b.CreatedAtUtc).Take(50).ToListAsync(ct))
        .Select(b => new CardBatchDto(b.Id, b.CreatedAtUtc, b.Session, b.PromotionLabel, b.StudentCount, b.CardCount, b.PageCount, b.Format, b.FileName, b.CreatedByName))
        .ToList();

    /// <summary>Inscriptions de l'année avec leur statut effectif (la discipline compte) et le signalement des matricules répétés.</summary>
    private async Task<List<CandidateRow>> CandidateRowsAsync(AcademicYear year, Guid? promotionId, IReadOnlyCollection<Guid>? only, CancellationToken ct)
    {
        var items = (await (from e in db.Enrollments.AsNoTracking()
                            join s in db.Students.AsNoTracking() on e.StudentId equals s.Id
                            join p in db.Promotions.AsNoTracking() on e.PromotionId equals p.Id
                            where e.AcademicYearId == year.Id && (promotionId == null || e.PromotionId == promotionId)
                            select new { e.Id, e.Status, s.Matricule, s.LastName, s.MiddleName, s.FirstName, Promotion = p.Code })
                           .ToListAsync(ct))
            .Where(x => only is null || only.Contains(x.Id))
            .ToList();
        var ids = items.Select(x => x.Id).ToList();
        var decisions = (await db.DisciplineDecisions.AsNoTracking()
            .Where(d => ids.Contains(d.EnrollmentId) && d.State == DecisionState.Validee).ToListAsync(ct))
            .GroupBy(d => d.EnrollmentId).ToDictionary(g => g.Key, g => g.ToList());
        var today = clock.Today;
        var repeated = items.GroupBy(x => x.Matricule).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return items.Select(x => new CandidateRow(
            x.Id, x.Matricule, x.LastName, x.MiddleName, x.FirstName, x.Promotion,
            DisciplineRules.EffectiveStatus(x.Status, decisions.TryGetValue(x.Id, out var mine) ? mine : [], today),
            repeated.Contains(x.Matricule))).ToList();
    }

    private static string Name(CandidateRow r) => string.Join(' ', new[] { r.LastName, r.MiddleName, r.FirstName }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static string JoinNonEmpty(params string?[] values) => string.Join(" · ", values.Where(v => !string.IsNullOrWhiteSpace(v)));
}
