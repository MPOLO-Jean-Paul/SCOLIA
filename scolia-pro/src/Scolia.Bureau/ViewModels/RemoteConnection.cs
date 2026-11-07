using System.Collections.ObjectModel;
using Scolia.Bureau.Infrastructure;
using Scolia.Transport;

namespace Scolia.Bureau.ViewModels;

/// <summary>Poste autorisé sur le serveur, indépendamment de l'état d'Internet.</summary>
public sealed class RemoteAuthorizedDeviceRowVm
{
    public RemoteAuthorizedDeviceRowVm(Guid id, string name, Func<Guid, Task> revoke)
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? "Appareil SCOLIA" : name;
        Remove = new AsyncCommand(() => revoke(Id));
    }
    public Guid Id { get; }
    public string Name { get; }
    public string Detail => "Autorisé · Reconnexion automatique après une coupure";
    public AsyncCommand Remove { get; }
}

/// <summary>Gestion réservée au secrétariat sur l'appareil principal. La comptabilité rejoint par invitation.</summary>
public sealed class RemoteConnectionViewModel : Observable
{
    private readonly ShellViewModel shell;
    private string relay = RemoteLink.DefaultRelay;
    private string invitation = string.Empty;
    private string message = string.Empty;
    /// <param name="prepareInvitation">Vrai dans « Réseau et activité » : une invitation de compte est préparée à l'ouverture.</param>
    public RemoteConnectionViewModel(ShellViewModel shell, bool prepareInvitation = true)
    {
        this.shell = shell;
        if (RemoteConnection.Load() is { } current)
        {
            relay = current.Link.Relay;
            if (prepareInvitation && current.Host && current.Enabled) _ = PrepareInvitationAsync(current.Link);
        }
        Enable = new AsyncCommand(EnableAsync);
        RefreshDevices = new AsyncCommand(RefreshDevicesAsync);
        if (CanManage) _ = RefreshDevicesAsync();
        RenewAccess = new AsyncCommand(async () =>
        {
            if (CanManage && RemoteConnection.Load() is { Host: true, Enabled: true } current)
                await PrepareInvitationAsync(current.Link);
        });
        Disable = new AsyncCommand(async () =>
        {
            if (!CanManage || !IsActive) return;
            // Les appareils reliés par Internet perdent l'accès : demander confirmation.
            if (!await shell.Confirm("Désactiver l'accès par Internet ?",
                    "Les appareils qui ne sont pas sur le Wi-Fi de l'établissement ne pourront plus joindre ce serveur. "
                    + "Pour les reconnecter, réactivez l'accès Internet : les appareils déjà autorisés n'ont pas besoin d'un nouveau code QR. Le partage Wi-Fi reste disponible.",
                    "Désactiver", danger: true))
                return;
            RemoteConnection.Disable();
            Invitation = string.Empty;
            Message = "Accès Internet temporairement désactivé. Les appareils autorisés restent mémorisés : ils se reconnecteront à la réactivation, sans nouveau QR.";
        });
    }

    /// <summary>Accès par Internet actif sur ce serveur : liaison activée et non expirée.</summary>
    public bool IsActive => RemoteConnection.Load() is { Host: true, Enabled: true } current && current.Link.ExpiresUtc > DateTime.UtcNow;
    public bool IsInactive => !IsActive;

    /// <summary>Nom du relais sans le protocole, pour l'afficher sans jargon technique.</summary>
    public string RelayHost => Uri.TryCreate(RemoteConnection.Load()?.Link.Relay ?? relay, UriKind.Absolute, out var uri) ? uri.Host : relay;

    public string StatusTitle => !IsActive ? "Désactivé"
        : RemoteConnection.Hosting ? "Actif · relais connecté"
        : RemoteConnection.HostFailure is null ? "Connexion au relais…" : "Relais injoignable · nouvel essai automatique";

    public string StatusLine => !IsActive || RemoteConnection.Load() is null
        ? "Les appareils hors du Wi-Fi de l'établissement ne peuvent pas joindre ce serveur. Activez l'accès pour les téléphones et ordinateurs distants."
        : RemoteConnection.Hosting
            ? $"Les appareils autorisés joignent ce serveur depuis n'importe quel réseau, par le relais sécurisé {RelayHost}. "
                + "Autorisation persistante jusqu'au retrait volontaire de l'appareil dans Réseau et activité."
            : $"Ce serveur n'est pas encore relié au relais {RelayHost} : les appareils distants et le jumelage par Internet attendent. "
                + Diagnosis(RemoteConnection.HostFailure, RemoteConnection.HostAttempts);

