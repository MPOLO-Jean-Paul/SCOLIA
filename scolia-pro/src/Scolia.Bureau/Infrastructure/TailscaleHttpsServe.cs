using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Un clic sur l'ordinateur principal : Tailscale Serve publie SCOLIA en HTTPS
/// uniquement dans le tailnet. La terminaison TLS et le renouvellement de son
/// certificat sont assures par Tailscale, sans PFX ni cle ACME dans SCOLIA.
/// Le port 443 d'une autre application n'est jamais ecrase.
/// </summary>
public static class TailscaleHttpsServe
{
    public enum PortState { Available, AlreadyScolia, Occupied, Unknown }
    public sealed record Result(bool Ready, Uri? Address, string Message);

    /// <summary>DNS reel de notre appareil Tailscale, jamais fourni par une adresse saisie.</summary>
    public static string? ParseOwnDnsName(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("BackendState", out var state)
                || state.ValueKind != JsonValueKind.String || state.GetString() != "Running"
                || !root.TryGetProperty("Self", out var self) || self.ValueKind != JsonValueKind.Object
                || !self.TryGetProperty("DNSName", out var field) || field.ValueKind != JsonValueKind.String)
                return null;
            var dns = (field.GetString() ?? string.Empty).TrimEnd('.').ToLowerInvariant();
            if (dns.Length > 253 || !dns.EndsWith(".ts.net", StringComparison.Ordinal)
                || !TailscaleEndpoint.IsTailnetHost(dns)) return null;
            // Chaque etiquette DNS possede au moins une lettre ou un chiffre aux extremites.
            var labels = dns.Split('.');
            return labels.Length >= 4 && labels.All(label => label.Length is > 0 and <= 63
                && char.IsAsciiLetterOrDigit(label[0]) && char.IsAsciiLetterOrDigit(label[^1])
                && label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) ? dns : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Inspection pure/testable : n'ecraser aucune configuration Serve preexistante.</summary>
    public static PortState Inspect(string json, string dns, int port)
    {
        if (port is < 1 or > 65535 || string.IsNullOrWhiteSpace(dns)) return PortState.Unknown;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return PortState.Unknown;
            // Une configuration de service est distincte de la configuration du noeud :
            // ne pas risquer de modifier le port 443 d'un autre service.
            if (root.TryGetProperty("Services", out var services)
                && services.ValueKind == JsonValueKind.Object && services.EnumerateObject().Any())
                return PortState.Occupied;

            var tcp = root.TryGetProperty("TCP", out var tcpField) && tcpField.ValueKind == JsonValueKind.Object
                ? tcpField : default;
            var web = root.TryGetProperty("Web", out var webField) && webField.ValueKind == JsonValueKind.Object
                ? webField : default;
            if ((root.TryGetProperty("TCP", out var t) && t.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
                || (root.TryGetProperty("Web", out var w) && w.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null)))
                return PortState.Unknown;

            var has443 = tcp.ValueKind == JsonValueKind.Object && tcp.TryGetProperty("443", out var handler);
            var hasWeb443 = web.ValueKind == JsonValueKind.Object &&
                web.EnumerateObject().Any(x => x.Name.EndsWith(":443", StringComparison.Ordinal));
            var correctTcp = has443 && handler.ValueKind == JsonValueKind.Object
                && handler.TryGetProperty("HTTPS", out var https) && https.ValueKind == JsonValueKind.True;
            var key = dns + ":443";
            var correctWeb = web.ValueKind == JsonValueKind.Object && web.TryGetProperty(key, out var endpoint)
                && endpoint.ValueKind == JsonValueKind.Object
                && endpoint.TryGetProperty("Handlers", out var handlers) && handlers.ValueKind == JsonValueKind.Object
                && handlers.TryGetProperty("/", out var rootRoute) && rootRoute.ValueKind == JsonValueKind.Object
                && rootRoute.TryGetProperty("Proxy", out var proxy) && proxy.ValueKind == JsonValueKind.String
                && string.Equals(proxy.GetString(), $"http://127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase);
            if (correctTcp && correctWeb) return PortState.AlreadyScolia;
            return has443 || hasWeb443 ? PortState.Occupied : PortState.Available;
        }
        catch (JsonException) { return PortState.Unknown; }
    }

    /// <summary>
    /// La seule action qui modifie Tailscale. Necessite une action utilisateur,
    /// une session Tailscale valide et les droits de configuration SCOLIA.
    /// </summary>
    public static async Task<Result> EnableAsync(int port, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return new(false, null, "L'activation HTTPS automatique est disponible sur l'ordinateur principal Windows.");
        if (port is < 1 or > 65535)
            return new(false, null, "Port du serveur SCOLIA incorrect.");

        var cli = TailscaleCompanion.FindWindowsExecutable("tailscale.exe");
        if (cli is null)
            return new(false, null, "Tailscale doit d'abord etre installe et connecte sur l'ordinateur principal.");
        try
        {
            var status = await RunAsync(cli, ["status", "--json"], TimeSpan.FromSeconds(8), ct);
            var dns = status.ExitCode == 0 ? ParseOwnDnsName(status.Output) : null;
            if (dns is null)
                return new(false, null, "Connectez d'abord cet ordinateur au reseau prive Tailscale (MagicDNS actif).");

            var before = await RunAsync(cli, ["serve", "status", "--json"], TimeSpan.FromSeconds(8), ct);
            if (before.ExitCode != 0)
                return new(false, null, "Impossible de verifier les partages Tailscale existants : aucune modification effectuee.");
            var state = Inspect(before.Output, dns, port);
            if (state == PortState.AlreadyScolia)
                return new(true, new Uri($"https://{dns}/"), "HTTPS prive deja actif. Le certificat est gere par Tailscale.");
            if (state != PortState.Available)
                return new(false, null, "Le port HTTPS 443 est deja utilise ou son etat est incertain : SCOLIA n'a rien remplace.");

            var change = await RunAsync(cli, ["serve", "--bg", "--https=443", $"http://127.0.0.1:{port}"],
                TimeSpan.FromSeconds(25), ct);
            if (change.ExitCode != 0)
                return new(false, null, "Tailscale n'a pas autorise HTTPS. Ouvrez l'assistant Tailscale et validez les autorisations HTTPS/MagicDNS, puis reessayez.");

            var after = await RunAsync(cli, ["serve", "status", "--json"], TimeSpan.FromSeconds(8), ct);
            if (after.ExitCode != 0 || Inspect(after.Output, dns, port) != PortState.AlreadyScolia)
                return new(false, null, "Tailscale a repondu, mais le partage HTTPS n'est pas confirme. Verifiez ses autorisations.");
            return new(true, new Uri($"https://{dns}/"), "HTTPS prive actif : certificat et renouvellement geres automatiquement par Tailscale.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, null, "Tailscale n'a pas repondu. Une autorisation dans son navigateur peut etre necessaire.");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException
            or InvalidOperationException or JsonException)
        {
            return new(false, null, "Tailscale n'a pas pu configurer HTTPS. Verifiez son installation et les autorisations du reseau prive.");
        }
    }

    private sealed record CliReply(int ExitCode, string Output);

    private static async Task<CliReply> RunAsync(string file, string[] args, TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Processus Tailscale absent.");
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var errors = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            // La sortie de stderr n'est volontairement jamais remontee dans l'interface.
            await errors.ConfigureAwait(false);
            return new CliReply(process.ExitCode, await output.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            throw;
        }
    }
}
