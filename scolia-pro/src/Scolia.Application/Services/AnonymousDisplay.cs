using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;

namespace Scolia.Application.Services;

/// <summary>
/// Identifiant affiché d'un apprenant dans les écrans administratifs (présences, encaissement par lot, caisse,
/// rapports, prises en charge, discipline) : son code anonyme actif pour l'inscription, jamais la référence
/// technique (INF101…). Les reçus, les comptes financiers, les notes et la délibération gardent leur référence.
/// </summary>
public static class AnonymousDisplay
{
    /// <summary>Mention affichée tant qu'aucun code anonyme n'est attribué à l'inscription.</summary>
    public const string Pending = "À ATTRIBUER";

    /// <summary>Codes actifs des inscriptions demandées, en une seule requête.</summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> CodesAsync(IAppDbContext db, IEnumerable<Guid> enrollmentIds,
        CancellationToken ct = default)
    {
        var ids = enrollmentIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string>();
        var rows = await db.AnonymousCodes.AsNoTracking()
            .Where(c => c.IsActive && ids.Contains(c.EnrollmentId))
            .Select(c => new { c.EnrollmentId, c.Code })
            .ToListAsync(ct);
        // Une inscription n'a qu'un code actif ; en cas d'anomalie, le premier lu suffit à l'affichage.
        return rows.GroupBy(r => r.EnrollmentId).ToDictionary(g => g.Key, g => g.First().Code);
    }

    /// <summary>Code actif de l'inscription, ou la mention « À ATTRIBUER ».</summary>
    public static string Of(IReadOnlyDictionary<Guid, string> codes, Guid enrollmentId) =>
        codes.TryGetValue(enrollmentId, out var code) && !string.IsNullOrWhiteSpace(code) ? code : Pending;
}
