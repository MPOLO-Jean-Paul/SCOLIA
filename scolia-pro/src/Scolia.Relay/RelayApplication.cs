using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Scolia.Transport;

namespace Scolia.Relay;

/// <summary>Relais éphémère : aucune base, aucun fichier, aucun déchiffrement des dossiers scolaires.</summary>
public static class RelayApplication
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = 429;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        var app = builder.Build();
        var hosts = new ConcurrentDictionary<string, Host>(StringComparer.Ordinal);
        app.UseRateLimiter();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        app.MapGet("/health", () => Results.Ok(new { service = "SCOLIA relais", protocol = 1 }));

        app.MapGet("/v1/host/{room}", async (HttpContext http, string room) =>
        {
            var token = http.Request.Headers["X-Scolia-Host"].ToString();
            var pair = http.Request.Headers["X-Scolia-Pair"].ToString();
            byte[] epoch;
            try { epoch = Convert.FromBase64String(http.Request.Headers["X-Scolia-Epoch"].ToString()); }
            catch (FormatException) { http.Response.StatusCode = 400; return; }
            if (!http.WebSockets.IsWebSocketRequest || room.Length != 64 || token.Length != 64 ||
                RemoteLink.Hash(token) != room || pair.Length != 64 || !pair.All(Uri.IsHexDigit) || epoch.Length != 32)
            { http.Response.StatusCode = 403; return; }
            if (hosts.Count >= 128) { http.Response.StatusCode = 503; return; }
            using var socket = await http.WebSockets.AcceptWebSocketAsync();
            using var host = new Host(socket, pair, epoch);
            if (!hosts.TryAdd(room, host)) { socket.Abort(); return; }
            try
            {
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted, app.Lifetime.ApplicationStopping);
                lifetime.CancelAfter(TimeSpan.FromHours(1));
                while (!lifetime.IsCancellationRequested)
                {
                    var bytes = await RelayWire.ReceiveAsync(socket, lifetime.Token);
                    if (bytes.Length < 40) throw new IOException("Trame invalide.");
                    var id = new Guid(bytes.AsSpan(0, 16));
                    if (host.Clients.TryGetValue(id, out var client))
                    {
                        try { await client.SendAsync(bytes.AsMemory(), WebSocketMessageType.Binary, true, lifetime.Token); }
                        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException)
                        { host.Clients.TryRemove(id, out _); client.Abort(); }
                    }
                }
            }
            catch (Exception ex) when (ex is WebSocketException or IOException or OperationCanceledException) { }
            finally { hosts.TryRemove(room, out _); }
        });

        app.MapGet("/v1/client/{room}", async (HttpContext http, string room) =>
        {
            var token = http.Request.Headers["X-Scolia-Client"].ToString();
            if (!http.WebSockets.IsWebSocketRequest || token.Length != 64 || !hosts.TryGetValue(room, out var host) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(RemoteLink.Hash(token)), Encoding.ASCII.GetBytes(host.Pair)))
            { http.Response.StatusCode = 503; return; }
            if (host.Clients.Count >= 16) { http.Response.StatusCode = 429; return; }
            using var client = await http.WebSockets.AcceptWebSocketAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted, app.Lifetime.ApplicationStopping);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            Guid? id = null;
            try
            {
                await client.SendAsync(host.Epoch.AsMemory(), WebSocketMessageType.Binary, true, timeout.Token);
                using var initial = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                initial.CancelAfter(TimeSpan.FromSeconds(15));
                var request = await RelayWire.ReceiveAsync(client, initial.Token);
                if (request.Length < 40) throw new IOException("Trame invalide.");
                id = new Guid(request.AsSpan(0, 16));
                if (!host.Clients.TryAdd(id.Value, client)) throw new IOException("Requête répétée.");
                await host.Send.WaitAsync(timeout.Token);
                try { await host.Socket.SendAsync(request.AsMemory(), WebSocketMessageType.Binary, true, timeout.Token); }
                finally { host.Send.Release(); }
                // Une requête par connexion ; attend la fermeture après la réponse, sans mettre de données en file.
                await RelayWire.ReceiveAsync(client, timeout.Token);
            }
            catch (Exception ex) when (ex is WebSocketException or IOException or OperationCanceledException or ObjectDisposedException) { }
            finally
            {
                if (id is { } key)
                {
                    host.Clients.TryRemove(key, out _);
                    try
                    {
                        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await host.Send.WaitAsync(cancel.Token);
                        try { await host.Socket.SendAsync(key.ToByteArray().AsMemory(), WebSocketMessageType.Binary, true, cancel.Token); }
                        finally { host.Send.Release(); }
                    }
                    catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException) { }
                }
                client.Abort();
            }
        });
        return app;
    }

    private sealed class Host(WebSocket socket, string pair, byte[] epoch) : IDisposable
    {
        public WebSocket Socket { get; } = socket;
        public string Pair { get; } = pair;
        public byte[] Epoch { get; } = epoch;
        public SemaphoreSlim Send { get; } = new(1, 1);
        public ConcurrentDictionary<Guid, WebSocket> Clients { get; } = new();
        public void Dispose()
        {
            Socket.Abort();
            foreach (var client in Clients.Values) client.Abort();
            // Send est partagé avec les requêtes en cours ; pas de Dispose concurrent.
        }
    }
}

public static class RelayProgram
{
    public static Task Main(string[] args) => RelayApplication.Build(args).RunAsync();
}
