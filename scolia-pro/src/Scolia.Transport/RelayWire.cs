using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace Scolia.Transport;

public static class RelayWire
{
    public const int MaxMessage = 1024 * 1024;
    public const int MaxRequestBody = 512 * 1024;
    public const long MaxResponseBody = 64 * 1024 * 1024;

    public static async Task<byte[]> ReceiveAsync(WebSocket socket, CancellationToken ct)
    {
        using var result = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await socket.ReceiveAsync(buffer.AsMemory(), ct);
            if (read.MessageType == WebSocketMessageType.Close) throw new IOException("Appareil distant indisponible.");
            if (read.MessageType != WebSocketMessageType.Binary || result.Length + read.Count > MaxMessage)
                throw new IOException("Message de liaison refusé.");
            result.Write(buffer, 0, read.Count);
            if (read.EndOfMessage) return result.ToArray();
        }
    }

    public static byte[] Seal(RemoteLink link, byte[] epoch, Guid id, long sequence, bool response, ReadOnlySpan<byte> body)
    {
        if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        var frame = new byte[24 + body.Length + 16];
        id.TryWriteBytes(frame);
        BinaryPrimitives.WriteInt64BigEndian(frame.AsSpan(16, 8), sequence);
        var nonce = new byte[12];
        BinaryPrimitives.WriteInt64BigEndian(nonce.AsSpan(4), sequence);
        using var aes = new AesGcm(Derive(link, epoch, id, response), 16);
        aes.Encrypt(nonce, body, frame.AsSpan(24, body.Length), frame.AsSpan(24 + body.Length, 16), frame.AsSpan(0, 24));
        return frame;
    }

    public static byte[] Open(RemoteLink link, byte[] epoch, byte[] frame, Guid id, long sequence, bool response)
    {
        if (frame.Length < 40 || new Guid(frame.AsSpan(0, 16)) != id ||
            BinaryPrimitives.ReadInt64BigEndian(frame.AsSpan(16, 8)) != sequence)
            throw new CryptographicException("Ordre ou identité du message de liaison invalide.");
        var clear = new byte[frame.Length - 40];
        var nonce = new byte[12];
        BinaryPrimitives.WriteInt64BigEndian(nonce.AsSpan(4), sequence);
        using var aes = new AesGcm(Derive(link, epoch, id, response), 16);
        aes.Decrypt(nonce, frame.AsSpan(24, clear.Length), frame.AsSpan(frame.Length - 16), clear, frame.AsSpan(0, 24));
        return clear;
    }

    private static byte[] Derive(RemoteLink link, byte[] epoch, Guid id, bool response)
    {
        if (epoch.Length != 32) throw new CryptographicException("Session de liaison invalide.");
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, Convert.FromHexString(link.Key), 32, epoch,
            Encoding.UTF8.GetBytes($"SCOLIA/remote/v1/{link.Room}/{id:N}/{(response ? "response" : "request")}"));
    }
}

public sealed record RemoteRequest(string Method, string Path, Dictionary<string, string[]> Headers, byte[] Body);
public sealed record RemoteResponse(int Status, Dictionary<string, string[]> Headers);
