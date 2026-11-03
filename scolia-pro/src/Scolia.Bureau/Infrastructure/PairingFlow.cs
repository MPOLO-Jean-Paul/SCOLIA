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
                return await ActivateAsync(shell, code, school, "Wi-Fi");
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
                    return await ActivateAsync(shell, code, school, "Tailscale");
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
                    return await ActivateAsync(shell, code, code.School, "Internet sécurisé");
                }
            }
            catch (FormatException ex) { return ex.Message; }
            catch (Exception ex) when (ex is HttpRequestException or Api.ApiException or TaskCanceledException or IOException) { }
        }

        return code.TailnetAddresses.Any() || code.HasInvitation
            ? $"{code.School} ne répond pas. Vérifiez que le serveur est allumé, que les deux appareils sont sur le même Wi-Fi, ou que Tailscale est connecté sur les deux."
            : $"{code.School} ne répond pas sur ce Wi-Fi. Vérifiez que les deux appareils sont sur le même réseau et que le serveur est allumé.";
    }

    /// <summary>La connexion réseau n'accorde aucun droit : seule l'invitation émise par le compte du serveur ouvre la session.</summary>
    private static async Task<string> ActivateAsync(ShellViewModel shell, PairingCode code, string school, string network)
    {
        if (string.IsNullOrWhiteSpace(code.AccessTicket))
            return $"Relié à {school} par {network}, mais ce code ne contient aucune autorisation. Faites générer un nouveau QR depuis une session ouverte sur le serveur.";
        try
        {
            var message = await shell.AcceptDeviceInvitationAsync(code.AccessTicket);
            shell.Notify(message);
            return message;
        }
        catch (Api.ApiException ex) when (ex.Status is 401 or 403 or 429)
        {
            return "La liaison réseau fonctionne, mais l'invitation est expirée, déjà utilisée ou non autorisée. Demandez au serveur un nouveau QR.";
        }
        catch (Exception ex) when (ex is Api.OfflineException or HttpRequestException or TaskCanceledException or IOException)
        {
            return "La liaison a été enregistrée, mais le serveur n'a pas validé l'invitation. Vérifiez le réseau privé et réessayez avec un nouveau QR.";
        }
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
