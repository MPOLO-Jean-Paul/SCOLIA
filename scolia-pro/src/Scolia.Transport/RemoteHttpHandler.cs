using System.Net;
using System.Globalization;
using System.Text;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace Scolia.Transport;

/// <summary>Les cookies et les clés métier restent sur les appareils ; le relais ne voit que les trames chiffrées.</summary>
public sealed record RemoteDeviceAccess(Guid GrantId, string DeviceId, string DeviceSecret);

public sealed class RemoteHttpHandler(RemoteLink link, CookieContainer cookies,
    Func<RemoteDeviceAccess?>? deviceAccess = null) : HttpMessageHandler
{
    // L'état est partagé entre les instances (sonde, pages, événements) d'une même salle.
    // Après échec du primaire, le secours est préféré pendant 30 s, puis retesté.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset>
        PreferredBackupUntil = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        link.Validate();
        var backup = link.EffectiveBackupRelay;
        if (backup is null) return await SendViaAsync(link, request, false, cancellationToken);
        var identity = link.Room + ":" + link.Relay;
        var backupFirst = PreferredBackupUntil.TryGetValue(identity, out var until) &&
            until > DateTimeOffset.UtcNow;
        var first = backupFirst ? link.Via(backup) : link.Via(link.Relay);
        var second = backupFirst ? link.Via(link.Relay) : link.Via(backup);
        try
        {
            var response = await SendViaAsync(first, request, !backupFirst, cancellationToken);
            // Le relais peut être accessible, mais ne plus avoir de serveur hébergé :
            // HTTP 502/503/504 est aussi une panne de transport, pas un refus métier.
            if (response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout)
            {
                response.Dispose();
                var fallback = await SendViaAsync(second, request, backupFirst, cancellationToken);
                if (fallback.StatusCode is not (HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
                    or HttpStatusCode.GatewayTimeout))
                {
                    if (backupFirst) PreferredBackupUntil.TryRemove(identity, out _);
                    else PreferredBackupUntil[identity] = DateTimeOffset.UtcNow.AddSeconds(30);
                }
                return fallback;
            }
            if (!backupFirst) PreferredBackupUntil.TryRemove(identity, out _);
            return response;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
            ex is HttpRequestException or System.Net.WebSockets.WebSocketException or IOException or OperationCanceledException)
        {
            // La requête conserve sa clé Idempotency-Key lors du changement de voie :
            // un encaissement dont l'accusé a été perdu ne doit pas être enregistré deux fois.
            var result = await SendViaAsync(second, request, backupFirst, cancellationToken);
            if (!backupFirst)
                PreferredBackupUntil[identity] = DateTimeOffset.UtcNow.AddSeconds(30);
            else
                PreferredBackupUntil.TryRemove(identity, out _);
            return result;
        }
    }

    private async Task<HttpResponseMessage> SendViaAsync(RemoteLink route, HttpRequestMessage request,
        bool shortConnect, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        try
        {
            socket.Options.SetRequestHeader("X-Scolia-Client", route.ClientToken);
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            socket.Options.KeepAliveTimeout = RemoteHost.KeepAliveTimeout;
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // Le relais peut se réveiller (relais gratuit) : laisser le temps de la poignée de main sur un réseau mobile.
            connect.CancelAfter(shortConnect ? TimeSpan.FromSeconds(8) : TimeSpan.FromSeconds(25));
            await socket.ConnectAsync(route.Endpoint("client"), connect.Token);
            var epoch = await RelayWire.ReceiveAsync(socket, connect.Token);
            if (epoch.Length != 32) throw new IOException("Relais incompatible.");
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            if (body.Length > RelayWire.MaxRequestBody) throw new HttpRequestException("Ce fichier est trop volumineux pour la liaison distante. Utilisez le réseau local pour cet import.");
            var uri = request.RequestUri ?? throw new HttpRequestException("Adresse absente.");
            var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
            if (request.Content is not null)
                foreach (var h in request.Content.Headers) headers[h.Key] = h.Value.ToArray();
            var cookie = cookies.GetCookieHeader(route.VirtualServer);
            if (cookie.Length > 0) headers["Cookie"] = [cookie];
            // Autorisation individuelle, signée pour CETTE requête et CET appareil.
            // La clé ne traverse jamais le réseau ; le relais ne voit que des trames chiffrées.
            if (deviceAccess?.Invoke() is { } grant)
            {
                var stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                var signed = $"{grant.GrantId:N}\n{grant.DeviceId}\n{stamp}\n{nonce}\n{request.Method.Method}\n{uri.PathAndQuery}";
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(grant.DeviceSecret));
                var proof = HMACSHA256.HashData(hash, Encoding.UTF8.GetBytes(signed));
                headers["X-Scolia-Grant"] = [grant.GrantId.ToString("N")];
                headers["X-Scolia-Device-Time"] = [stamp];
                headers["X-Scolia-Device-Nonce"] = [nonce];
                headers["X-Scolia-Device-Proof"] = [Convert.ToHexString(proof)];
            }
            var id = Guid.NewGuid();
            var payload = JsonSerializer.SerializeToUtf8Bytes(new RemoteRequest(request.Method.Method, uri.PathAndQuery, headers, body));
            await socket.SendAsync(RelayWire.Seal(route, epoch, id, 0, false, payload).AsMemory(), WebSocketMessageType.Binary, true, cancellationToken);
            var answer = await RelayWire.ReceiveAsync(socket, cancellationToken);
            var metadata = JsonSerializer.Deserialize<RemoteResponse>(RelayWire.Open(route, epoch, answer, id, 0, true))
                ?? throw new IOException("Réponse distante invalide.");
            var response = new HttpResponseMessage((HttpStatusCode)metadata.Status)
            {
                RequestMessage = request,
                Content = new StreamContent(new ResponseStream(socket, route, epoch, id)),
            };
            foreach (var header in metadata.Headers)
            {
                if (header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                    foreach (var value in header.Value) cookies.SetCookies(route.VirtualServer, value);
                if (!response.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            return response;
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or CryptographicException)
        {
            socket.Dispose();
            throw new HttpRequestException("L'appareil principal ou la liaison Internet est indisponible. Les opérations en attente restent sur cet appareil.", ex);
        }
        catch { socket.Dispose(); throw; }
    }

    private sealed class ResponseStream(ClientWebSocket socket, RemoteLink link, byte[] epoch, Guid id) : Stream
    {
        private byte[] current = [];
        private int offset;
        private long sequence = 1;
        private long total;
        private bool ended;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length == 0 || ended) return 0;
            if (offset == current.Length)
            {
                try
                {
                    current = RelayWire.Open(link, epoch, await RelayWire.ReceiveAsync(socket, cancellationToken), id, sequence++, true);
                    offset = 0;
                    if (current.Length == 0) { ended = true; socket.Dispose(); return 0; }
                    total += current.Length;
                    if (total > RelayWire.MaxResponseBody) throw new IOException("Document distant trop volumineux.");
                }
                catch (Exception ex) when (ex is WebSocketException or CryptographicException)
                { throw new IOException("La liaison distante a été interrompue.", ex); }
            }
            var length = Math.Min(buffer.Length, current.Length - offset);
            current.AsMemory(offset, length).CopyTo(buffer);
            offset += length;
            return length;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        protected override void Dispose(bool disposing) { if (disposing) socket.Dispose(); base.Dispose(disposing); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
