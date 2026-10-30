using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace Scolia.Transport;

/// <summary>Les cookies et les clés métier restent sur les appareils ; le relais ne voit que les trames chiffrées.</summary>
public sealed class RemoteHttpHandler(RemoteLink link, CookieContainer cookies) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        link.Validate();
        var socket = new ClientWebSocket();
        try
        {
            socket.Options.SetRequestHeader("X-Scolia-Client", link.ClientToken);
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connect.CancelAfter(TimeSpan.FromSeconds(8));
            await socket.ConnectAsync(link.Endpoint("client"), connect.Token);
            var epoch = await RelayWire.ReceiveAsync(socket, connect.Token);
            if (epoch.Length != 32) throw new IOException("Relais incompatible.");
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            if (body.Length > RelayWire.MaxRequestBody) throw new HttpRequestException("Ce fichier est trop volumineux pour la liaison distante. Utilisez le réseau local pour cet import.");
            var uri = request.RequestUri ?? throw new HttpRequestException("Adresse absente.");
            var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
            if (request.Content is not null)
                foreach (var h in request.Content.Headers) headers[h.Key] = h.Value.ToArray();
            var cookie = cookies.GetCookieHeader(link.VirtualServer);
            if (cookie.Length > 0) headers["Cookie"] = [cookie];
            var id = Guid.NewGuid();
            var payload = JsonSerializer.SerializeToUtf8Bytes(new RemoteRequest(request.Method.Method, uri.PathAndQuery, headers, body));
            await socket.SendAsync(RelayWire.Seal(link, epoch, id, 0, false, payload).AsMemory(), WebSocketMessageType.Binary, true, cancellationToken);
            var answer = await RelayWire.ReceiveAsync(socket, cancellationToken);
            var metadata = JsonSerializer.Deserialize<RemoteResponse>(RelayWire.Open(link, epoch, answer, id, 0, true))
                ?? throw new IOException("Réponse distante invalide.");
            var response = new HttpResponseMessage((HttpStatusCode)metadata.Status)
            {
                RequestMessage = request,
                Content = new StreamContent(new ResponseStream(socket, link, epoch, id)),
            };
            foreach (var header in metadata.Headers)
            {
                if (header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                    foreach (var value in header.Value) cookies.SetCookies(link.VirtualServer, value);
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
