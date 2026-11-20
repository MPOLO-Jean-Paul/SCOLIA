using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Scolia.Api.Security;
using Scolia.Domain.Cloud;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Api.Cloud;

/// <summary>
/// Rejoue une opération déposée en ligne dans l'API de CE serveur, au nom de son auteur : la requête suit
/// tout le chemin habituel (licence, droits, verrous, idempotence, règles métier). Le secret qui l'autorise
/// est tiré au hasard à chaque démarrage, n'existe qu'en mémoire et n'est accepté que depuis ce poste.
/// </summary>
public sealed class CloudDispatcher(IServer server)
{
    public const string SecretHeader = "X-Scolia-Nuage";
    public const string UserHeader = "X-Scolia-Nuage-Compte";
    private readonly string secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>Transport vers l'API locale ; remplacé par le serveur de test dans les tests d'intégration.</summary>
    public Func<HttpMessageHandler>? HandlerFactory { get; set; }

    public bool Verify(string? given) =>
        given is { Length: > 0 and < 100 } &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(given), Encoding.ASCII.GetBytes(secret));

    public async Task<(int Status, string Message)> SendAsync(Guid userId, CloudOperation operation, CancellationToken ct)
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(operation.Method), operation.Path)
        {
            Content = new StringContent(operation.Body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", operation.Key);
        request.Headers.Add("X-Scolia", "1");
        request.Headers.Add(SecretHeader, secret);
        request.Headers.Add(UserHeader, userId.ToString());
        using var response = await client.SendAsync(request, ct);
        var status = (int)response.StatusCode;
        if (response.IsSuccessStatusCode) return (status, "Appliquée.");
        var message = $"Refus {status}.";
        try
        {
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (problem.RootElement.TryGetProperty("title", out var title) && title.GetString() is { Length: > 0 } text) message = text;
        }
        catch (JsonException) { }
        return (status, message);
    }

    private HttpClient CreateClient()
    {
        if (HandlerFactory is { } factory)
            return new HttpClient(factory(), disposeHandler: true) { BaseAddress = new Uri("http://localhost/"), Timeout = TimeSpan.FromSeconds(60) };
        var address = server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("Adresse du serveur local inconnue.");
        var uri = new Uri(address.Replace("://+", "://127.0.0.1", StringComparison.Ordinal)
            .Replace("://*", "://127.0.0.1", StringComparison.Ordinal)
            .Replace("0.0.0.0", "127.0.0.1", StringComparison.Ordinal)
            .Replace("[::]", "127.0.0.1", StringComparison.Ordinal)
            .Replace("localhost", "127.0.0.1", StringComparison.Ordinal));
        var handler = new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(5) };
        // Appel de ce poste à lui-même (127.0.0.1) : le certificat HTTPS éventuel est émis pour le nom public.
        handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, errors) =>
            errors is SslPolicyErrors.None or SslPolicyErrors.RemoteCertificateNameMismatch;
        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri($"{uri.Scheme}://127.0.0.1:{uri.Port}/"),
            Timeout = TimeSpan.FromSeconds(60),
        };
    }
}

/// <summary>Reconnaît les requêtes rejouées par <see cref="CloudDispatcher"/> et leur donne l'identité de leur auteur.</summary>
public static class CloudDispatchAuthentication
{
    public static IApplicationBuilder UseCloudDispatch(this IApplicationBuilder app) => app.Use(async (HttpContext http, RequestDelegate next) =>
    {
        if (!http.Request.Headers.TryGetValue(CloudDispatcher.SecretHeader, out var given))
        {
            await next(http);
            return;
        }
        var dispatcher = http.RequestServices.GetRequiredService<CloudDispatcher>();
        var remote = http.Connection.RemoteIpAddress;
        var local = remote is null || IPAddress.IsLoopback(remote);
        if (!local || !dispatcher.Verify(given.ToString())
            || http.Request.Headers.ContainsKey("X-Scolia-Relay-Connection")
            || !Guid.TryParse(http.Request.Headers[CloudDispatcher.UserHeader].ToString(), out var userId))
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await http.Response.WriteAsJsonAsync(new { title = "Reprise d'opération en ligne refusée." });
            return;
        }
        var db = http.RequestServices.GetRequiredService<ScoliaDbContext>();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, http.RequestAborted);
        if (user is null)
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await http.Response.WriteAsJsonAsync(new { title = "Compte de l'auteur introuvable ou désactivé." });
            return;
        }
        http.User = SessionFactory.Principal(user);
        await next(http);
    });
}