    /// <summary>Raison d'un échec de connexion au relais, en clair, avec la conduite à tenir.</summary>
    public static string Diagnosis(string? failure, int attempts)
    {
        var tries = attempts > 1 ? $" ({attempts} essais)" : string.Empty;
        return failure switch
        {
            null => "Connexion en cours…",
            "HTTP 409" => $"Le relais garde encore une ancienne session de ce serveur{tries}. Le relais Cloudflare corrigé la remplace aussitôt ; sinon elle se libère en quelques minutes.",
            "HTTP 403" => $"Le relais refuse l'identité de ce serveur{tries}. Désactivez puis réactivez l'accès par Internet pour renouveler la liaison.",
            "HTTP 404" => $"Adresse du relais incorrecte{tries} : vérifiez l'adresse wss://…/ dans le réglage avancé.",
            "HTTP 426" or "HTTP 400" or "HTTP 405" => $"Cette adresse ne correspond pas à un relais SCOLIA{tries} : vérifiez le réglage avancé.",
            "HTTP 429" => $"Trop de tentatives vers le relais{tries} : nouvel essai automatique dans quelques secondes.",
            "HTTP 502" or "HTTP 503" or "HTTP 504" => $"Le relais est indisponible ou se réveille{tries} : nouvel essai automatique.",
            "délai dépassé" => $"Le relais met du temps à répondre (réveil d'un relais gratuit ou réseau lent){tries} : nouvel essai automatique.",
            "réseau indisponible" => $"Pas d'accès Internet depuis ce serveur, ou adresse du relais introuvable{tries}.",
            _ => $"Échec : {failure}{tries}. Nouvel essai automatique.",
        };
    }

    public Avalonia.Media.IBrush StatusBrush => ThemeBrush.Get(!IsActive ? "ToneNeutral" : RemoteConnection.Hosting ? "ToneGreen"
        : RemoteConnection.HostFailure is null ? "ToneBlue" : "ToneOrange");
    public string EnableLabel => IsActive ? "Vérifier la connexion" : "Activer l'accès par Internet";

    public void RefreshStatus()
    {
        foreach (var name in new[] { nameof(IsActive), nameof(IsInactive), nameof(RelayHost), nameof(StatusTitle), nameof(StatusLine), nameof(StatusBrush), nameof(EnableLabel) })
            Raise(name);
    }
    public bool CanManage => shell.IsServerComputer && shell.Can("parametres.ecrire");

    public string Relay { get => relay; set => Set(ref relay, value); }
    public string Invitation { get => invitation; private set => Set(ref invitation, value); }
    public string Message { get => message; private set => Set(ref message, value); }
    public ObservableCollection<RemoteAuthorizedDeviceRowVm> Devices { get; } = [];
    public bool NoDevices => Devices.Count == 0;
    private string deviceStatus = "Liste des appareils autorisés sur ce serveur.";
    public string DeviceStatus { get => deviceStatus; private set => Set(ref deviceStatus, value); }
    public AsyncCommand RefreshDevices { get; }
    private sealed record DeviceReply(Guid Id, string Name, DateTime ExpiresUtc, bool Revoked);

    private async Task RefreshDevicesAsync()
    {
        if (!CanManage) return;
        try
        {
            var devices = await shell.Api.GetAsync<List<DeviceReply>>("api/jumelage/appareils");
            Devices.Clear();
            foreach (var device in devices.Where(d => !d.Revoked))
                Devices.Add(new RemoteAuthorizedDeviceRowVm(device.Id, device.Name, RemoveDeviceAsync));
            Raise(nameof(NoDevices));
            DeviceStatus = Devices.Count == 0
                ? "Aucun appareil autorisé. Affichez un QR pour en relier un."
                : $"{Devices.Count} appareil(s) autorisé(s) sur le serveur, même hors connexion.";
        }
        catch (Exception ex) when (ex is Api.ApiException or HttpRequestException or TaskCanceledException or IOException)
        {
            DeviceStatus = "Liste indisponible : vérifiez la connexion au serveur.";
        }
    }

    private async Task RemoveDeviceAsync(Guid id)
    {
        if (!CanManage) return;
        var item = Devices.FirstOrDefault(x => x.Id == id);
        if (item is null) return;
        if (!await shell.Confirm("Retirer cet appareil ?",
            $"L'appareil « {item.Name} » perdra immédiatement son accès Internet au serveur. Il faudra un nouveau QR pour le réautoriser.",
            "Retirer l'appareil")) return;
        try
        {
            await shell.Api.SendAsync(HttpMethod.Post, $"api/jumelage/appareils/{id}/revoquer", new { });
            Devices.Remove(item);
            Raise(nameof(NoDevices));
            DeviceStatus = $"{Devices.Count} appareil(s) toujours autorisé(s).";
            shell.Notify("Appareil retiré : son ancienne autorisation ne fonctionne plus.");
        }
        catch (Exception ex) when (ex is Api.ApiException or HttpRequestException or TaskCanceledException or IOException)
        {
            DeviceStatus = "Retrait non confirmé : vérifiez la connexion et actualisez la liste.";
        }
    }

