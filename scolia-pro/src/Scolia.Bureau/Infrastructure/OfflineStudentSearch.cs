using System.Text.Json;
using Scolia.Bureau.Api;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Recherche locale sur les seules pages de registre déjà chiffrées sur l'appareil.
/// Ne promet jamais que l'index incomplet représente la totalité des apprenants.
/// </summary>
public static class OfflineStudentSearch
{
    public sealed record Result(PagedResult<StudentListItem> Page, int Loaded, int Registered, bool Complete);

    /// <summary>
    /// Parcourt les pages originales (sans filtre) conservées pour l'utilisateur courant.
    /// Une recherche filtrée jamais envoyée au serveur fonctionne ainsi sans réseau.
    /// </summary>
    public static Result? Search(OfflineStore store, string? query, string? promotionCode,
        string? status, int requestedPage, int pageSize = 50)
    {
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));

        var items = new Dictionary<Guid, StudentListItem>();
        var registered = 0;
        // 20 pages au maximum pour éviter un balayage SQLite excessif sur le téléphone.
        // Les fiches non téléchargées ne sont pas synthétisées ni inventées.
        for (var number = 1; number <= 20; number++)
        {
            var key = "api/apprenants" + ScoliaClient.Query(("page", number), ("taille", pageSize));
            if (store.ReadPage(key) is not { } cached)
            {
                // Le cache de l'index est paginé et récupéré dans l'ordre.
                if (number == 1) return null;
                break;
            }

            PagedResult<StudentListItem>? response;
            try { response = JsonSerializer.Deserialize<PagedResult<StudentListItem>>(cached.Json, Json.Options); }
            catch (JsonException) { return null; }
            if (response is null) return null;
            registered = Math.Max(registered, response.Total);
            foreach (var student in response.Items) items[student.Id] = student;
            if (items.Count >= registered) break;
        }

        var searchable = (query ?? string.Empty).Trim();
        var match = items.Values
            .Where(s => string.IsNullOrWhiteSpace(promotionCode)
                || string.Equals(s.PromotionCode, promotionCode, StringComparison.OrdinalIgnoreCase))
            .Where(s => string.IsNullOrWhiteSpace(status)
                || string.Equals(s.Status, status, StringComparison.OrdinalIgnoreCase))
            .Where(s => searchable.Length == 0 || string.Join(" ", s.Matricule,
                s.LastName, s.MiddleName, s.FirstName, s.GuardianPhone ?? string.Empty)
                .Contains(searchable, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.LastName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.MiddleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.FirstName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var safePage = Math.Clamp(requestedPage, 1,
            Math.Max(1, (int)Math.Ceiling(match.Count / (double)pageSize)));
        var part = match.Skip((safePage - 1) * pageSize).Take(pageSize).ToList();
        return new Result(new PagedResult<StudentListItem>(part, match.Count, safePage, pageSize, null),
            items.Count, registered, registered > 0 && items.Count >= registered);
    }
}
