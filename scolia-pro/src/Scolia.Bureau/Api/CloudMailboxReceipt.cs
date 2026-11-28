using System.Text.Json;

namespace Scolia.Bureau.Api;

/// <summary>
/// Aucune opération en attente ne peut être retirée sur un simple HTTP 200.
/// Le Worker doit reconnaître explicitement le dépôt chiffré ou confirmer
/// l'application métier définitive par le serveur scolaire.
/// </summary>
public enum CloudMailboxReceipt
{
    Stored,
    Applied,
    Refused,
}

public static class CloudMailboxReceiptPolicy
{
    public static CloudMailboxReceipt Parse(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Accusé du dépôt cloud invalide.");

        if (response.TryGetProperty("outcome", out var outcome))
        {
            if (outcome.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Issue du dépôt cloud invalide.");
            return outcome.GetString() switch
            {
                "applied" => CloudMailboxReceipt.Applied,
                "refused" => CloudMailboxReceipt.Refused,
                _ => throw new InvalidDataException(
                    "Issue du dépôt cloud non reconnue."),
            };
        }

        // Réponse du Worker /api/v1/device/mailbox :
        // stored:true atteste seulement le dépôt, JAMAIS un paiement validé.
        if (response.TryGetProperty("stored", out var stored) &&
            stored.ValueKind == JsonValueKind.True)
            return CloudMailboxReceipt.Stored;

        throw new InvalidDataException(
            "Le serveur n'a pas confirmé le stockage de l'opération.");
    }

    /// <summary>
    /// Lire et valider toutes les issues AVANT de toucher à la file SQLite.
    /// Un corps non conforme n'efface aucune opération locale.
    /// </summary>
    public static IReadOnlySet<string> AppliedOutcomes(
        JsonElement response, IEnumerable<string> requestedIds)
    {
        if (response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("outcomes", out var values) ||
            values.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Réponse des issues cloud invalide.");

        var applied = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in requestedIds.Distinct(StringComparer.Ordinal))
        {
            if (!values.TryGetProperty(id, out var value)) continue;
            if (value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Issue de dépôt malformée.");

            switch (value.GetString())
            {
                case "applied":
                    applied.Add(id);
                    break;
                case "pending":
                case "refused":
                case "unknown":
                    break;
                default:
                    throw new InvalidDataException("Issue cloud inconnue.");
            }
        }
        return applied;
    }
}
