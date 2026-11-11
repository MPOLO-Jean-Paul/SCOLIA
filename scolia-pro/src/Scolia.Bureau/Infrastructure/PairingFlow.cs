using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Liaison par code QR, côté appareil qui rejoint : lire le code (appareil photo du téléphone, image ou texte collé
/// sur l'ordinateur), puis essayer les voies dans l'ordre le plus rapide : le Wi-Fi, Tailscale, puis Internet.
/// </summary>
public static class PairingFlow
{
    private static readonly TimeSpan ProbeDelay = TimeSpan.FromSeconds(4);

    /// <summary>Vrai sur un appareil qui a un appareil photo pour scanner ; sinon, une image du code est demandée.</summary>
    public static bool HasCamera => Platform.CaptureImageAsync is not null;

    /// <summary>
    /// Texte du code lu dans une photo ou dans une image choisie. Null si l'utilisateur annule ;
    /// une explication dans <paramref name="problem"/> si l'image ne montre pas de code lisible.
    /// </summary>
    public static async Task<(string? Text, string? Problem)> CaptureAsync(ShellViewModel shell)
    {
        byte[]? image;
        if (Platform.CaptureImageAsync is { } camera) image = await camera();
        else if (shell.PickFileHandler is { } pick) image = (await pick([".png", ".jpg", ".jpeg", ".bmp", ".webp"]))?.Data;
        else return (null, "Aucun appareil photo ni sélecteur d'image sur cet appareil. Collez le code de liaison.");
        if (image is null || image.Length == 0) return (null, null);
        var text = await Task.Run(() => QrCodes.Read(image));
        return text is null
            ? (null, "Aucun code QR lisible sur l'image. Approchez l'appareil de l'écran du serveur, bien en face, puis réessayez.")
            : (text, null);
    }

    /// <summary>
    /// Relie cet appareil au serveur du code. Rend le message à afficher : la réussite est suivie de la page de
    /// connexion du compte, l'échec dit quelle voie vérifier.
    /// </summary>
    public static async Task<string> JoinAsync(ShellViewModel shell, string text, Action<string>? progress = null)
    {
        if (!PairingCode.TryParse(text, out var code, out var error) || code is null) return error;

        foreach (var address in code.LanAddresses)
        {
            progress?.Invoke($"Connexion à {code.School} sur le Wi-Fi ({address.Host})…");
            if (await RespondsAsync(address) is { } school)
            {
                await shell.JoinAsync(new DiscoveredServer(address.Host, school, Machine(code, address), string.Empty, address));
                return $"Relié à {school} sur le Wi-Fi. Connectez-vous avec votre compte.";
            }
        }

        if (code.TailnetAddresses.Any())
        {
            progress?.Invoke("Liaison privée Tailscale…");
            // Tailscale installé sur le téléphone : il se reconnecte discrètement avant l'essai.
            if (Platform.ConnectTailscaleInBackground is not null)
                try { await TailscaleCompanion.ConnectInBackgroundAsync(); }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or TaskCanceledException) { }
            foreach (var address in code.TailnetAddresses)
            {
                if (!await TailscaleEndpoint.ResolvesPrivatelyAsync(address)) continue;
                if (await RespondsAsync(address) is { } school)
                {
                    await shell.JoinAsync(new DiscoveredServer(address.Host, school, Machine(code, address), string.Empty, address));
                    return $"Relié à {school} par Tailscale. Connectez-vous avec votre compte.";
                }
            }
        }

        if (code.HasInvitation)
        {
            progress?.Invoke("Liaison Internet sécurisée…");
            try
            {
                var link = Scolia.Transport.RemoteLink.Parse(code.Invitation!);
                using var probe = new Api.ScoliaClient(link.VirtualServer);
                probe.UseRemote(link);
                if (await WithinAsync(probe.IsRunningAsync()))
                {
                    await shell.JoinInternetAsync(link);
                    return $"Relié à {code.School} par Internet. Connectez-vous avec votre compte.";
                }
            }
            catch (FormatException ex) { return ex.Message; }
            catch (Exception ex) when (ex is HttpRequestException or Api.ApiException or TaskCanceledException or IOException) { }
        }

        return code.TailnetAddresses.Any() || code.HasInvitation
            ? $"{code.School} ne répond pas. Vérifiez que le serveur est allumé, que les deux appareils sont sur le même Wi-Fi, ou que Tailscale est connecté sur les deux."
            : $"{code.School} ne répond pas sur ce Wi-Fi. Vérifiez que les deux appareils sont sur le même réseau et que le serveur est allumé.";
    }

    /// <summary>Nom de l'établissement si SCOLIA répond à cette adresse dans le délai, sinon null.</summary>
    private static async Task<string?> RespondsAsync(Uri address)
    {
        try
        {
            using var probe = new Api.ScoliaClient(address);
            if (!await WithinAsync(probe.IsRunningAsync())) return null;
            var status = await probe.GetAsync<Api.SetupStatusDto>("api/installation");
            return status.SchoolName ?? "Établissement";
        }
        catch (Exception ex) when (ex is HttpRequestException or Api.ApiException or TaskCanceledException or IOException) { return null; }
    }

    private static async Task<bool> WithinAsync(Task<bool> check)
    {
        var done = await Task.WhenAny(check, Task.Delay(ProbeDelay));
        return done == check && await check;
    }

    private static string Machine(PairingCode code, Uri address) => code.Machine.Length > 0 ? code.Machine : address.Host;
}
