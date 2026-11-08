using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Scolia.Domain.Common;

namespace Scolia.Domain.Cloud;

/// <summary>
/// Chiffrement de la continuité en ligne, commun au serveur et aux appareils : AES-256-GCM,
/// nonce aléatoire, contexte authentifié (une copie de base ne peut pas passer pour une opération).
/// La clé appartient à l'école : ni JP-Services ni Cloudflare ne la reçoivent.
/// Format : « SCOLIA-NUAGE-1 » | nonce (12) | étiquette (16) | données chiffrées.
/// </summary>
public static class CloudSeal
{
    public const string BackupContext = "sauvegarde";
    public const string OperationContext = "operation";
    public const int KeySize = 32;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SCOLIA-NUAGE-1");
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const string Base32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] NewKey() => RandomNumberGenerator.GetBytes(KeySize);

    public static byte[] Seal(ReadOnlySpan<byte> plain, ReadOnlySpan<byte> key, string context)
    {
        if (key.Length != KeySize) throw new ArgumentException("Clé de 256 bits requise.", nameof(key));
        var result = new byte[Magic.Length + NonceSize + TagSize + plain.Length];
        Magic.CopyTo(result, 0);
        var nonce = result.AsSpan(Magic.Length, NonceSize);
        var tag = result.AsSpan(Magic.Length + NonceSize, TagSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, result.AsSpan(Magic.Length + NonceSize + TagSize), tag, Associated(context));
        return result;
    }

    public static byte[] Open(ReadOnlySpan<byte> sealedBytes, ReadOnlySpan<byte> key, string context)
    {
        var header = Magic.Length + NonceSize + TagSize;
        if (key.Length != KeySize || sealedBytes.Length < header || !sealedBytes[..Magic.Length].SequenceEqual(Magic))
            throw new DomainException("nuage", "Données en ligne illisibles : format ou clé incorrects.");
        var plain = new byte[sealedBytes.Length - header];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(sealedBytes.Slice(Magic.Length, NonceSize), sealedBytes[header..],
                sealedBytes.Slice(Magic.Length + NonceSize, TagSize), plain, Associated(context));
            return plain;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plain);
            throw new DomainException("nuage", "Clé de l'école incorrecte ou données en ligne modifiées : lecture refusée.");
        }
    }

    private static byte[] Associated(string context) => [.. Magic, .. Encoding.UTF8.GetBytes(context)];

    /// <summary>Empreinte publique d'une clé (32 caractères hexadécimaux) : elle désigne la clé sans la révéler.</summary>
    public static string Fingerprint(ReadOnlySpan<byte> key)
    {
        byte[] data = [.. Encoding.ASCII.GetBytes("SCOLIA-NUAGE-CLE:"), .. key];
        return Convert.ToHexString(SHA256.HashData(data))[..32].ToLowerInvariant();
    }

    /// <summary>
    /// Code de secours : la clé de l'école écrite en 14 groupes de 4 caractères (base 32, sans 0/1/8/9 ambigus),
    /// avec 4 caractères de contrôle. Sans lui, une copie en ligne ne peut pas être restaurée sur un autre ordinateur.
    /// </summary>
    public static string RecoveryCode(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySize) throw new ArgumentException("Clé de 256 bits requise.", nameof(key));
        var text = ToBase32(key) + ToBase32(SHA256.HashData(key).AsSpan(0, 3))[..4];
        return string.Join('-', Enumerable.Range(0, text.Length / 4).Select(i => text.Substring(i * 4, 4)));
    }

    public static byte[] ParseRecoveryCode(string? code)
    {
        var text = new string((code ?? string.Empty).ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray())
            .Replace('0', 'O').Replace('1', 'I').Replace('8', 'B');
        if (text.Length != 56 || text.Any(c => !Base32.Contains(c, StringComparison.Ordinal)))
            throw new DomainException("codeSecours", "Code de secours incomplet : 56 caractères attendus (14 groupes de 4).");
        var key = FromBase32(text[..52])[..KeySize];
        if (!string.Equals(ToBase32(SHA256.HashData(key).AsSpan(0, 3))[..4], text[52..], StringComparison.Ordinal))
            throw new DomainException("codeSecours", "Code de secours erroné : vérifiez chaque groupe.");
        return key;
    }

    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true)) gzip.Write(data);
        return output.ToArray();
    }

    public static byte[] Decompress(byte[] data, long maximum)
    {
        using var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > maximum) throw new DomainException("nuage", "Copie en ligne trop volumineuse une fois décompressée.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static string ToBase32(ReadOnlySpan<byte> data)
    {
        var builder = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = ((buffer << 8) | b) & 0xFFFF;
            bits += 8;
            while (bits >= 5)
            {
                builder.Append(Base32[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) builder.Append(Base32[(buffer << (5 - bits)) & 31]);
        return builder.ToString();
    }

    private static byte[] FromBase32(string text)
    {
        var bytes = new List<byte>(text.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in text)
        {
            buffer = ((buffer << 5) | Base32.IndexOf(c, StringComparison.Ordinal)) & 0xFFFF;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return [.. bytes];
    }
}

/// <summary>
/// Opération faite sur un appareil pendant une coupure du serveur, déposée en ligne (chiffrée).
/// <c>Delegation</c> : preuve remise par le serveur au compte connecté ; sans elle, rien n'est appliqué.
/// </summary>
public sealed record CloudOperation(string Key, string Method, string Path, string Body, string Label,
    string Delegation, DateTime CreatedUtc, string Device);

/// <summary>Accès de dépôt remis par le serveur à un compte connecté (jamais de clé de sauvegarde).</summary>
public sealed record CloudDepositAccess(bool Enabled, string? ServiceUrl, string? DepositToken, string? DepositKey,
    string? Delegation, DateTime? ExpiresUtc);

/// <summary>
/// Écritures du quotidien qui peuvent attendre le serveur (gardées sur l'appareil, déposées en ligne).
/// Règle unique, appliquée par l'appareil avant de garder une opération et par le serveur avant de
/// l'appliquer : les annulations, effacements, réglages et comptes demandent toujours le serveur.
/// </summary>
public static class CloudOperationRules
{
    private static readonly string[] Daily =
    [
        "api/apprenants", "api/inscriptions", "api/paiements", "api/depenses", "api/scolarite/presences",
        "api/scolarite/discipline", "api/scolarite/evaluations", "api/personnel",
    ];

    private static readonly string[] Refused = ["annulation", "effacement", "supprimer", "revoquer", "export", "apercu"];

    public static bool Queueable(string? method, string? path)
    {
        if (method is not ("POST" or "PUT" or "PATCH") || string.IsNullOrEmpty(path) || path.Length > 300) return false;
        if (path.StartsWith('/') || path.Contains("..", StringComparison.Ordinal) || path.Contains("//", StringComparison.Ordinal)
            || path.Contains('\\', StringComparison.Ordinal) || path.Any(char.IsWhiteSpace)) return false;
        if (Refused.Any(term => path.Contains(term, StringComparison.OrdinalIgnoreCase))) return false;
        return Daily.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal)
            && (path.Length == prefix.Length || path[prefix.Length] is '/' or '?'));
    }
}
