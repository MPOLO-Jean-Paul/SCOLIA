using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Scolia.Transport;

/// <summary>Invitation privée à un appareil, distincte des identifiants des comptes scolaires.</summary>
public sealed record RemoteLink(string Relay, string Room, string ClientToken, string Key, DateTime ExpiresUtc, string? HostToken = null)
{
    public const string Prefix = "scolia-link:v1:";
    /// <summary>Relais institutionnel, configuré une seule fois. Remplaçable dans Réseau et activité.</summary>
    public const string DefaultRelay = "wss://scolia-pro-relay.onrender.com/";
    /// <summary>Durée historique des invitations (migration) ; les appareils restent liés jusqu'à révocation.</summary>
    public const int LinkValidityDays = 365;
    /// <summary>Second relais optionnel ; la salle et les clés de chiffrement restent identiques.</summary>
    public string? BackupRelay { get; init; }

    /// <summary>Les liaisons Cloudflare déjà enregistrées peuvent essayer Render sans nouveau QR.</summary>
    public string? EffectiveBackupRelay => !string.IsNullOrWhiteSpace(BackupRelay) ? BackupRelay
        : !string.Equals(Relay, DefaultRelay, StringComparison.OrdinalIgnoreCase) &&
          !new Uri(Relay).IsLoopback ? DefaultRelay : null;

    /// <summary>Autre chemin réseau vers la MEME salle chiffrée, sans renouvellement de clé.</summary>
    public RemoteLink Via(string relay) => (this with { Relay = relay, BackupRelay = null }).Validate();
    public Uri VirtualServer => new($"http://{Room[..32]}.{Room[32..]}.scolia.invalid/");
    public bool CanHost => HostToken is not null;

    public static RemoteLink Create(string relay)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        return new RemoteLink(relay, Hash(token), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), DateTime.MaxValue, token).Validate();
    }

    public RemoteLink Renew(string relay)
    {
        if (!CanHost) throw new InvalidOperationException("Seul l'appareil principal renouvelle sa liaison.");
        // Rotation EXPLICITE de sécurité : l'ancienne clé partagée est révoquée.
        // Ne jamais appeler Renew() pour une simple coupure ou réactivation.
        return (this with
        {
            Relay = relay,
            ClientToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            Key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            ExpiresUtc = DateTime.MaxValue,
        }).Validate();
    }

    /// <summary>Reprise ordinaire après une coupure ou migration d'une ancienne date limite.
    /// Aucune clé n'est remplacée ; la liste des appareils reste la source d'autorisation.</summary>
    public RemoteLink Resume()
    {
        if (!CanHost) throw new InvalidOperationException("Seul l'appareil principal restaure sa salle.");
        return (this with { ExpiresUtc = DateTime.MaxValue }).Validate();
    }

    public RemoteLink Validate()
    {
        if (!Uri.TryCreate(Relay, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "wss" && !(uri.Scheme == "ws" && uri.IsLoopback)) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/")
            throw new FormatException("Le relais doit être une adresse wss:// avec un certificat valide, sans chemin ni identifiants.");
        if (!string.IsNullOrWhiteSpace(BackupRelay))
        {
            if (!Uri.TryCreate(BackupRelay, UriKind.Absolute, out var backup) ||
                (backup.Scheme != "wss" && !(backup.Scheme == "ws" && backup.IsLoopback)) ||
                backup.UserInfo.Length > 0 || backup.Query.Length > 0 ||
                backup.Fragment.Length > 0 || backup.AbsolutePath != "/" ||
                string.Equals(BackupRelay, Relay, StringComparison.OrdinalIgnoreCase))
                throw new FormatException("Le relais secondaire doit être une autre adresse wss:// valide.");
        }
        foreach (var value in new[] { Room, ClientToken, Key })
            if (value is not { Length: 64 } || !value.All(Uri.IsHexDigit)) throw new FormatException("Invitation de liaison invalide.");
        if (HostToken is not null && (HostToken.Length != 64 || !HostToken.All(Uri.IsHexDigit) || Hash(HostToken) != Room))
            throw new FormatException("Identité de l'appareil principal invalide.");
        if (ExpiresUtc <= DateTime.UtcNow)
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
