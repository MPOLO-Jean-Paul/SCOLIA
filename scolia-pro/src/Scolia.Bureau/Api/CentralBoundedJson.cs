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

        // Avec ResponseHeadersRead, HttpClient.Timeout s'arrête après les
        // en-têtes. Ce second délai protège contre un flux chunked qui reste
        // ouvert indéfiniment en envoyant très peu d'octets.
        using var readingBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readingBudget.CancelAfter(TimeSpan.FromSeconds(20));
        using var buffer = new MemoryStream();
        var chunk = new byte[Math.Min(byteLimit + 1, 4096)];
        try
        {
            await using var input = await response.Content.ReadAsStreamAsync(
                readingBudget.Token);
            while (true)
            {
                var size = await input.ReadAsync(chunk.AsMemory(),
                    readingBudget.Token);
                if (size == 0) break;
                if (buffer.Length > byteLimit - size)
                    throw new InvalidDataException("Réponse centrale trop volumineuse.");
                buffer.Write(chunk.AsSpan(0, size));
            }
        }
        catch (OperationCanceledException exception)
            when (!ct.IsCancellationRequested && readingBudget.IsCancellationRequested)
        {
            throw new TaskCanceledException(
                "Le délai de lecture de la réponse centrale est dépassé.", exception);
        }

        using var document = JsonDocument.Parse(
            buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Réponse centrale invalide.");
        return document.RootElement.Clone();
    }
}