    public AsyncCommand Enable { get; }
    public AsyncCommand RenewAccess { get; }
    private sealed record InvitationReply(string Token, DateTime ExpiresUtc);

    private async Task PrepareInvitationAsync(RemoteLink link)
    {
        if (!CanManage) return;
        try
        {
            var access = await shell.Api.SendAsync<InvitationReply>(HttpMethod.Post, "api/jumelage/inviter", new { });
            var addresses = Platform.IsMobile || PosteAdmin.LanEnabled() ? PosteAdmin.Addresses() : [];
            var qr = PairingCode.ForThisServer(shell.Login.SchoolName, DeviceIdentity.Name, addresses, link.Invitation())
                with { AccessTicket = access.Token };
            Invitation = qr.Text();
            Message = "Invitation de connexion automatique prête. Valable 5 minutes pour un seul appareil ; renouvelez-la pour le prochain.";
        }
        catch (Exception ex) when (ex is Api.ApiException or HttpRequestException or IOException or TaskCanceledException)
        {
            Invitation = string.Empty;
            Message = "Liaison Internet active, mais l'invitation de compte n'a pas été créée. Ouvrez « Relier un appareil » pour générer un nouveau QR.";
        }
    }

    public AsyncCommand Disable { get; }

    private async Task EnableAsync()
    {
        if (!CanManage) { Message = "Le secrétariat configure la liaison sur l'appareil principal."; return; }
        try
        {
            var previous = RemoteConnection.Load();
            // Un clic répété sur « Activer » ne doit jamais invalider les appareils
            // déjà autorisés. Rotation seulement après expiration, changement de
            // relais ou réactivation d'une connexion volontairement désactivée.
            var addressText = Relay.Trim();
            // Désactiver Internet met le relais en pause : cela ne révoque pas les
            // appareils et ne doit JAMAIS faire tourner la clé de la salle.
            var reuse = previous is { Host: true } &&
                string.Equals(previous.Link.Relay, addressText, StringComparison.OrdinalIgnoreCase);
            var link = reuse
                ? (previous!.Link.ExpiresUtc <= DateTime.UtcNow ? previous.Link.Resume() : previous.Link)
                : previous is { Host: true } ? previous.Link.Renew(addressText)
                : RemoteLink.Create(addressText);
            // Vérifier le relais avant de remplacer une liaison encore utilisable.
            var address = new UriBuilder(link.Relay) { Scheme = new Uri(link.Relay).Scheme == "wss" ? "https" : "http", Path = "/health" }.Uri;
            Message = "Vérification de la connexion Internet sécurisée et du certificat HTTPS…";
            using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(75) };
            using var response = await probe.GetAsync(address);
            response.EnsureSuccessStatusCode();
            RemoteConnection.Save(new RemoteConfiguration(link, true, shell.Login.SchoolName));
            // « Vérifier » relance aussi une connexion bloquée : un hôte qui n'est pas relié repart de zéro.
            if (reuse && RemoteConnection.Hosting) RemoteConnection.StartHostIfMissing(shell.Api.Server);
            else RemoteConnection.StartHost(shell.Api.Server);
            RefreshStatus();
            // Attendre le vrai résultat au lieu de laisser « en cours » sans réponse.
            Message = "Relais joignable. Connexion de ce serveur au relais…";
            var started = DateTime.UtcNow;
            while (!RemoteConnection.Hosting && DateTime.UtcNow - started < TimeSpan.FromSeconds(45))
            {
                await Task.Delay(1000);
                RefreshStatus();
            }
            if (!RemoteConnection.Hosting)
            {
                Message = "Le relais répond, mais ce serveur n'a pas encore pu s'y relier. "
                    + Diagnosis(RemoteConnection.HostFailure, RemoteConnection.HostAttempts)
                    + " SCOLIA continue d'essayer en arrière-plan ; l'état ci-dessus se met à jour seul.";
                return;
            }
            if (reuse)
                Message = "Accès par Internet actif et relais connecté. Les appareils autorisés restent jumelés ; aucun nouveau QR n'est nécessaire.";
            else
                await PrepareInvitationAsync(link);
        }
        catch (Exception ex) when (ex is FormatException or HttpRequestException or TaskCanceledException or IOException)
        {
            Message = ex is FormatException
                ? $"Adresse du relais invalide : {ex.Message}"
                : ex is TaskCanceledException
                    ? "Le relais n'a pas répondu en 75 secondes (relais endormi, hors service ou réseau trop lent). La configuration précédente est conservée."
                    : ex is HttpRequestException { StatusCode: { } code }
                        ? $"Le relais a répondu « HTTP {(int)code} » : vérifiez son adresse et son déploiement. La configuration précédente est conservée."
                        : "Accès non activé : pas d'accès Internet depuis ce serveur, ou adresse du relais introuvable. La configuration précédente est conservée.";
        }
        finally { RefreshStatus(); }
    }
}
