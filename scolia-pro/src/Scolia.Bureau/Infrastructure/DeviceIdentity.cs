using System.Text.Json;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Identité de cet appareil auprès de l'ordinateur principal : un identifiant tiré une fois et gardé, et un nom
/// lisible (le modèle du téléphone, le nom du PC). Les autres postes voient ainsi qui travaille, et d'où.
/// </summary>
public static class DeviceIdentity
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCOLIA", "poste.json");

    private sealed record Stored(string Id);

    private static readonly Lazy<string> id = new(Load);

    public static string Id => id.Value;

    /// <summary>Nom lisible : « Téléphone Samsung SM-A155F », ou le nom de l'ordinateur.</summary>
    public static string Name => Platform.DeviceName is { Length: > 0 } name ? name : Environment.MachineName;

    private static string Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath)) is { Id.Length: > 0 } stored) return stored.Id;
            var fresh = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Stored(fresh)));
            return fresh;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}
