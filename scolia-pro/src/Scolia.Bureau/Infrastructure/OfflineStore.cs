using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Scolia.Bureau.Api;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Opération faite hors ligne, en attente d'envoi à l'ordinateur principal.</summary>
public sealed class OutboxItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Clé d'idempotence : le serveur n'applique l'opération qu'une fois, même renvoyée après une coupure.</summary>
    public string Key { get; set; } = string.Empty;
    public string Method { get; set; } = "POST";
    public string Path { get; set; } = string.Empty;
    public string Body { get; set; } = "{}";
    public string Label { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    /// <summary>Motif du refus du serveur, pour une opération passée en conflit.</summary>
    public string? Error { get; set; }
}

/// <summary>Session gardée pour se connecter sans réseau : le compte, ses droits et l'empreinte du mot de passe.</summary>
public sealed class OfflineCredential
{
    public string UserName { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public string Session { get; set; } = string.Empty;
    public DateTime SavedAt { get; set; }
}

/// <summary>
/// Données de l'établissement gardées sur cet appareil, pour continuer à travailler quand l'ordinateur principal
/// est éteint ou hors de portée du Wi-Fi :
/// les dernières pages lues (par utilisateur), les opérations faites hors ligne dans l'ordre, celles que le
/// serveur a refusées au retour, et de quoi se reconnecter sans réseau.
/// Tout est rangé par serveur : un appareil relié à deux établissements ne mélange rien.
/// </summary>
public sealed class OfflineStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly object gate = new();
    private readonly string root;

    public OfflineStore(Uri server, string? baseFolder = null)
    {
        var name = $"{server.Host}_{server.Port}".Replace(':', '_');
        root = Path.Combine(baseFolder ?? DefaultFolder, name);
        Directory.CreateDirectory(root);
    }

    public static string DefaultFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCOLIA", "hors-ligne");

    /// <summary>Utilisateur dont les pages sont gardées : chaque compte ne relit que ce qu'il a lui-même consulté.</summary>
    public string User { get; set; } = string.Empty;

    private string CacheFolder => Path.Combine(root, "pages", Safe(User));
    private string OutboxFile => Path.Combine(root, "envois.json");
    private string ConflictFile => Path.Combine(root, "conflits.json");
    private string CredentialFile => Path.Combine(root, "session.json");

