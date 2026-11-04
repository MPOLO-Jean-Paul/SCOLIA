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
    public static async Task<(string? Text, string? Problem)> CaptureAsync(ShellViewModel shell, bool fromPhoto = false)
    {
        byte[]? image;
        if (fromPhoto && Platform.PickQrImageAsync is { } androidPicker)
            image = await androidPicker();
        else if (!fromPhoto && Platform.CaptureImageAsync is { } camera)
            image = await camera();
        else if (shell.PickFileHandler is { } pick)
            image = (await pick([".png", ".jpg", ".jpeg", ".bmp", ".webp"]))?.Data;
        else return (null, "Aucun appareil photo ni sélecteur d'image sur cet appareil. Collez le code de liaison.");

        if (image is null || image.Length == 0) return (null, null);
        if (image.Length > 12 * 1024 * 1024)
            return (null, "Cette photo est trop volumineuse (maximum 12 Mo). Recadrez le QR ou faites une capture d'écran.");
        string? text;
        try { text = await Task.Run(() => QrCodes.Read(image)); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        { return (null, "Cette image n'est pas lisible. Importez une photo JPG ou PNG contenant le QR SCOLIA."); }
        if (text is null)
            return (null, "Aucun QR lisible : rapprochez-vous du code affiché, évitez les reflets ou importez une capture d'écran nette.");
        if (!PairingCode.TryParse(text, out _, out var error)) return (null, error);
        return (text, null);
    }

    /// <summary>
    /// Relie cet appareil au serveur du code. Rend le message à afficher : la réussite est suivie de la page de
    /// connexion du compte, l'échec dit quelle voie vérifier.
    /// </summary>
    public static async Task<string> JoinAsync(ShellViewModel shell, string text, Action<string>? progress = null)
    {
        if (!PairingCode.TryParse(text, out var code, out var error) || code is null) return error;

        // Essayer toutes les adresses Wi-Fi en parallèle (certains équipements ont
        // plusieurs cartes réseau). Préparer Tailscale simultanément pour éviter
        // d'attendre des délais séquentiels sur des adresses obsolètes.
        progress?.Invoke("Recherche de la liaison disponible : Wi-Fi local, puis réseau privé…");
        var lanTask = CheckRoutesAsync(code.LanAddresses, code, tailnet: false);
        var tailnetTask = CheckRoutesAsync(code.TailnetAddresses, code, tailnet: true);

        var lan = await lanTask;
        if (lan is { } local)
        {
            progress?.Invoke($"Connexion sécurisée sur le Wi-Fi ({local.Address.Host})…");
            await shell.JoinAsync(new DiscoveredServer(local.Address.Host, local.School,
                Machine(code, local.Address), string.Empty, local.Address));
            return await ActivateAsync(shell, code, local.School, "Wi-Fi local");
        }

        var tailnet = await tailnetTask;
        if (tailnet is { } privateRoute)
        {
            progress?.Invoke($"Connexion par Tailscale ({privateRoute.Address.Host})…");
            await shell.JoinAsync(new DiscoveredServer(privateRoute.Address.Host, privateRoute.School,
                Machine(code, privateRoute.Address), string.Empty, privateRoute.Address));
            return await ActivateAsync(shell, code, privateRoute.School, "Tailscale privé");
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

    private sealed record AvailableRoute(Uri Address, string School);

    private static async Task<AvailableRoute?> CheckRoutesAsync(
        IEnumerable<Uri> addresses, PairingCode code, bool tailnet)
    {
        var candidates = addresses.Take(6).ToArray();
        if (candidates.Length == 0) return null;
        if (tailnet && Platform.ConnectTailscaleInBackground is not null)
        {
            try { await TailscaleCompanion.ConnectInBackgroundAsync().WaitAsync(TimeSpan.FromSeconds(7)); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or
                HttpRequestException or TaskCanceledException or TimeoutException) { /* Wi-Fi et Internet restent disponibles. */ }
        }
        var probes = candidates.Select(async (address, priority) =>
        {
            if (tailnet && !await TailscaleEndpoint.ResolvesPrivatelyAsync(address)) return (priority, route: (AvailableRoute?)null);
            var school = await RespondsAsync(address);
            // Ne jamais jumeler un autre établissement trouvé sur la même plage réseau.
            if (school is null || (code.School != "Établissement" &&
                !string.Equals(school.Trim(), code.School.Trim(), StringComparison.OrdinalIgnoreCase)))
                return (priority, route: (AvailableRoute?)null);
            return (priority, route: new AvailableRoute(address, school));
        });
        var results = await Task.WhenAll(probes);
        return results.OrderBy(x => x.priority).Select(x => x.route).FirstOrDefault(x => x is not null);
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
            var status = await probe.GetAsync<Api.SetupStatusDto>("api/installation").WaitAsync(ProbeDelay);
            return status.SchoolName ?? "Établissement";
        }
        catch (Exception ex) when (ex is HttpRequestException or Api.ApiException or TaskCanceledException or IOException or TimeoutException) { return null; }
    }

    private static async Task<bool> WithinAsync(Task<bool> check)
    {
        var done = await Task.WhenAny(check, Task.Delay(ProbeDelay));
        return done == check && await check;
    }

    private static string Machine(PairingCode code, Uri address) => code.Machine.Length > 0 ? code.Machine : address.Host;
}
