using System.Text.Json;

namespace Scolia.Bureau.Api;

/// <summary>
/// Lecture JSON bornée pour les échanges avec le Worker central.
/// Une réponse HTTP chunked sans Content-Length reste soumise à la
/// même limite que les réponses avec une taille déclarée.
/// </summary>
internal static class CentralBoundedJson
{
    public static async Task<JsonElement> ReadAsync(
        HttpResponseMessage response, int byteLimit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (byteLimit is < 1 or > 262_144)
            throw new ArgumentOutOfRangeException(nameof(byteLimit));
        if (response.Content.Headers.ContentLength is { } declared &&
            declared > byteLimit)
            throw new InvalidDataException("Réponse centrale trop volumineuse.");

        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[Math.Min(byteLimit + 1, 4096)];
        while (true)
        {
            var size = await input.ReadAsync(chunk.AsMemory(), ct);
            if (size == 0) break;
            if (buffer.Length > byteLimit - size)
                throw new InvalidDataException("Réponse centrale trop volumineuse.");
            buffer.Write(chunk.AsSpan(0, size));
        }

        using var document = JsonDocument.Parse(
            buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Réponse centrale invalide.");
        return document.RootElement.Clone();
    }
}
