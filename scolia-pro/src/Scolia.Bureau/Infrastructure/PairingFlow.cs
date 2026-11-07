using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Liaison par code QR, côté appareil qui rejoint : lire le code (appareil photo du téléphone, image ou texte collé
/// sur l'ordinateur), puis essayer le réseau local et, si disponible, le relais Render.
/// </summary>
public static class PairingFlow
{
    private static readonly TimeSpan ProbeDelay = TimeSpan.FromSeconds(4);

    /// <summary>Vrai sur un appareil qui a un appareil photo pour scanner ; sinon, une image du code est demandée.</summary>
    public static bool HasCamera => Platform.ScanQrAsync is not null || Platform.CaptureImageAsync is not null;

    /// <summary>
    /// Texte du code lu dans une photo ou dans une image choisie. Null si l'utilisateur annule ;
    /// une explication dans <paramref name="problem"/> si l'image ne montre pas de code lisible.
    /// </summary>
    public static async Task<(string? Text, string? Problem)> CaptureAsync(ShellViewModel shell, bool fromPhoto = false)
    {
        // En priorité : viseur Android en direct, sans photographier manuellement le QR.
        // La fonction native gère un recours local à l'appareil photo si le module manque.
        if (!fromPhoto && Platform.ScanQrAsync is { } liveScan)
        {
            var detected = await liveScan();
            if (string.IsNullOrWhiteSpace(detected)) return (null, null);
            return PairingCode.TryParse(detected, out _, out var problem)
                ? (detected, null) : (null, problem);
        }

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

        // Ne jamais transformer un ancien QR HTTPS privé en liaison HTTP locale.
        // Ces QR historiques peuvent être rejumelés par Render, sinon le
        // responsable génère un nouveau code dans la version actuelle.
        if (code.TailnetAddresses.Any(address => address.Scheme == Uri.UriSchemeHttps))
        {
            var secureInternet = await TryInternetAsync(shell, code, progress);
            return secureInternet ?? "Ce code QR utilise une ancienne connexion privée. Générez un nouveau code QR sur le serveur, avec l'accès par Internet actif.";
        }

        progress?.Invoke("Recherche sur le réseau local…");
        var lan = await CheckRoutesAsync(code.LanAddresses, code);
        if (lan is { } local)
        {
            progress?.Invoke("Serveur local reconnu : vérification de l'invitation…");
            await shell.JoinAsync(new DiscoveredServer(local.Address.Host, local.School,
                Machine(code, local.Address), string.Empty, local.Address));
            return await ActivateAsync(shell, code, local.School, "réseau local");
        }

        var internet = await TryInternetAsync(shell, code, progress);
        if (internet is not null) return internet;

        return code.HasInvitation
            ? $"Le serveur de {code.School} ne répond pas. Vérifiez sa connexion Internet et son accès par Internet, ou rejoignez le même Wi-Fi."
            : $"Le serveur de {code.School} ne répond pas sur ce réseau local. Vérifiez que les appareils sont sur le même Wi-Fi.";
    }

    private static async Task<string?> TryInternetAsync(
        ShellViewModel shell, PairingCode code, Action<string>? progress)
    {
        if (!code.HasInvitation) return null;
        progress?.Invoke("Connexion Internet chiffrée…");
        try
        {
            var link = Scolia.Transport.RemoteLink.Parse(code.Invitation!);
            using var probe = new Api.ScoliaClient(link.VirtualServer);
            probe.UseRemote(link);
            if (!await WithinAsync(probe.IsRunningAsync())) return null;
            await shell.JoinInternetAsync(link);
            return await ActivateAsync(shell, code, code.School, "Internet sécurisé");
        }
        catch (FormatException ex) { return ex.Message; }
        catch (Exception ex) when (ex is HttpRequestException or Api.ApiException or TaskCanceledException or IOException)
        { return null; }
    }

    private sealed record AvailableRoute(Uri Address, string School);

    private static async Task<AvailableRoute?> CheckRoutesAsync(
        IEnumerable<Uri> addresses, PairingCode code)
    {
        var candidates = addresses.Take(6).ToArray();
        if (candidates.Length == 0) return null;
        var probes = candidates.Select(async (address, priority) =>
        {
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
            // Seul le QR émis par le serveur contient la route WSS et le ticket
            // qui vient d'être accepté. Ce même appareil peut désormais retrouver
            // le serveur sur Render sans nouveau QR lorsqu'il quitte le Wi-Fi.
            if (code.HasInvitation && !shell.IsServerComputer)
            {
                try
                {
                    var link = Scolia.Transport.RemoteLink.Parse(code.Invitation!);
                    if (DevicePairingStore.CopyForTrustedRoute(shell.Api.Server, link.VirtualServer))
                        RemoteConnection.Save(new RemoteConfiguration(link, false, school));
                }
                catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException or
                    System.Security.Cryptography.CryptographicException)
                {
                    message += " Le secours Internet n'a pas été mémorisé ; vérifiez le stockage sécurisé de cet appareil.";
                }
            }
            shell.Notify(message);
            return message;
        }
        catch (Api.ApiException ex) when (ex.Status is 401 or 403 or 429)
        {
            return "La liaison réseau fonctionne, mais l'invitation est expirée, déjà utilisée ou non autorisée. Demandez au serveur un nouveau QR.";
        }
        catch (Exception ex) when (ex is Api.OfflineException or HttpRequestException or TaskCanceledException or IOException)
        {
            return "La liaison a été enregistrée, mais le serveur n'a pas validé l'invitation. Vérifiez le Wi-Fi ou l'accès par Internet et réessayez avec un nouveau QR.";
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
