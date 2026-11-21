using Scolia.Bureau.Api;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Cycle de synchronisation du catalogue pilote de sections.
/// Les modifications locales restent persistantes en cas de coupure, timeout,
/// HTTP 202, conflit, révocation ou interruption de processus.
/// </summary>
public static class CentralSectionSync
{
    /// <summary>
    /// Exécute au plus un lot d'outbox puis cinq pages entrantes. Aucun
    /// accusé global n'est envoyé : la mise en place définitive du moteur
    /// scolaire et des permissions doit précéder l'ACK distant.
    /// </summary>
    public static async Task<CentralSectionSyncResult> RunOnceAsync(
        CentralSectionOfflineStore local, CentralSectionClient remote,
        Uri origin, string centralSessionJwt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);
        var pending = local.Snapshot().Pending;
        var confirmed = 0;
        var conflicts = 0;
        foreach (var item in pending.Where(x => !x.Conflict))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Jamais recréer une operation_id : la même demande peut avoir
                // été traitée juste avant une coupure TCP.
                var receipt = await remote.UpsertAsync(origin, centralSessionJwt, item.Change, ct);
                if (receipt.OperationId != item.Change.OperationId)
                    throw new InvalidDataException("Opération confirmée non correspondante.");
                local.Confirm(receipt);
                confirmed++;
            }
            catch (CentralPairingException ex) when (ex.Status == 409)
            {
                local.MarkConflict(item.Change.OperationId);
                conflicts++;
            }
            // Sur 202, quota, 401/403, 5xx ou exception réseau : NE PAS
            // retirer l'outbox. Le travail peut être repris ultérieurement.
        }

        var applied = 0;
        var hasMore = false;
        for (var pageNumber = 0; pageNumber < 5; pageNumber++)
        {
            ct.ThrowIfCancellationRequested();
            var after = local.Snapshot().Revision;
            var page = await remote.ReadAsync(origin, centralSessionJwt,
                after, limit: 50, ct);
            local.ApplyPage(after, page);
            applied += page.Events.Count;
            hasMore = page.HasMore;
            if (!hasMore) break;
        }

        var latest = local.Snapshot();
        return new CentralSectionSyncResult(
            confirmed, conflicts, applied, latest.Pending.Count,
            latest.Revision, hasMore);
    }
}

public sealed record CentralSectionSyncResult(
    int Confirmed, int Conflicts, int AppliedEvents,
    int Pending, long Revision, bool HasMore);
