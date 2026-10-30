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
    public string SessionCookie { get; set; } = string.Empty;
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
    private readonly OfflineDatabase database;

    public OfflineStore(Uri server, string? baseFolder = null)
    {
        var name = $"{server.Host}_{server.Port}".Replace(':', '_');
        root = Path.Combine(baseFolder ?? DefaultFolder, name);
        Directory.CreateDirectory(root);
        database = new OfflineDatabase(root);
        MigrateJournal();
    }

    public static string DefaultFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCOLIA", "hors-ligne");

    /// <summary>Utilisateur dont les pages sont gardées : chaque compte ne relit que ce qu'il a lui-même consulté.</summary>
    public string User { get; set; } = string.Empty;

    private string CacheFolder => Path.Combine(root, "pages", Safe(User));
    private string OutboxFile => "envois.json";
    private string ConflictFile => "conflits.json";
    private string CredentialFile => "session.json";

    private sealed record SavedPage(string Json, DateTime SavedAt);
    private string PageKey(string path) => "page:" + Safe(User) + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));

    /// <summary>Pages chiffrées dans SQLite ; les anciennes pages sont migrées à leur première lecture.</summary>
    public void SavePage(string path, string json)
    {
        if (User.Length == 0) return;
        database.Write(PageKey(path), JsonSerializer.Serialize(new SavedPage(json, DateTime.Now), Json));
    }

    public (string Json, DateTime SavedAt)? ReadPage(string path)
    {
        if (User.Length == 0) return null;
        if (database.Read(PageKey(path)) is { } text)
        {
            var page = JsonSerializer.Deserialize<SavedPage>(text, Json) ?? throw new InvalidDataException("Cache local invalide.");
            return (page.Json, page.SavedAt);
        }
        var file = PageFile(path);
        if (!File.Exists(file)) return null;
        var json = File.ReadAllText(file);
        var savedAt = File.GetLastWriteTime(file);
        database.Write(PageKey(path), JsonSerializer.Serialize(new SavedPage(json, savedAt), Json));
        ArchiveLegacy(file);
        return (json, savedAt);
    }

    public IReadOnlyList<OutboxItem> Outbox() => Load(OutboxFile).Where(i => i.User == User).ToList();
    public IReadOnlyList<OutboxItem> Conflicts() => Load(ConflictFile).Where(i => i.User == User).ToList();

    public void Enqueue(OutboxItem item)
    {
        if (string.IsNullOrEmpty(item.User)) item.User = User;
        Change((pending, _) => { if (pending.All(i => i.Id != item.Id)) pending.Add(item); });
    }

    public void Sent(string id) => Change((pending, _) => pending.RemoveAll(i => i.Id == id && i.User == User));

    public void Refused(string id, string error) => Change((pending, conflicts) =>
    {
        var item = pending.FirstOrDefault(i => i.Id == id && i.User == User);
        if (item is null) return;
        item.Error = error;
        pending.Remove(item);
        conflicts.Add(item);
    });

    public void Retry(string id) => Change((pending, conflicts) =>
    {
        var item = conflicts.FirstOrDefault(i => i.Id == id && i.User == User);
        if (item is null) return;
        conflicts.Remove(item);
        item.Error = null;
        pending.Add(item);
        pending.Sort((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
    });

    public void Discard(string id) => Change((_, conflicts) => conflicts.RemoveAll(i => i.Id == id && i.User == User));

    private void Change(Action<List<OutboxItem>, List<OutboxItem>> change)
    {
        lock (gate) database.Update([OutboxFile, ConflictFile], values =>
        {
            var pending = Decode(values[OutboxFile]);
            var conflicts = Decode(values[ConflictFile]);
            change(pending, conflicts);
            values[OutboxFile] = JsonSerializer.Serialize(pending, Json);
            values[ConflictFile] = JsonSerializer.Serialize(conflicts, Json);
        });
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
    public void SaveCredential(string userName, string password, SessionDto session, string sessionCookie = "")
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var credential = new OfflineCredential
        {
            UserName = session.UserName,
            Salt = Convert.ToBase64String(salt),
            Hash = Convert.ToBase64String(Derive(password, salt)),
            Session = JsonSerializer.Serialize(session, Api.Json.Options),
            SavedAt = DateTime.Now,
            SessionCookie = sessionCookie,
        };
        database.Write(CredentialFile, JsonSerializer.Serialize(credential, Json));
    }

    /// <summary>
    /// Connexion sans réseau : accordée seulement au dernier compte connecté sur cet appareil, avec son mot de passe.
    /// </summary>
    public SessionDto? OfflineLogin(string userName, string password)
    {
        if (database.Read(CredentialFile) is not { Length: > 0 } credentialJson) return null;
        try
        {
            var credential = JsonSerializer.Deserialize<OfflineCredential>(credentialJson, Json);
            if (credential is null || !string.Equals(credential.UserName, userName.Trim(), StringComparison.OrdinalIgnoreCase)) return null;
            var expected = Convert.FromBase64String(credential.Hash);
            var actual = Derive(password, Convert.FromBase64String(credential.Salt));
            if (!CryptographicOperations.FixedTimeEquals(expected, actual)) return null;
            return JsonSerializer.Deserialize<SessionDto>(credential.Session, Api.Json.Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException) { return null; }
    }

    /// <summary>Reprise différée : cookie limité à la durée de session, jamais de mot de passe stocké.</summary>
    public OfflineCredential? SavedCredential => database.Read(CredentialFile) is { Length: > 0 } text
        ? JsonSerializer.Deserialize<OfflineCredential>(text, Json) : null;

    public bool HasCredential => database.Read(CredentialFile) is { Length: > 0 };

    private static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32);

    private string PageFile(string path) =>
        Path.Combine(CacheFolder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..32] + ".json");

    private static string Safe(string text) =>
        string.Concat(text.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_'));

    private List<OutboxItem> Load(string key) => Decode(database.Read(key));
    private static List<OutboxItem> Decode(string? text) =>
        text is null ? [] : JsonSerializer.Deserialize<List<OutboxItem>>(text, Json) ?? throw new InvalidDataException("Journal local invalide : restauration nécessaire, aucune opération abandonnée.");

    private void MigrateJournal()
    {
        var files = new[] { OutboxFile, ConflictFile, CredentialFile };
        database.Update(["migration:v1", .. files], values =>
        {
            if (values["migration:v1"] == "done") return;
            foreach (var file in files.Where(name => File.Exists(Path.Combine(root, name))))
            {
                var text = File.ReadAllText(Path.Combine(root, file));
                // Refuse une ancienne file corrompue plutôt que la remplacer silencieusement par une file vide.
                if (file == CredentialFile) _ = JsonSerializer.Deserialize<OfflineCredential>(text, Json) ?? throw new InvalidDataException("Session locale invalide.");
                else _ = Decode(text);
                values[file] ??= text;
            }
            values[OutboxFile] ??= "[]";
            values[ConflictFile] ??= "[]";
            values[CredentialFile] ??= string.Empty;
            values["migration:v1"] = "done";
        });
        foreach (var file in files.Where(name => File.Exists(Path.Combine(root, name)))) ArchiveLegacy(Path.Combine(root, file));
    }

    private static void ArchiveLegacy(string file)
    {
        // L'original reste récupérable, mais n'est plus conservé en clair dans le dossier privé.
        SecretStore.Save(file + ".avant-sqlite.enc", File.ReadAllBytes(file));
        File.Delete(file);
    }
}
