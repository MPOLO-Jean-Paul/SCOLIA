using Scolia.Bureau.Api;

namespace Scolia.Bureau.Infrastructure;

/// <summary>État d'une préparation volontaire des copies locales chiffrées.</summary>
public sealed record OfflinePreparationProgress(int Completed, int Expected, string Stage);
public sealed record OfflinePreparationResult(int Indexed, int Details, int Accounts, int Expected,
    int Errors, bool Complete);

/// <summary>
/// Précharge les pages utiles à une consultation hors connexion, sous les permissions
/// du compte réellement connecté. Aucune donnée n'est récupérée sans action explicite.
/// Le serveur continue de décider des autorisations ; une erreur 403 n'est jamais contournée.
/// </summary>
public static class OfflinePreparation
{
    public const int PageSize = 50;
    public const int MaximumPages = 20;
    public const int MaximumStudents = PageSize * MaximumPages;

    /// <summary>Chemin identique à l'index utilisé par la recherche locale, sans filtre.</summary>
    public static string PagePath(int number)
    {
        if (number < 1 || number > MaximumPages)
            throw new ArgumentOutOfRangeException(nameof(number));
        return "api/apprenants" + ScoliaClient.Query(("page", number), ("taille", PageSize));
    }

    /// <summary>
    /// Protéger l'identité active pendant toute la préparation. La déconnexion,
    /// le changement d'établissement ou le changement de compte arrêtent le travail.
    /// </summary>
    public static async Task<OfflinePreparationResult> RunAsync(ScoliaClient client,
        bool includeFinancialAccounts = false,
        Action<OfflinePreparationProgress>? progress = null, CancellationToken ct = default)
    {
        var store = client.Offline;
        var user = store?.User;
        var server = client.Server;
        if (store is null || string.IsNullOrWhiteSpace(user))
            throw new InvalidOperationException("Connectez-vous à votre compte SCOLIA avant de préparer le mode hors ligne.");
        if (!client.IsOnline)
            throw new OfflineException("La préparation complète nécessite une connexion au serveur de l'établissement.");

        void Guard()
        {
            ct.ThrowIfCancellationRequested();
            if (!ReferenceEquals(store, client.Offline) || store.User != user || client.Server != server)
                throw new OperationCanceledException("Le compte ou l'établissement a changé pendant la préparation.");
            if (!client.IsOnline)
                throw new OfflineException("La connexion a été interrompue. Les données déjà téléchargées restent disponibles.");
        }

        // Ces métadonnées ne sont pas privées par rapport au compte déjà authentifié.
        // Le client les garde sous son propre identifiant, sans créer un second format de cache.
        var metadata = new[] { "api/etablissement", "api/etablissement/annees",
            "api/etablissement/sections", "api/etablissement/promotions" };
        var completed = 0;
        foreach (var path in metadata)
        {
            Guard();
            await client.GetAsync<System.Text.Json.JsonElement>(path, ct);
            completed++;
            progress?.Invoke(new OfflinePreparationProgress(completed, 4, "Configuration scolaire"));
        }

        var indexed = 0;
        var details = 0;
        var accounts = 0;
        var errors = 0;
        var first = await client.GetAsync<PagedResult<StudentListItem>>(PagePath(1), ct);
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
                : await client.GetAsync<PagedResult<StudentListItem>>(PagePath(page), ct);
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
                    await client.GetAsync<StudentDto>($"api/apprenants/{row.Id}", ct);
                    details++;
                }
                catch (ApiException ex) when (ex.Status is 403 or 404)
                {
                    errors++; // Dossier supprimé ou droits modifiés : jamais de contournement.
                }
                completed++;
                progress?.Invoke(new OfflinePreparationProgress(completed, planned,
                    $"Fiches apprenants : {details}/{Math.Min(expected, MaximumStudents)}"));

                if (!includeFinancialAccounts || row.EnrollmentId is not { } enrollmentId) continue;
                Guard();
                try
                {
                    await client.GetAsync<AccountDto>($"api/inscriptions/{enrollmentId}/compte", ct);
                    accounts++;
                }
                catch (ApiException ex) when (ex.Status is 403 or 404)
                {
                    errors++;
                }
                completed++;
                progress?.Invoke(new OfflinePreparationProgress(completed, planned,
                    $"Comptes autorisés : {accounts}/{Math.Min(expected, MaximumStudents)}"));
            }
        }

        Guard();
        var complete = expected <= MaximumStudents && indexed >= expected && details == indexed
            && (!includeFinancialAccounts || errors == 0);
        progress?.Invoke(new OfflinePreparationProgress(planned, planned,
            complete ? "Copie hors ligne complète" : "Copie locale partielle : vérification recommandée"));
        return new OfflinePreparationResult(indexed, details, accounts, expected, errors, complete);
    }
}
