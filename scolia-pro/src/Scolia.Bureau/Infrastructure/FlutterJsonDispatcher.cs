using System.Text.Json;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Contrat JSON commun au canal Android et au moteur existant. Ne journalise aucune donnée.</summary>
public sealed class FlutterJsonDispatcher(
    Func<string, IReadOnlyDictionary<string, string>, Task<FlutterCommandAdapter.Snapshot>> execute)
{
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> DispatchAsync(string operation, string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(operation) || operation.Length > 40 || argumentsJson.Length > 8192)
            throw new ArgumentException("Commande invalide.");
        using var document = JsonDocument.Parse(argumentsJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Arguments invalides.");
        var arguments = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (arguments.Count >= 12 || property.Name.Length > 40 ||
                property.Value.ValueKind != JsonValueKind.String ||
                !arguments.TryAdd(property.Name, property.Value.GetString()!))
                throw new ArgumentException("Arguments invalides.");
        }
        var snapshot = await execute(operation, arguments);
        return JsonSerializer.Serialize(snapshot, WireOptions);
    }
}
