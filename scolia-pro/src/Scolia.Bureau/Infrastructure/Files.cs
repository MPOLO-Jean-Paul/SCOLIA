using System.Diagnostics;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Fichier choisi par l'utilisateur, lu en mémoire.</summary>
public sealed record PickedFile(string Name, byte[] Data);

/// <summary>Enregistre les documents dans le dossier Téléchargements et les ouvre avec l'application du poste.</summary>
public static class Files
{
    public static string DownloadsFolder
    {
        get
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(folder)) folder = Path.GetTempPath();
            return folder;
        }
    }

    /// <summary>Écrit le fichier sans écraser un document existant, puis renvoie son chemin.</summary>
    public static string Save(byte[] data, string fileName, string? folder = null)
    {
        var directory = folder ?? DownloadsFolder;
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var path = Path.Combine(directory, fileName);
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(directory, $"{name} ({i}){extension}");
        File.WriteAllBytes(path, data);
        return path;
    }

    /// <summary>Ouvre un fichier ou une adresse avec l'application associée du poste.</summary>
    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
}
