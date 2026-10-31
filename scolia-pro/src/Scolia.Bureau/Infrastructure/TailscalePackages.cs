using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Paquet Tailscale officiel trouvé sur le dépôt stable : son nom, son adresse et sa version.</summary>
public sealed record TailscalePackage(string FileName, string Url, Version Version);

/// <summary>
/// Téléchargement de Tailscale depuis son dépôt officiel (pkgs.tailscale.com, piste stable), sans quitter SCOLIA :
/// l'application Android complète (APK universel) ou l'installateur Windows. Le fichier est vérifié par son
/// empreinte SHA-256 publiée à côté de lui, puis remis à l'installateur du système.
/// </summary>
public static partial class TailscalePackages
{
    public const string StableIndex = "https://pkgs.tailscale.com/stable/";

    [GeneratedRegex(@"tailscale-android-universal-(?<v>\d+(?:\.\d+){1,3})\.apk", RegexOptions.CultureInvariant)]
    private static partial Regex AndroidApk();

    [GeneratedRegex(@"tailscale-setup-(?<v>\d+(?:\.\d+){1,3})\.exe", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsSetup();

    [GeneratedRegex(@"\b[0-9a-fA-F]{64}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Hex();

    /// <summary>Plus récente version du paquet de cette plateforme dans la page du dépôt, ou rien.</summary>
    public static TailscalePackage? Latest(string indexHtml, bool android)
    {
        var pattern = android ? AndroidApk() : WindowsSetup();
        return pattern.Matches(indexHtml)
            .Select(m => (Name: m.Value, Ok: Version.TryParse(m.Groups["v"].Value, out var v), Version: v))
            .Where(x => x.Ok)
            .OrderByDescending(x => x.Version)
            .Select(x => new TailscalePackage(x.Name, StableIndex + x.Name, x.Version!))
            .FirstOrDefault();
    }

    /// <summary>Empreinte lue dans un fichier .sha256 (« empreinte  nom » ou empreinte seule).</summary>
    public static string? ParseSha256(string? text) =>
        text is null ? null : Sha256Hex().Match(text) is { Success: true } m ? m.Value.ToLowerInvariant() : null;

    public static async Task<TailscalePackage> FindAsync(HttpClient http, bool android, CancellationToken ct)
    {
        var html = await http.GetStringAsync(StableIndex, ct).ConfigureAwait(false);
        return Latest(html, android)
            ?? throw new InvalidOperationException("Le dépôt officiel de Tailscale ne propose pas de paquet pour cet appareil.");
    }

    /// <summary>Empreinte officielle publiée à côté du paquet ; rien si le dépôt ne la donne pas.</summary>
    public static async Task<string?> OfficialSha256Async(HttpClient http, TailscalePackage package, CancellationToken ct)
    {
        try { return ParseSha256(await http.GetStringAsync(package.Url + ".sha256", ct).ConfigureAwait(false)); }
        catch (HttpRequestException) { return null; }
    }

    /// <summary>Télécharge le paquet dans le dossier donné en signalant octets reçus et taille totale.</summary>
    public static async Task<string> DownloadAsync(HttpClient http, TailscalePackage package, string folder,
        IProgress<(long Received, long? Total)> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, package.FileName);
        var partial = target + ".partiel";
        using var response = await http.GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var file = File.Create(partial))
        {
            var buffer = new byte[81920];
            long received = 0;
            int read;
            progress.Report((0, total));
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                received += read;
                progress.Report((received, total));
            }
        }
        File.Move(partial, target, overwrite: true);
        return target;
    }

    public static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct).ConfigureAwait(false));
    }

    /// <summary>Taille lisible : « 42,3 Mo ».</summary>
    public static string Size(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.0} Mo" : $"{bytes / 1024d:0} Ko";
}
