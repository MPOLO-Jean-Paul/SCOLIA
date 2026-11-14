using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Scolia.Infrastructure.Backup;

namespace Scolia.Api.Security;

/// <summary>
/// Une clé d'idempotence ne peut désigner qu'une opération précise.
/// Les réponses en attente sont protégées avec ASP.NET Core Data Protection.
/// Une ancienne réponse non vérifiable n'est jamais rejouée à l'aveugle.
/// </summary>
public static class Idempotency
{
    public const string Header = "Idempotency-Key";
    private const string Prefix = "SCOLIA-IDEMPOTENCE-V2:";
    private const int MaxBody = 1024 * 1024;
    // Une opération faite hors ligne peut revenir longtemps après (téléphone resté loin du serveur,
    // ou déjà appliquée depuis la boîte de dépôt en ligne) : la preuve d'exécution reste 90 jours.
    private static readonly TimeSpan Retention = TimeSpan.FromDays(90);
    private static readonly SemaphoreSlim[] Gates = [.. Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1))];
    private static int cleaned;

    private sealed record Stored(
        int Status, string? ContentType, string? Body,
        string RequestHash, string SecurityStamp, DateTime AtUtc);

    public static IApplicationBuilder UseIdempotency(this IApplicationBuilder app) => app.Use(async (HttpContext http, RequestDelegate next) =>
    {
        var key = http.Request.Headers[Header].ToString();
        if (key.Length is 0 or > 100 || HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)
            || !http.Request.Path.StartsWithSegments("/api") || http.User.Identity?.IsAuthenticated != true)
        {
            await next(http);
            return;
        }

        var directory = http.RequestServices.GetRequiredService<IOptions<ScoliaStorageOptions>>().Value.DataDirectory;
        var folder = Path.Combine(directory, "idempotence");
        Directory.CreateDirectory(folder);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (Interlocked.Exchange(ref cleaned, 1) == 0) _ = Task.Run(() => Clean(folder));

        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var stamp = http.User.FindFirstValue(ScoliaClaims.Stamp) ?? string.Empty;
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}\u001F{key}")));
        var path = Path.Combine(folder, name + ".json");
        var gate = Gates[Convert.ToByte(name[..2], 16) % Gates.Length];
        await gate.WaitAsync(http.RequestAborted);
        try
        {
            var requestHash = await FingerprintAsync(http.Request, stamp, http.RequestAborted);
            var protector = http.RequestServices.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("SCOLIA.Idempotency.Response.v2");

            if (File.Exists(path))
            {
                Stored? stored;
                try
                {
                    var protectedText = await File.ReadAllTextAsync(path, http.RequestAborted);
                    if (!protectedText.StartsWith(Prefix, StringComparison.Ordinal))
                    {
                        await ConflictAsync(http, "Une réponse de l'ancienne version existe. Vérifiez l'opération sur le serveur avant de la reprendre.");
                        return;
                    }

                    var clear = protector.Unprotect(Convert.FromBase64String(protectedText[Prefix.Length..]));
                    stored = JsonSerializer.Deserialize<Stored>(clear);
                }
                catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
                {
                    await ConflictAsync(http, "La confirmation sauvegardée est illisible. Vérifiez l'opération avant de la reprendre.");
                    return;
                }

                if (stored is null || stored.SecurityStamp != stamp || stored.RequestHash != requestHash)
                {
                    await ConflictAsync(http, "Cette clé de reprise appartient déjà à une autre opération ou à une ancienne session.");
                    return;
                }

                if (stored.Body is null)
                {
                    await ConflictAsync(http, "L'opération a été exécutée, mais sa réponse est trop volumineuse pour être rejouée. Vérifiez son résultat.");
                    return;
                }

                http.Response.StatusCode = stored.Status;
                if (stored.ContentType is not null) http.Response.ContentType = stored.ContentType;
                http.Response.Headers["Idempotent-Replayed"] = "true";
                await http.Response.Body.WriteAsync(Convert.FromBase64String(stored.Body), http.RequestAborted);
                return;
            }

            var original = http.Response.Body;
            await using var copy = new MemoryStream();
            http.Response.Body = copy;
            try { await next(http); }
            finally { http.Response.Body = original; }

            // Persister AVANT d'envoyer la confirmation au client.
            // Une connexion interrompue pendant l'envoi ne doit pas rendre l'écriture rejouable.
            // Garder aussi une preuve sans réponse pour les grosses écritures :
            // ne jamais réexécuter silencieusement une opération déjà terminée.
            if (http.Response.StatusCode < 500)
            {
                var record = new Stored(http.Response.StatusCode, http.Response.ContentType,
                    copy.Length <= MaxBody ? Convert.ToBase64String(copy.ToArray()) : null,
                    requestHash, stamp, DateTime.UtcNow);
                var protectedBytes = protector.Protect(JsonSerializer.SerializeToUtf8Bytes(record));
                await WriteAtomicallyAsync(path, Prefix + Convert.ToBase64String(protectedBytes));
            }

            copy.Position = 0;
            await copy.CopyToAsync(original, http.RequestAborted);
        }
        finally
        {
            gate.Release();
        }
    });

    private static async Task<string> FingerprintAsync(HttpRequest request, string stamp, CancellationToken ct)
    {
        // Les téléchargements et formulaires multipart peuvent être volumineux :
        // EnableBuffering utilise un fichier temporaire passé un seuil de 64 Kio.
        request.EnableBuffering(bufferThreshold: 64 * 1024, bufferLimit: 64 * 1024 * 1024);
        var bodyHash = Convert.ToHexString(await SHA256.HashDataAsync(request.Body, ct));
        request.Body.Position = 0;
        var identity = string.Join("\n", request.Method.ToUpperInvariant(),
            request.Path.Value ?? "", request.QueryString.Value ?? "",
            request.ContentType ?? "", stamp, bodyHash);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    private static async Task ConflictAsync(HttpContext http, string message)
    {
        http.Response.StatusCode = StatusCodes.Status409Conflict;
        http.Response.Headers["Cache-Control"] = "no-store";
        await http.Response.WriteAsJsonAsync(new { title = message, code = "idempotency-conflict" });
    }

    private static async Task WriteAtomicallyAsync(string target, string content)
    {
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(temporary, options))
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                await stream.WriteAsync(bytes, CancellationToken.None);
                await stream.FlushAsync(CancellationToken.None);
            }
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

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
