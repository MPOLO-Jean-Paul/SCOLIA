using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace Scolia.Transport;

/// <summary>L'appareil principal ouvre lui-même la connexion sortante. Aucun port entrant à ouvrir sur Internet.</summary>
public sealed class RemoteHost : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Task running;
    private int connected;
    public bool Connected => Volatile.Read(ref connected) == 1;

    public RemoteHost(RemoteLink link, Uri server)
    {
        if (!link.Validate().CanHost || !server.IsLoopback) throw new ArgumentException("Hébergement distant réservé au serveur local.");
        running = RunAsync(link, server, stop.Token);
    }

    public Task Completion => running;

    private async Task RunAsync(RemoteLink link, Uri server, CancellationToken ct)
    {
        var delay = 2;
        while (!ct.IsCancellationRequested && link.ExpiresUtc > DateTime.UtcNow)
        {
            using var socket = new ClientWebSocket();
            using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
            session.CancelAfter(link.ExpiresUtc - DateTime.UtcNow);
            using var send = new SemaphoreSlim(1, 1);
            var epoch = RandomNumberGenerator.GetBytes(32);
            var seen = new HashSet<Guid>();
            var calls = new ConcurrentDictionary<Guid, Task>();
            var cancellations = new ConcurrentDictionary<Guid, CancellationTokenSource>();
            try
            {
                socket.Options.SetRequestHeader("X-Scolia-Host", link.HostToken!);
                socket.Options.SetRequestHeader("X-Scolia-Pair", RemoteLink.Hash(link.ClientToken));
                socket.Options.SetRequestHeader("X-Scolia-Epoch", Convert.ToBase64String(epoch));
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                using var connect = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connect.CancelAfter(TimeSpan.FromSeconds(8));
                await socket.ConnectAsync(link.Endpoint("host"), connect.Token);
                var acknowledged = await RelayWire.ReceiveAsync(socket, connect.Token);
                if (!acknowledged.AsSpan().SequenceEqual(epoch)) throw new IOException("Le relais n'a pas confirmé l'hébergement.");
                Interlocked.Exchange(ref connected, 1);
                delay = 2;
                using var http = new HttpClient(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(4) })
                { BaseAddress = server, Timeout = Timeout.InfiniteTimeSpan };
                while (!ct.IsCancellationRequested && link.ExpiresUtc > DateTime.UtcNow)
                {
                    var frame = await RelayWire.ReceiveAsync(socket, session.Token);
                    if (frame.Length == 16)
                    {
                        if (cancellations.TryGetValue(new Guid(frame), out var cancelled))
                        {
                            try { await cancelled.CancelAsync(); }
                            catch (ObjectDisposedException) { }
                        }
                        continue;
                    }
                    if (frame.Length < 40) continue;
                    var id = new Guid(frame.AsSpan(0, 16));
                    if (seen.Contains(id)) continue;
                    byte[] clear;
                    try { clear = RelayWire.Open(link, epoch, frame, id, 0, false); }
                    catch (CryptographicException) { continue; }
                    seen.Add(id);
                    // Une nouvelle époque renouvelle les clés et borne la mémoire de l'anti-rejeu.
                    if (seen.Count > 10000) break;
                    if (calls.Count >= 16) continue;
                    var callStop = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
                    cancellations[id] = callStop;
                    var task = ForwardAsync(clear, id, link, epoch, http, socket, send, callStop.Token);
                    calls[id] = task;
                    _ = task.ContinueWith(_ =>
                    {
                        calls.TryRemove(id, out var removed);
                        if (cancellations.TryRemove(id, out var completed)) completed.Dispose();
                    }, CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
            }
            catch (Exception ex) when (ex is WebSocketException or IOException or OperationCanceledException or JsonException or HttpRequestException) { }
            finally
            {
                Interlocked.Exchange(ref connected, 0);
                await session.CancelAsync();
                socket.Abort();
                await Task.WhenAll(calls.Values);
                foreach (var cancellation in cancellations.Values) cancellation.Dispose();
            }
            if (ct.IsCancellationRequested) return;
            try { await Task.Delay(TimeSpan.FromSeconds(delay + Random.Shared.NextDouble()), ct); }
            catch (OperationCanceledException) { return; }
            delay = Math.Min(delay * 2, 60);
        }
    }

    private static readonly HashSet<string> AllowedHeaders = new(StringComparer.OrdinalIgnoreCase)
    { "Cookie", "X-Scolia", "X-Scolia-Poste", "X-Scolia-Appareil", "X-Scolia-Verrou", "Idempotency-Key", "Content-Type", "Accept" };

    private static async Task ForwardAsync(byte[] clear, Guid id, RemoteLink link, byte[] epoch, HttpClient http,
        ClientWebSocket socket, SemaphoreSlim send, CancellationToken ct)
    {
        long sequence = 0;
        try
        {
            var input = JsonSerializer.Deserialize<RemoteRequest>(clear) ?? throw new IOException("Requête invalide.");
            if (input.Path is null || input.Headers is null || input.Body is null) throw new IOException("Requête incomplète.");
            var uri = new Uri(http.BaseAddress!, input.Path);
            if (uri.Scheme != http.BaseAddress!.Scheme || uri.Authority != http.BaseAddress.Authority ||
                !uri.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal) ||
                uri.AbsolutePath.StartsWith("/api/bureau", StringComparison.Ordinal) ||
                input.Body.Length > RelayWire.MaxRequestBody ||
                input.Method is not ("GET" or "HEAD" or "POST" or "PUT" or "DELETE" or "PATCH"))
                throw new IOException("Route distante refusée.");
            using var request = new HttpRequestMessage(new HttpMethod(input.Method), uri);
            if (input.Method is not ("GET" or "HEAD")) request.Content = new ByteArrayContent(input.Body);
            foreach (var h in input.Headers.Where(h => AllowedHeaders.Contains(h.Key)))
                if (!request.Headers.TryAddWithoutValidation(h.Key, h.Value)) request.Content?.Headers.TryAddWithoutValidation(h.Key, h.Value);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var headers = response.Headers.Concat(response.Content.Headers)
                .Where(h => !h.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) && !h.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
            await SendAsync(JsonSerializer.SerializeToUtf8Bytes(new RemoteResponse((int)response.StatusCode, headers)));
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[16 * 1024];
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(), ct)) > 0)
            {
                total += read;
                if (total > RelayWire.MaxResponseBody) throw new IOException("Réponse trop volumineuse.");
                await SendAsync(buffer.AsMemory(0, read));
            }
            await SendAsync(ReadOnlyMemory<byte>.Empty);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or WebSocketException or JsonException or ArgumentException)
        {
            // Jamais de faux succès ni de fin de document après un flux interrompu.
            if (sequence == 0 && !ct.IsCancellationRequested)
            {
                try
                {
                    await SendAsync(JsonSerializer.SerializeToUtf8Bytes(new RemoteResponse(503, [])));
                    await SendAsync(ReadOnlyMemory<byte>.Empty);
                }
                catch (Exception failure) when (failure is WebSocketException or OperationCanceledException or IOException) { }
            }
        }

        async Task SendAsync(ReadOnlyMemory<byte> bytes)
        {
            var packet = RelayWire.Seal(link, epoch, id, sequence++, true, bytes.Span);
            await send.WaitAsync(ct);
            try { await socket.SendAsync(packet.AsMemory(), WebSocketMessageType.Binary, true, ct); }
            finally { send.Release(); }
        }
    }

    public void Dispose() { stop.Cancel(); /* Le jeton est libéré quand la boucle et ses requêtes sont terminées. */ _ = running.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default); }
}
