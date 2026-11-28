using System.Text.Json;

namespace JPServices.Owner;

/// <summary>Chiffrement propre à la plateforme : DPAPI sous Windows, Android Keystore sur téléphone.</summary>
public interface ISecretProtector
{
    /// <summary>Faux si aucune protection matérielle ou de compte n'est disponible : rien n'est alors enregistré.</summary>
    bool Available { get; }
    byte[] Protect(byte[] clear);
    byte[] Unprotect(byte[] sealedData);
}

/// <summary>
/// Coffre des identifiants de la console : un seul fichier, chiffré par la plateforme, dans le dossier
/// privé de l'application. Les secrets ne sont jamais écrits en clair ni journalisés.
/// </summary>
public sealed class OwnerVault(ISecretProtector protector, string directory)
{
    private const string FileName = "console-acces.bin";

    /// <summary>Coffre de la plateforme, fourni au démarrage par Windows ou Android.</summary>
    public static OwnerVault? Current { get; set; }

    public bool CanStore => protector.Available;
    private string FilePath => Path.Combine(directory, FileName);

    public OwnerCredentials? Load()
    {
        try
        {
            if (!protector.Available || !File.Exists(FilePath)) return null;
            var clear = protector.Unprotect(File.ReadAllBytes(FilePath));
            try { return JsonSerializer.Deserialize<OwnerCredentials>(clear); }
            finally { Array.Clear(clear); }
        }
        catch (Exception)
        {
            // Fichier d'un autre compte Windows, clé Android effacée ou fichier abîmé : on repart de zéro.
            return null;
        }
    }

    public void Save(OwnerCredentials credentials)
    {
        if (!protector.Available)
            throw new OwnerApiException("Cet appareil ne permet pas de conserver des secrets chiffrés.");
        Directory.CreateDirectory(directory);
        var clear = JsonSerializer.SerializeToUtf8Bytes(credentials);
        try
        {
            var temp = FilePath + ".tmp";
            File.WriteAllBytes(temp, protector.Protect(clear));
            File.Move(temp, FilePath, overwrite: true);
        }
        finally { Array.Clear(clear); }
    }

    public void Clear()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }
}
