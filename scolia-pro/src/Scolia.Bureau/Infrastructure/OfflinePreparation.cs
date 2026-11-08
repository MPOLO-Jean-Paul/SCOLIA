using System.Text.Json;
using Scolia.Bureau.Api;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Progression du cache hors ligne, avec curseur durable entre deux sessions.</summary>
public sealed record OfflinePreparationProgress(int Completed, int Expected, string Stage);
public sealed record OfflinePreparationResult(int Indexed, int Details, int Accounts, int Expected,
    int Errors, bool Complete);

/// <summary>
/// Réplication automatique et manuelle des ressources autorisées, par révision.
/// Chaque ressource réussie est marquée dans SQLite chiffré. Après une coupure,
/// la nouvelle exécution reprend seulement ce qui n'a pas été confirmé pour
/// cette révision. À la révision suivante, tout est revalidé par ETag/HTTP 304.
/// Aucun paiement n'est écrasé et aucune session scolaire n'est exportée.
/// </summary>
public static class OfflinePreparation
{
    public const int PageSize = 50;
    public const int MaximumPages = 200;
    public const int MaximumStudents = PageSize * MaximumPages;

    public static string PagePath(int number)
    {
        if (number < 1 || number > MaximumPages)
            throw new ArgumentOutOfRangeException(nameof(number));
        return "api/apprenants" + ScoliaClient.Query(("page", number), ("taille", PageSize));
    }

