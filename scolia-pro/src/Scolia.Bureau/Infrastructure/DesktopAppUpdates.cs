using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Mises à jour du client natif Windows depuis la dernière GitHub Release officielle.
/// Détection automatique (fenêtre ouverte), téléchargement consenti et contrôlé,
/// empreinte SHA-256 vérifiée, lancement du véritable assistant Inno Setup.
/// Aucune installation silencieuse, ni suppression des bases scolaires.
/// </summary>
public static class DesktopAppUpdates
{
    private const string LatestRelease = "https://api.github.com/repos/MPOLO-Jean-Paul/SCOLIA/releases/latest";
    private const string TrustedReleasePath = "/MPOLO-Jean-Paul/SCOLIA/releases/download/";
    private const long MaximumSetupBytes = 400L * 1024 * 1024;
    private static readonly TimeSpan CheckDelay = TimeSpan.FromHours(6);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(35) };
    private static DateTimeOffset lastAttempt;
    private static string? versionPrompted;
    private static int running;

    internal sealed record AvailableUpdate(Version Version, Uri Uri, string Sha256, long Size);

    public static void Attach(Window owner)
    {
        if (!OperatingSystem.IsWindows()) return;
        var timer = new DispatcherTimer { Interval = CheckDelay };
        timer.Tick += (_, _) => _ = CheckAsync(owner);
        owner.Opened += (_, _) =>
        {
            timer.Start();
            _ = CheckAsync(owner);
        };
        owner.Closed += (_, _) => timer.Stop();
    }

    /// <summary>Peut aussi être appelé à la demande ; toutes les erreurs restent non bloquantes.</summary>
    public static async Task CheckAsync(Window owner, bool manual = false)
    {
        if (!OperatingSystem.IsWindows() || !owner.IsVisible || Interlocked.Exchange(ref running, 1) != 0) return;
        try
        {
            if (!manual && DateTimeOffset.UtcNow - lastAttempt < CheckDelay) return;
            lastAttempt = DateTimeOffset.UtcNow;
            var update = await GetLatestAsync();
            if (update is null)
            {
                if (manual) await MessageAsync(owner, "SCOLIA Pro", "Vous utilisez déjà la dernière version officielle.");
                return;
            }
            if (!manual && versionPrompted == update.Version.ToString()) return;
            versionPrompted = update.Version.ToString();
            bool agreed = await AskAsync(owner,
                $"Mise à jour SCOLIA Pro {update.Version}",
                $"Une nouvelle version de SCOLIA Pro est disponible.\nTaille : {update.Size / (1024.0 * 1024.0):F1} Mo.\n\n"
                + "La mise à jour se télécharge dans SCOLIA, puis l'assistant Windows s'ouvre après votre confirmation. "
                + "Les bases SQLite et les paramètres sont conservés par l'installateur.",
                "Télécharger");
            if (!agreed) return;

            var setup = await DownloadVerifiedAsync(owner, update);
            if (setup is null) return;
            // Le service Windows maintient les autres postes connectés : jamais interrompre
            // le serveur actif en plein travail de secrétariat ou pendant un encaissement.
            if (PosteAdmin.ServiceInstalled())
            {
                await MessageAsync(owner, "Serveur SCOLIA en service",
                    "Le service Windows SCOLIA est installé sur cet ordinateur. "
                    + "Avant de remplacer ses exécutables, sauvegardez les données, informez les autres postes "
                    + "et retirez temporairement le service depuis « Poste et réseau ».\n\n"
                    + "L'installateur vérifié a été conservé dans votre dossier de mises à jour. "
                    + "Relancez SCOLIA après la préparation de la maintenance.");
                return;
            }

            if (!await AskAsync(owner, "Installation prête",
                "Le fichier d'installation a été téléchargé et son empreinte SHA-256 vérifiée.\n\n"
                + "En cliquant sur « Installer », SCOLIA fermera sa fenêtre puis ouvrira "
                + "l'assistant Windows. Ne mettez pas à jour pendant des écritures scolaires "
                + "ou lorsqu'un autre poste dépend du serveur local.",
                "Installer")) return;
            using var started = Process.Start(new ProcessStartInfo(setup) { UseShellExecute = true })
                ?? throw new Win32Exception("Windows n'a pas ouvert l'assistant d'installation.");
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException
            or JsonException or TaskCanceledException or Win32Exception or InvalidOperationException)
        {
            Trace.WriteLine($"SCOLIA : contrôle de mise à jour indisponible : {ex}");
            if (manual)
                await MessageAsync(owner, "Mise à jour indisponible",
                    "Impossible de vérifier les mises à jour. Vérifiez votre connexion Internet, "
                    + "la disponibilité de GitHub, puis réessayez.");
        }
        finally { Interlocked.Exchange(ref running, 0); }
    }

    private static async Task<AvailableUpdate?> GetLatestAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestRelease);
        request.Headers.UserAgent.ParseAdd("SCOLIA-Pro-Windows-Updater/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        var root = json.RootElement;
        string tag = root.TryGetProperty("tag_name", out var tagValue) ? tagValue.GetString() ?? "" : "";
        var match = Regex.Match(tag, @"(?<!\d)\d+\.\d+\.\d+(?!\d)");
        if (!match.Success || !Version.TryParse(match.Value, out var version)) return null;

        var installed = typeof(DesktopAppUpdates).Assembly.GetName().Version ?? new Version(0, 0);
        if (version <= installed) return null;
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

        var expectedName = $"SCOLIA_Pro_Installation_{version}_Windows_x64.exe";
        foreach (var asset in assets.EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), expectedName, StringComparison.Ordinal)) continue;
            long size = asset.GetProperty("size").GetInt64();
            if (size is < 1_048_576 or > MaximumSetupBytes) continue;
            var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
            if (!Regex.IsMatch(digest, @"^sha256:[A-Fa-f0-9]{64}$")) continue;
            var url = asset.GetProperty("browser_download_url").GetString();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                || !uri.AbsolutePath.StartsWith(TrustedReleasePath, StringComparison.Ordinal)
                || !uri.AbsolutePath.EndsWith("/" + expectedName, StringComparison.Ordinal)) continue;
            return new AvailableUpdate(version, uri, digest[7..].ToUpperInvariant(), size);
        }
        return null;
    }

    private static async Task<string?> DownloadVerifiedAsync(Window owner, AvailableUpdate update)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JP-Services", "SCOLIA Pro", "MisesAJour");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, $"SCOLIA_Pro_Installation_{update.Version}_Windows_x64.exe");
        if (File.Exists(target) && await VerifyAsync(target, update)) return target;

        // Fichier temporaire : même si le téléphone partage sa connexion ou Internet s'interrompt,
        // un téléchargement partiel n'est jamais donné à Windows comme installateur.
        var partial = target + ".part";
        using var cancellation = new CancellationTokenSource();
        var dialog = new Window
        {
            Title = "Téléchargement SCOLIA Pro",
            Width = 490,
            Height = 205,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var status = new TextBlock
        {
            Text = "Connexion sécurisée à GitHub…",
            TextWrapping = TextWrapping.Wrap
        };
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Height = 12 };
        var cancel = new Button { Content = "Annuler", HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_, _) => cancellation.Cancel();
        var stack = new StackPanel { Spacing = 13, Margin = new Thickness(22) };
        stack.Children.Add(new TextBlock { Text = $"SCOLIA Pro {update.Version}", FontWeight = FontWeight.SemiBold });
        stack.Children.Add(status);
        stack.Children.Add(progress);
        stack.Children.Add(cancel);
        dialog.Content = stack;
        dialog.Closed += (_, _) => cancellation.Cancel();
        dialog.Show(owner);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, update.Uri);
            request.Headers.UserAgent.ParseAdd("SCOLIA-Pro-Windows-Updater/1.0");
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long size && size > MaximumSetupBytes)
                throw new InvalidOperationException("Le programme de mise à jour est trop volumineux.");
            await using (var input = await response.Content.ReadAsStreamAsync(cancellation.Token))
            await using (var output = File.Create(partial))
            {
                var buffer = new byte[128 * 1024];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellation.Token)) > 0)
                {
                    total += read;
                    if (total > MaximumSetupBytes) throw new InvalidOperationException("Taille de fichier invalide.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellation.Token);
                    progress.Value = Math.Clamp(100.0 * total / update.Size, 0.0, 100.0);
                    status.Text = $"{total / (1024.0 * 1024.0):F1} / {update.Size / (1024.0 * 1024.0):F1} Mo";
                }
            }
            status.Text = "Vérification de l'intégrité du programme…";
            if (!await VerifyAsync(partial, update))
                throw new InvalidOperationException("La vérification SHA-256 a échoué. Le fichier sera supprimé.");
            File.Move(partial, target, overwrite: true);
            return target;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException
            or InvalidOperationException or TaskCanceledException)
        {
            await MessageAsync(owner, "Téléchargement interrompu",
                $"Le programme n'a pas été installé : {ex.Message}\n\nVous pourrez relancer la mise à jour plus tard.");
            return null;
        }
        finally
        {
            if (File.Exists(partial)) { try { File.Delete(partial); } catch (IOException) { } }
            if (dialog.IsVisible) dialog.Close();
        }
    }

    private static async Task<bool> VerifyAsync(string path, AvailableUpdate update)
    {
        try
        {
            await using var input = File.OpenRead(path);
            if (input.Length != update.Size) return false;
            var magic = new byte[2];
            if (await input.ReadAsync(magic) != 2 || magic[0] != 0x4D || magic[1] != 0x5A) return false;
            input.Position = 0;
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(input));
            return hash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static async Task<bool> AskAsync(Window owner, string title, string message, string accept)
    {
        var dialog = BuildDialog(title, message, accept);
        return await dialog.ShowDialog<bool>(owner);
    }

    private static async Task MessageAsync(Window owner, string title, string message) =>
        await BuildDialog(title, message, "Fermer", infoOnly: true).ShowDialog<bool>(owner);

    private static Window BuildDialog(string title, string message, string accept, bool infoOnly = false)
    {
        var window = new Window
        {
            Title = title,
            Width = 530,
            SizeToContent = SizeToContent.Height,
            MinHeight = 190,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var content = new StackPanel { Spacing = 16, Margin = new Thickness(24) };
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap
        });
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        if (!infoOnly)
        {
            var later = new Button { Content = "Plus tard", MinWidth = 105 };
            later.Click += (_, _) => window.Close(false);
            actions.Children.Add(later);
        }
        var confirm = new Button { Content = accept, MinWidth = 110 };
        confirm.Click += (_, _) => window.Close(true);
        actions.Children.Add(confirm);
        content.Children.Add(actions);
        window.Content = content;
        return window;
    }
}
