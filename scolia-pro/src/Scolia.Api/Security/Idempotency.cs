using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Scolia.Infrastructure.Backup;

namespace Scolia.Api.Security;

/// <summary>
/// Écritures rejouables sans doublon. Un poste qui a travaillé hors ligne renvoie ses opérations au retour du
/// serveur ; une coupure peut aussi survenir après l'enregistrement, avant la réponse. Chaque écriture porte
/// une clé unique (en-tête Idempotency-Key) : la première fois, elle s'exécute et sa réponse est gardée ;
/// les fois suivantes, la même réponse est rendue sans rien refaire. Un reçu n'est donc jamais créé deux fois.
/// </summary>
public static class Idempotency
{
    public const string Header = "Idempotency-Key";

    /// <summary>Réponses gardées une semaine : bien plus qu'une coupure de réseau ou une journée hors ligne.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    /// <summary>Réponse gardée au plus : au-delà (un document), elle n'est pas conservée.</summary>
    private const int MaxBody = 1024 * 1024;

    /// <summary>
    /// Verrous répartis par clé : deux envois de la même clé passent l'un après l'autre, des clés différentes
    /// avancent en parallèle, et le nombre de verrous reste fixe.
    /// </summary>
    private static readonly SemaphoreSlim[] Gates = [.. Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1))];
    private static int cleaned;

    private sealed record Stored(int Status, string? ContentType, string Body, DateTime AtUtc);

    public static IApplicationBuilder UseIdempotency(this IApplicationBuilder app) => app.Use(async (HttpContext http, RequestDelegate next) =>
    {
        var key = http.Request.Headers[Header].ToString();
        if (key.Length is 0 or > 100 || HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)
            || !http.Request.Path.StartsWithSegments("/api") || http.User.Identity?.IsAuthenticated != true)
        {
            await next(http);
            return;
        }

        var folder = Path.Combine(http.RequestServices.GetRequiredService<IOptions<ScoliaStorageOptions>>().Value.DataDirectory, "idempotence");
        Directory.CreateDirectory(folder);
        if (Interlocked.Exchange(ref cleaned, 1) == 0) _ = Task.Run(() => Clean(folder));

        // La clé est propre à l'utilisateur : un autre compte ne peut pas relire la réponse d'un tiers.
        var user = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{user}\u001F{key}")));
        var file = Path.Combine(folder, name + ".json");
        var gate = Gates[Convert.ToByte(name[..2], 16) % Gates.Length];
        await gate.WaitAsync(http.RequestAborted);
        try
        {
            if (File.Exists(file) && JsonSerializer.Deserialize<Stored>(await File.ReadAllTextAsync(file)) is { } stored)
            {
                http.Response.StatusCode = stored.Status;
                if (stored.ContentType is not null) http.Response.ContentType = stored.ContentType;
                http.Response.Headers["Idempotent-Replayed"] = "true";
                await http.Response.Body.WriteAsync(Convert.FromBase64String(stored.Body));
                return;
            }

            var original = http.Response.Body;
            await using var copy = new MemoryStream();
            http.Response.Body = copy;
            try { await next(http); }
            finally
            {
                http.Response.Body = original;
                copy.Position = 0;
                await copy.CopyToAsync(original);
            }
            // Une erreur du serveur n'est pas gardée : l'opération pourra être retentée.
            if (http.Response.StatusCode < 500 && copy.Length <= MaxBody)
            {
                var record = new Stored(http.Response.StatusCode, http.Response.ContentType, Convert.ToBase64String(copy.ToArray()), DateTime.UtcNow);
                await File.WriteAllTextAsync(file, JsonSerializer.Serialize(record));
            }
        }
        finally
        {
            gate.Release();
        }
    });

    private static void Clean(string folder)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > Retention) File.Delete(file);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