    public static string RevisionLabel(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("revision", out var number) ||
            number.ValueKind != JsonValueKind.Number ||
            !number.TryGetInt64(out var revision) || revision < 0)
            throw new InvalidDataException("Révision de synchronisation du serveur invalide.");
        var epoch = data.TryGetProperty("epoch", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString() : "ancien";
        return (string.IsNullOrWhiteSpace(epoch) ? "ancien" : epoch) + ":" +
            revision.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static async Task<OfflinePreparationResult> RunAsync(ScoliaClient client,
        bool includeFinancialAccounts = false,
        Action<OfflinePreparationProgress>? progress = null,
        CancellationToken ct = default, string? snapshotRevision = null)
    {
        var store = client.Offline;
        var user = store?.User;
        var server = client.Server;
        if (store is null || string.IsNullOrWhiteSpace(user))
            throw new InvalidOperationException("Connectez-vous à votre compte SCOLIA avant de préparer le mode hors ligne.");
        if (!client.IsOnline)
            throw new OfflineException("La préparation nécessite une connexion au serveur de l'établissement.");

        void Guard()
        {
            ct.ThrowIfCancellationRequested();
            if (!ReferenceEquals(store, client.Offline) || store.User != user || client.Server != server)
                throw new OperationCanceledException("Le compte ou l'établissement a changé pendant la préparation.");
            if (!client.IsOnline)
                throw new OfflineException("La connexion a été interrompue. Les fichiers déjà validés sont conservés.");
        }

        // La révision est liée au serveur, aux autorisations et au mode financier.
        // Elle ne se confond jamais avec le checkpoint des événements en direct.
        var revision = snapshotRevision ?? RevisionLabel(
            await client.GetAsync<JsonElement>("api/revision", ct));
        Guard();
        var marker = revision + (includeFinancialAccounts ? "|finances" : "|dossiers");
        store.BeginPreparationFor(user, marker);

        // Un élément est sauté SEULEMENT après confirmation durable ET si son JSON
        // reste présent dans le cache. Aucune opération partielle n'est marquée.
        async Task<T> RetrieveAsync<T>(string path)
        {
            Guard();
            if (store.WasPreparedFor(user, marker, path) &&
                store.ReadPage(path) is { } saved)
            {
                try
                {
                    var cached = JsonSerializer.Deserialize<T>(saved.Json, Json.Options);
                    if (cached is not null) return cached;
                }
                catch (JsonException) { /* Ancien cache invalide : reprendre sur le serveur. */ }
            }

            var value = await client.GetAsync<T>(path, ct);
            Guard();
            // GetAsync écrit d'abord la copie dans SQLite. Sans copie complète,
            // on ne déclare jamais un élément "fait" pour une future reprise.
            if (store.ReadPage(path) is not null)
                store.MarkPreparedFor(user, marker, path);
            return value;
        }

        var metadata = new[] { "api/etablissement", "api/etablissement/annees",
            "api/etablissement/sections", "api/etablissement/promotions" };
        var completed = 0;
        foreach (var path in metadata)
        {
            Guard();
            try { await RetrieveAsync<JsonElement>(path); }
            catch (ApiException ex) when (ex.Status is 403 or 404) { }
            completed++;
            progress?.Invoke(new OfflinePreparationProgress(completed, metadata.Length, "Configuration scolaire"));
        }

        var indexed = 0;
        var details = 0;
        var accounts = 0;
        var errors = 0;
        var first = await RetrieveAsync<PagedResult<StudentListItem>>(PagePath(1));
        Guard();
        var expected = Math.Max(first.Total, 0);
        var pages = Math.Min(MaximumPages, Math.Max(1, (expected + PageSize - 1) / PageSize));
        var planned = metadata.Length + pages + Math.Min(expected, MaximumStudents)
            * (includeFinancialAccounts ? 2 : 1);
        var receivedIds = new HashSet<Guid>();

        for (var page = 1; page <= pages; page++)
        {
            Guard();
            var batch = page == 1 ? first
                : await RetrieveAsync<PagedResult<StudentListItem>>(PagePath(page));
            Guard();
            completed++;
            progress?.Invoke(new OfflinePreparationProgress(completed, planned,
                $"Index apprenants : page {page}/{pages}"));
            foreach (var row in batch.Items)
            {
                Guard();
                if (!receivedIds.Add(row.Id)) continue;
                indexed++;
                try
                {
                    await RetrieveAsync<StudentDto>($"api/apprenants/{row.Id}");
                    details++;
                }
                catch (ApiException ex) when (ex.Status is 403 or 404)
                {
                    errors++; // Un refus n'est jamais marqué comme préparation réussie.
                }
                completed++;
                progress?.Invoke(new OfflinePreparationProgress(completed, planned,
                    $"Fiches apprenants : {details}/{Math.Min(expected, MaximumStudents)}"));

                if (!includeFinancialAccounts || row.EnrollmentId is not { } enrollmentId) continue;
                Guard();
                try
                {
                    await RetrieveAsync<AccountDto>($"api/inscriptions/{enrollmentId}/compte");
                    accounts++;
                }
                catch (ApiException ex) when (ex.Status is 403 or 404) { errors++; }
                completed++;
                progress?.Invoke(new OfflinePreparationProgress(completed, planned,
                    $"Comptes autorisés : {accounts}/{Math.Min(expected, MaximumStudents)}"));
            }
        }

        Guard();
        if (includeFinancialAccounts)
        {
            var journal = "api/paiements" + ScoliaClient.Query(("page", 1), ("pageSize", 200));
            try { await RetrieveAsync<PagedResult<ReceiptRowDto>>(journal); }
            catch (ApiException ex) when (ex.Status is 403 or 404) { errors++; }
        }

        // Une écriture provenant d'un autre poste pendant le téléchargement
        // invalide le snapshot ; on recommence seulement pour la NOUVELLE révision.
        var latestRevision = RevisionLabel(await client.GetAsync<JsonElement>("api/revision", ct));
        Guard();
        var complete = latestRevision == revision && expected <= MaximumStudents
            && indexed >= expected && details == indexed && errors == 0;
        progress?.Invoke(new OfflinePreparationProgress(planned, planned,
            complete ? "Copie locale complète" :
            latestRevision != revision ? "Nouvelles données détectées : prochaine révision" :
            "Copie partielle : reprise automatique"));
        return new OfflinePreparationResult(indexed, details, accounts, expected, errors, complete);
    }
}
