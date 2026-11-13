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
    /// <summary>Copie chiffrée déposée en ligne : le serveur l'appliquera même si cet appareil ne le revoit pas.</summary>
    public bool Deposited { get; set; }
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
    private string currentUser = string.Empty;
    private long searchRevision;
    public string User
    {
        get => System.Threading.Volatile.Read(ref currentUser);
        set
        {
            var previous = System.Threading.Interlocked.Exchange(ref currentUser, value ?? string.Empty);
            if (!string.Equals(previous, value, StringComparison.Ordinal))
                System.Threading.Interlocked.Increment(ref searchRevision);
        }
    }

    /// <summary>
    /// Révision locale du registre, utilisée pour ne pas reparcourir 10 000
    /// apprenants à chaque frappe. Tout changement de compte invalide l'index.
    /// </summary>
    // Dernière décision de lecture seule reçue du serveur de l'établissement.
    // Un poste hors ligne ne peut pas contourner une restriction déjà connue.
    private static string LicenceKeyFor(string account) =>
        "license:read-only:" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(account.Trim().ToUpperInvariant())));
    public bool IsLicenseReadOnlyFor(string account) =>
        !string.IsNullOrWhiteSpace(account) &&
        database.Read(LicenceKeyFor(account)) == "1";
    public void SaveLicenseReadOnlyFor(string account, bool restricted)
    {
        if (string.IsNullOrWhiteSpace(account)) return;
        database.Write(LicenceKeyFor(account), restricted ? "1" : "0");
    }

    public long SearchRevision => System.Threading.Interlocked.Read(ref searchRevision);

    private string CacheFolder => Path.Combine(root, "pages", Safe(User));
    private string OutboxFile => "envois.json";
    private string ConflictFile => "conflits.json";
    private string CredentialFile => "session.json";
    private const string LastAccountKey = "dernier-compte";
    private const string KnownAccountsKey = "comptes-connus:v1";
    private static string CredentialKeyFor(string account) =>
        "session:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account.Trim().ToUpperInvariant())));

    private sealed record SavedPage(string Json, DateTime SavedAt, string? Path = null);

    // Point de reprise propre au compte et à cet établissement, conservé dans SQLite chiffré.
    // L'époque change au redémarrage du serveur : aucune révision ne peut être confondue.
    private static string SyncKeyFor(string user) =>
        "sync:revision:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.ToUpperInvariant())));
    public string? ReadSyncCheckpointFor(string user) =>
        string.IsNullOrWhiteSpace(user) ? null : database.Read(SyncKeyFor(user));
    public void SaveSyncCheckpointFor(string user, string checkpoint)
    {
        if (string.IsNullOrWhiteSpace(user)) return;
        database.Write(SyncKeyFor(user), checkpoint);
    }

    // Curseur de préparation indépendant du flux "api/revision" et de l'outbox.
    // Portée : emplacement du serveur (root), compte (haché), révision et droits.
    // Seules les ressources obtenues intégralement sont marquées, APRÈS la
    // sauvegarde réussie dans SQLite chiffré. Aucune liste d'élèves n'est exportée.
    private static string PreparedPrefixFor(string user) =>
        "preparation:v3:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(user.Trim().ToUpperInvariant()))) + ":";
    private static string PreparedRevisionKeyFor(string user) => PreparedPrefixFor(user) + "revision";
    private static string PreparedResourceKeyFor(string user, string path) =>
        PreparedPrefixFor(user) + "done:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(path)));

    /// <summary>
    /// Lorsqu'un nouvel état serveur arrive, repartir sur une validation complète
    /// mais uniquement pour cette révision. Ne pas modifier le contenu du cache :
    /// les GET/304 ne récupèrent pas à nouveau les JSON identiques.
    /// </summary>
    public void BeginPreparationFor(string user, string revision)
    {
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(revision))
            throw new ArgumentException("Compte et révision de préparation requis.");
        lock (gate)
        {
            if (database.Read(PreparedRevisionKeyFor(user)) == revision) return;
            database.DeletePrefix(PreparedPrefixFor(user));
            database.Write(PreparedRevisionKeyFor(user), revision);
        }
    }

    /// <summary>Vrai seulement si cette ressource a déjà été vérifiée pour cette
    /// révision ET demeure physiquement dans le cache du compte.</summary>
    public bool WasPreparedFor(string user, string revision, string path)
    {
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(path) ||
            string.IsNullOrWhiteSpace(revision)) return false;
        return database.Read(PreparedRevisionKeyFor(user)) == revision
            && database.Read(PreparedResourceKeyFor(user, path)) == "ok";
    }

    /// <summary>Écriture durable après réception complète ; l'arrêt du programme,
    /// une perte réseau ou un nouveau login conservent cette marque.</summary>
    public void MarkPreparedFor(string user, string revision, string path)
    {
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(revision) ||
            string.IsNullOrWhiteSpace(path)) return;
        lock (gate)
        {
            if (database.Read(PreparedRevisionKeyFor(user)) == revision)
                database.Write(PreparedResourceKeyFor(user, path), "ok");
        }
    }

    /// <summary>Une réponse HTTP 304 confirme la fraîcheur, sans réécrire l'index ni re-télécharger.</summary>
    public void TouchPageFor(string user, string path, string json)
    {
        if (string.IsNullOrWhiteSpace(user)) return;
        database.Write(PageKeyFor(user, path),
            JsonSerializer.Serialize(new SavedPage(json, DateTime.Now, path), Json));
    }
    private string PagePrefix => "page:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(User.ToUpperInvariant()))) + ":";
    private string PageKey(string path) => PageKeyFor(User, path);
    private static string PageKeyFor(string user, string path) =>
        "page:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.ToUpperInvariant()))) +
        ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
    /// <summary>
    /// Pages locales à réactualiser, les plus anciennes d'abord. Un classement par hash SQLite
    /// retraitait toujours les mêmes 32 pages et laissait les autres périmées indéfiniment.
    /// La lecture reste bornée aux 2048 chemins du compte courant ; le travail réseau est
    /// limité séparément (24 pages par exécution WorkManager).
    /// </summary>
    public IReadOnlyList<string> CachedPaths(int limit = 32)
    {
        if (string.IsNullOrWhiteSpace(User) || limit <= 0) return [];
        return database.ReadPrefix(PagePrefix, 2048)
            .Select(text =>
            {
                try { return JsonSerializer.Deserialize<SavedPage>(text, Json); }
                catch (JsonException) { return null; }
            })
            .OfType<SavedPage>()
            .Where(p => !string.IsNullOrWhiteSpace(p.Path) && p.Path != "api/etablissement/logo")
            .OrderBy(p => p.SavedAt)
            .ThenBy(p => p.Path!, StringComparer.Ordinal)
            .Take(Math.Min(limit, 128))
            .Select(p => p.Path!)
            .ToList();
    }

    /// <summary>Pages chiffrées dans SQLite ; les anciennes pages sont migrées à leur première lecture.</summary>
    public void SavePage(string path, string json) => SavePageFor(User, path, json);

    /// <summary>
    /// Récupère des pages de registre en une requête SQLite puis reconstitue
    /// uniquement les clés du compte courant. Compatible avec l'ancien cache
    /// JSON individuel encore présent après une migration non destructive.
    /// </summary>
    public IReadOnlyDictionary<string, string> ReadPages(IReadOnlyList<string> paths)
    {
        var account = User;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (account.Length == 0 || paths.Count == 0) return result;
        var keys = paths.Select(path => (Path: path, Key: PageKeyFor(account, path))).ToArray();
        var saved = database.ReadMany(keys.Select(x => x.Key).ToArray());
        foreach (var item in keys)
        {
            if (!string.Equals(account, User, StringComparison.Ordinal)) break;
            if (saved.TryGetValue(item.Key, out var stored))
            {
                var page = JsonSerializer.Deserialize<SavedPage>(stored, Json)
                    ?? throw new InvalidDataException("Cache local invalide.");
                result[item.Path] = page.Json;
                continue;
            }
            // Migration à la demande, uniquement si la page existait réellement.
            var file = Path.Combine(root, "pages", Safe(account),
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.Path)))[..32] + ".json");
            if (!File.Exists(file)) continue;
            var json = File.ReadAllText(file);
            SavePageFor(account, item.Path, json);
            ArchiveLegacy(file);
            result[item.Path] = json;
        }
        return result;
    }

    /// <summary>
    /// Écrit sous le compte qui a déclenché la requête HTTP. Une connexion à
    /// un autre compte pendant l'attente réseau ne peut pas transférer ses données.
    /// </summary>
    public void SavePageFor(string user, string path, string json)
    {
        if (string.IsNullOrWhiteSpace(user)) return;
        database.Write(PageKeyFor(user, path),
            JsonSerializer.Serialize(new SavedPage(json, DateTime.Now, path), Json));
        if (string.Equals(user, User, StringComparison.Ordinal) &&
            (path.StartsWith("api/apprenants", StringComparison.Ordinal) ||
             path == "api/etablissement/promotions"))
            System.Threading.Interlocked.Increment(ref searchRevision);
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
        database.Write(PageKey(path), JsonSerializer.Serialize(new SavedPage(json, savedAt, path), Json));
        ArchiveLegacy(file);
        return (json, savedAt);
    }

    /// <summary>
    /// Un 403/404 du serveur invalide la copie locale de cette ressource :
    /// la conservation hors ligne ne doit pas contourner une révocation explicite.
    /// Le compte d'origine est conservé pour éviter d'effacer la copie d'un autre utilisateur.
    /// </summary>
    public void RemovePageFor(string user, string path)
    {
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(path)) return;
        database.Delete(PageKeyFor(user, path));
        if (string.Equals(user, User, StringComparison.Ordinal) &&
            path.StartsWith("api/apprenants", StringComparison.Ordinal))
            System.Threading.Interlocked.Increment(ref searchRevision);
        // Éviter la réimportation de l'ancien cache JSON après une suppression serveur.
        var legacy = Path.Combine(root, "pages", Safe(user),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..32] + ".json");
        if (File.Exists(legacy)) File.Delete(legacy);
    }

    public IReadOnlyList<OutboxItem> Outbox() => Load(OutboxFile).Where(i => i.User == User).ToList();
    public IReadOnlyList<OutboxItem> OutboxFor(string user) => Load(OutboxFile).Where(i => i.User == user).ToList();

    /// <summary>L'opération est en sécurité en ligne : elle reste dans la file jusqu'à son issue.</summary>
    public void MarkDepositedFor(string user, string id) => Change((pending, _) =>
    {
        foreach (var item in pending.Where(i => i.Id == id && i.User == user)) item.Deposited = true;
    });

    // Accès de dépôt en ligne remis par le serveur au compte (jeton, clé de dépôt, délégation signée),
    // gardé dans la base locale chiffrée et propre à chaque compte.
    private static string CloudKeyFor(string user) =>
        "nuage:depot:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.Trim().ToUpperInvariant())));
    public string? ReadCloudAccessFor(string user) =>
        string.IsNullOrWhiteSpace(user) ? null : database.Read(CloudKeyFor(user));
    public void SaveCloudAccessFor(string user, string json)
    {
        if (string.IsNullOrWhiteSpace(user)) return;
        database.Write(CloudKeyFor(user), json);
    }
    public IReadOnlyList<OutboxItem> Conflicts() => Load(ConflictFile).Where(i => i.User == User).ToList();

    public void Enqueue(OutboxItem item)
    {
        if (string.IsNullOrEmpty(item.User)) item.User = User;
        Change((pending, _) => { if (pending.All(i => i.Id != item.Id)) pending.Add(item); });
    }

    public void Sent(string id) => SentFor(User, id);

    /// <summary>Ne jamais solder la file du compte B lorsque l'envoi appartenait au compte A.</summary>
    public void SentFor(string user, string id) =>
        Change((pending, _) => pending.RemoveAll(i => i.Id == id && i.User == user));

    public void Refused(string id, string error) => RefusedFor(User, id, error);
    public void RefusedFor(string user, string id, string error) => Change((pending, conflicts) =>
    {
        var item = pending.FirstOrDefault(i => i.Id == id && i.User == user);
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
        System.Threading.Interlocked.Increment(ref searchRevision);
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

    /// <summary>Les empreintes des comptes restent cloisonnées par utilisateur dans le SQLite chiffré.</summary>
    private static OfflineCredential? DecodeCredential(string? value) =>
        string.IsNullOrEmpty(value) ? null : JsonSerializer.Deserialize<OfflineCredential>(value, Json);

    private OfflineCredential? ReadCredentialFor(string account)
    {
        if (string.IsNullOrWhiteSpace(account)) return null;
        var scoped = DecodeCredential(database.Read(CredentialKeyFor(account)));
        if (scoped is not null && string.Equals(scoped.UserName, account.Trim(), StringComparison.OrdinalIgnoreCase))
            return scoped;
        // Anciennes installations : conserver la session historique sans écraser ni croiser les comptes.
        var legacy = DecodeCredential(database.Read(CredentialFile));
        return legacy is not null && string.Equals(legacy.UserName, account.Trim(), StringComparison.OrdinalIgnoreCase)
            ? legacy : null;
    }

    /// <summary>Sauvegarde une preuve locale par compte, après connexion réussie au serveur.</summary>
    public void SaveCredential(string userName, string password, SessionDto session, string sessionCookie = "")
    {
        if (sessionCookie.Length == 0 && ReadCredentialFor(session.UserName) is { } previous)
            sessionCookie = previous.SessionCookie;
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
        var serialized = JsonSerializer.Serialize(credential, Json);
        // Garder la clé historique pour la compatibilité, et l'empreinte propre au compte
        // afin qu'un nouvel utilisateur ne remplace pas le cache d'un collègue.
        database.Update([CredentialFile, CredentialKeyFor(session.UserName)], values =>
        {
            values[CredentialFile] = serialized;
            values[CredentialKeyFor(session.UserName)] = serialized;
        });
    }

    /// <summary>Hors ligne : uniquement le compte demandé avec le bon secret ; jamais une autre identité.</summary>
    public SessionDto? OfflineLogin(string userName, string password)
    {
        try
        {
            var credential = ReadCredentialFor(userName);
            if (credential is null) return null;
            var expected = Convert.FromBase64String(credential.Hash);
            var actual = Derive(password, Convert.FromBase64String(credential.Salt));
            if (!CryptographicOperations.FixedTimeEquals(expected, actual)) return null;
            return JsonSerializer.Deserialize<SessionDto>(credential.Session, Api.Json.Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException) { return null; }
    }

    /// <summary>Dernière session de l'appareil, sans droit automatique d'entrée.</summary>
    public OfflineCredential? SavedCredential =>
        ReadCredentialFor(LastAccount ?? string.Empty) ?? DecodeCredential(database.Read(CredentialFile));

    public void ClearBackgroundSession()
    {
        string[] keys = string.IsNullOrWhiteSpace(User) ? [CredentialFile] : [CredentialFile, CredentialKeyFor(User)];
        database.Update(keys, values =>
        {
            foreach (var key in keys)
            {
                var saved = DecodeCredential(values[key]);
                if (saved is null || (User.Length > 0 &&
                    !string.Equals(saved.UserName, User, StringComparison.OrdinalIgnoreCase))) continue;
                saved.SessionCookie = string.Empty;
                values[key] = JsonSerializer.Serialize(saved, Json);
            }
        });
    }

    /// <summary>Le dernier identifiant utilisé, pas un jeton ni un mot de passe.</summary>
    public string? LastAccount => database.Read(LastAccountKey) is { Length: > 0 } user
        ? user : DecodeCredential(database.Read(CredentialFile))?.UserName;

    /// <summary>Comptes déjà utilisés sur ce poste pour ce serveur, sans divulguer les comptes du serveur.</summary>
    public IReadOnlyList<string> KnownAccounts
    {
        get
        {
            List<string> accounts;
            try
            {
                var saved = database.Read(KnownAccountsKey);
                accounts = string.IsNullOrWhiteSpace(saved)
                    ? [] : JsonSerializer.Deserialize<List<string>>(saved, Json) ?? [];
            }
            catch (JsonException) { accounts = []; }
            if (LastAccount is { Length: > 0 } last)
                accounts.Insert(0, last);
            return accounts.Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
        }
    }

    public void RememberAccount(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName)) return;
        var account = userName.Trim();
        database.Update([LastAccountKey, KnownAccountsKey], values =>
        {
            List<string> accounts;
            try { accounts = JsonSerializer.Deserialize<List<string>>(values[KnownAccountsKey] ?? "[]", Json) ?? []; }
            catch (JsonException) { accounts = []; }
            var known = new[] { account }.Concat(accounts)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
            values[LastAccountKey] = account;
            values[KnownAccountsKey] = JsonSerializer.Serialize(known, Json);
        });
    }

    /// <summary>
    /// Oublie un compte sur cet appareil, et seulement lui : plus de suggestion ni de
    /// connexion avec son ancienne preuve hors ligne. Les écritures en attente
    /// interdisent ce retrait pour éviter de perdre leur auteur et leur reprise.
    /// Le compte reste intact sur le serveur de l'établissement.
    /// </summary>
    public bool ForgetKnownAccount(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName)) return false;
        var account = userName.Trim();
        if (OutboxFor(account).Count != 0) return false;
        ForgetCredential(account);
        database.Update([LastAccountKey, KnownAccountsKey], values =>
        {
            List<string> accounts;
            try { accounts = JsonSerializer.Deserialize<List<string>>(values[KnownAccountsKey] ?? "[]", Json) ?? []; }
            catch (JsonException) { accounts = []; }
            var remaining = accounts.Where(x => !string.Equals(x, account, StringComparison.OrdinalIgnoreCase))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
            values[KnownAccountsKey] = JsonSerializer.Serialize(remaining, Json);
            if (string.Equals(values[LastAccountKey], account, StringComparison.OrdinalIgnoreCase))
                values[LastAccountKey] = remaining.FirstOrDefault() ?? string.Empty;
        });
        return true;
    }

    /// <summary>Une récupération invalide uniquement l'empreinte du compte concerné, sans effacer les autres.</summary>
    public void ForgetCredential(string? userName = null)
    {
        var account = string.IsNullOrWhiteSpace(userName) ? (User.Length > 0 ? User : LastAccount) : userName.Trim();
        if (string.IsNullOrWhiteSpace(account)) return;
        var scopedKey = CredentialKeyFor(account);
        database.Update([CredentialFile, scopedKey], values =>
        {
            values[scopedKey] = string.Empty;
            var previous = DecodeCredential(values[CredentialFile]);
            if (previous is not null && string.Equals(previous.UserName, account, StringComparison.OrdinalIgnoreCase))
                values[CredentialFile] = string.Empty;
        });
    }

    public bool HasCredential => (database.Read(CredentialFile) is { Length: > 0 }) ||
        database.ReadPrefix("session:", 1).Count > 0;

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
