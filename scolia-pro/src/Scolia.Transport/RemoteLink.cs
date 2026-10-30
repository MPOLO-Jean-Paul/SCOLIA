using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Scolia.Transport;

/// <summary>Invitation privée à un appareil, distincte des identifiants des comptes scolaires.</summary>
public sealed record RemoteLink(string Relay, string Room, string ClientToken, string Key, DateTime ExpiresUtc, string? HostToken = null)
{
    public const string Prefix = "scolia-link:v1:";
    public Uri VirtualServer => new($"http://{Room[..32]}.{Room[32..]}.scolia.invalid/");
    public bool CanHost => HostToken is not null;

    public static RemoteLink Create(string relay)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        return new RemoteLink(relay, Hash(token), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), DateTime.UtcNow.AddDays(30), token).Validate();
    }

    public RemoteLink Validate()
    {
        if (!Uri.TryCreate(Relay, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "wss" && !(uri.Scheme == "ws" && uri.IsLoopback)) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/")
            throw new FormatException("Le relais doit être une adresse wss:// avec un certificat valide, sans chemin ni identifiants.");
        foreach (var value in new[] { Room, ClientToken, Key })
            if (value is not { Length: 64 } || !value.All(Uri.IsHexDigit)) throw new FormatException("Invitation de liaison invalide.");
        if (HostToken is not null && (HostToken.Length != 64 || !HostToken.All(Uri.IsHexDigit) || Hash(HostToken) != Room))
            throw new FormatException("Identité de l'appareil principal invalide.");
        if (ExpiresUtc <= DateTime.UtcNow || ExpiresUtc > DateTime.UtcNow.AddDays(31))
            throw new FormatException("Cette invitation a expiré. Demandez une nouvelle liaison au secrétariat.");
        return this;
    }

    public string Invitation() => Prefix + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this with { HostToken = null }))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static RemoteLink Parse(string text)
    {
        text = text.Trim();
        if (text.Length > 4096 || !text.StartsWith(Prefix, StringComparison.Ordinal)) throw new FormatException("Collez l'invitation SCOLIA reçue du secrétariat.");
        try
        {
            var data = text[Prefix.Length..].Replace('-', '+').Replace('_', '/');
            data = data.PadRight((data.Length + 3) / 4 * 4, '=');
            var link = JsonSerializer.Deserialize<RemoteLink>(Convert.FromBase64String(data)) ?? throw new FormatException("Invitation vide.");
            if (link.HostToken is not null) throw new FormatException("Une clé d'hébergement ne peut pas servir d'invitation.");
            return link.Validate();
        }
        catch (JsonException ex) { throw new FormatException("Invitation SCOLIA illisible.", ex); }
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public Uri Endpoint(string role) => new(new Uri(Relay), $"v1/{role}/{Room}");
}