    /// <summary>Garde la réponse d'une page lue en ligne.</summary>
    public void SavePage(string path, string json)
    {
        if (User.Length == 0) return;
        try
        {
            Directory.CreateDirectory(CacheFolder);
            File.WriteAllText(PageFile(path), json);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Dernière version gardée d'une page, avec l'heure où elle a été lue.</summary>
    public (string Json, DateTime SavedAt)? ReadPage(string path)
    {
        var file = PageFile(path);
        if (User.Length == 0 || !File.Exists(file)) return null;
        try { return (File.ReadAllText(file), File.GetLastWriteTime(file)); }
        catch (IOException) { return null; }
    }

    public IReadOnlyList<OutboxItem> Outbox() => Load(OutboxFile);
    public IReadOnlyList<OutboxItem> Conflicts() => Load(ConflictFile);

    public void Enqueue(OutboxItem item)
    {
        lock (gate)
        {
            var items = Load(OutboxFile).ToList();
            items.Add(item);
            Store(OutboxFile, items);
        }
    }

    /// <summary>Opération acceptée par le serveur : elle sort de la file.</summary>
    public void Sent(string id)
    {
        lock (gate) Store(OutboxFile, Load(OutboxFile).Where(i => i.Id != id).ToList());
    }

    /// <summary>Opération refusée par le serveur : elle passe en conflit, avec le motif, pour décision de l'utilisateur.</summary>
    public void Refused(string id, string error)
    {
        lock (gate)
        {
            var items = Load(OutboxFile).ToList();
            var item = items.FirstOrDefault(i => i.Id == id);
            if (item is null) return;
            items.Remove(item);
            item.Error = error;
            var conflicts = Load(ConflictFile).ToList();
            conflicts.Add(item);
            Store(OutboxFile, items);
            Store(ConflictFile, conflicts);
        }
    }

    /// <summary>Remet un conflit dans la file, pour un nouvel essai après correction sur l'autre poste.</summary>
    public void Retry(string id)
    {
        lock (gate)
        {
            var conflicts = Load(ConflictFile).ToList();
            var item = conflicts.FirstOrDefault(i => i.Id == id);
            if (item is null) return;
            conflicts.Remove(item);
            item.Error = null;
            var items = Load(OutboxFile).ToList();
            items.Add(item);
            Store(ConflictFile, conflicts);
            Store(OutboxFile, items);
        }
    }

    /// <summary>Abandonne un conflit : l'opération ne sera jamais envoyée.</summary>
    public void Discard(string id)
    {
        lock (gate) Store(ConflictFile, Load(ConflictFile).Where(i => i.Id != id).ToList());
    }

    /// <summary>
    /// Mois déjà encaissés hors ligne pour cette inscription, en attente d'envoi : ils ne se proposent plus,
    /// pour ne pas être payés deux fois depuis cet appareil.
    /// </summary>
    public IReadOnlySet<Guid> PendingFeeItems(Guid enrollmentId)
    {
        var ids = new HashSet<Guid>();
        foreach (var item in Outbox().Where(i => i.Path == "api/paiements"))
        {
            try
            {
                using var body = JsonDocument.Parse(item.Body);
                if (!body.RootElement.TryGetProperty("enrollmentId", out var e) || e.GetGuid() != enrollmentId) continue;
                if (body.RootElement.TryGetProperty("feeItemIds", out var fees))
                    foreach (var fee in fees.EnumerateArray()) ids.Add(fee.GetGuid());
            }
            catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException) { }
        }
        return ids;
    }

    /// <summary>Garde la session d'un compte après une connexion réussie, avec une empreinte salée du mot de passe.</summary>
    public void SaveCredential(string userName, string password, SessionDto session)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var credential = new OfflineCredential
        {
            UserName = userName.Trim(),
            Salt = Convert.ToBase64String(salt),
            Hash = Convert.ToBase64String(Derive(password, salt)),
            Session = JsonSerializer.Serialize(session, Api.Json.Options),
            SavedAt = DateTime.Now,
        };
        try { File.WriteAllText(CredentialFile, JsonSerializer.Serialize(credential, Json)); }
        catch (IOException) { }
    }

    /// <summary>
    /// Connexion sans réseau : accordée seulement au dernier compte connecté sur cet appareil, avec son mot de passe.
    /// </summary>
    public SessionDto? OfflineLogin(string userName, string password)
    {
        if (!File.Exists(CredentialFile)) return null;
        try
        {
            var credential = JsonSerializer.Deserialize<OfflineCredential>(File.ReadAllText(CredentialFile), Json);
            if (credential is null || !string.Equals(credential.UserName, userName.Trim(), StringComparison.OrdinalIgnoreCase)) return null;
            var expected = Convert.FromBase64String(credential.Hash);
            var actual = Derive(password, Convert.FromBase64String(credential.Salt));
            if (!CryptographicOperations.FixedTimeEquals(expected, actual)) return null;
            return JsonSerializer.Deserialize<SessionDto>(credential.Session, Api.Json.Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException) { return null; }
    }

    public bool HasCredential => File.Exists(CredentialFile);

    private static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32);

    private string PageFile(string path) =>
        Path.Combine(CacheFolder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..32] + ".json");

    private static string Safe(string text) =>
        string.Concat(text.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_'));

    private static List<OutboxItem> Load(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<List<OutboxItem>>(File.ReadAllText(file), Json) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or JsonException) { return []; }
    }

    private static void Store(string file, List<OutboxItem> items)
    {
        // Écriture dans un fichier voisin puis remplacement : une coupure ne laisse jamais une file à moitié écrite.
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(items, Json));
        File.Move(temp, file, overwrite: true);
    }
}
