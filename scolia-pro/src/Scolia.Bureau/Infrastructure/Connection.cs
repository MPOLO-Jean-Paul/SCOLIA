using System.Text.Json;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Connexion de ce poste : il est lui-même le serveur de l'établissement (« local »), ou il est relié au serveur
/// d'un autre ordinateur (« distant »). L'établissement est mémorisé : si l'adresse du serveur change, il est retrouvé.
/// </summary>
public sealed record ConnectionSettings(string Mode, string? Url = null, string? SchoolId = null, string? School = null, string? Machine = null,
    bool LanPromptDone = false)
{
    public const string Local = "local";
    public const string Remote = "distant";
    public const string Internet = "internet";

    public bool IsRemote => Mode == Remote && Url is not null;
}

/// <summary>Enregistre la connexion du poste dans le dossier de l'utilisateur.</summary>
public static class ConnectionStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCOLIA", "connexion.json");

    public static ConnectionSettings? Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<ConnectionSettings>(File.ReadAllText(FilePath)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public static void Save(ConnectionSettings settings) => _ = TrySave(settings);

    /// <summary>
    /// Écriture atomique du serveur mémorisé : éviter qu'une coupure pendant l'écriture
    /// tronque le fichier et fasse perdre la connexion de l'établissement au redémarrage.
    /// La connexion Tailscale avertit l'utilisateur si la sauvegarde n'a pas abouti.
    /// </summary>
    public static bool TrySave(ConnectionSettings settings)
    {
        string? temporary = null;
        try
        {
            var folder = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(folder);
            temporary = Path.Combine(folder, $"connexion-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
            File.Move(temporary, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            if (temporary is not null)
                try { File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Vrai si ce poste porte déjà la base d'un établissement : il en est le serveur.</summary>
    public static bool HasLocalData() => File.Exists(Path.Combine(PosteAdmin.DataFolder(), "scolia.db"));

    /// <summary>Adresse du serveur de ce poste.</summary>
    public static Uri LocalAddress => new($"http://localhost:{PosteAdmin.Port()}/");
}
