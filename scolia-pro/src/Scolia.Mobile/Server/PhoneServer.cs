using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using Scolia.Api;
using Scolia.Application.Abstractions;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Mobile.Server;

/// <summary>
/// Serveur scolaire local, partagé par la fenêtre Android et son service au premier plan.
/// Les opérations de démarrage/arrêt sont mises en file afin de ne jamais ouvrir deux
/// serveurs sur le même port, même après une reprise Android ou un clic rapide.
/// </summary>
public sealed class PhoneServer(Context context) : ILocalServer
{
    private static readonly object SharedGate = new();
    private static PhoneServer? shared;
    public static PhoneServer Get(Context context)
    {
        lock (SharedGate)
            return shared ??= new PhoneServer(context.ApplicationContext ?? context);
    }

    private readonly object gate = new();
    private EmbeddedServer? server;
    private Task lifecycle = Task.CompletedTask;
    private bool shouldHost;
    // Une commande explicite de changement de serveur ne doit pas être annulée par
    // un OnResume Android arrivé pendant l'ouverture des réglages ou du VPN.
    private bool explicitlyStopped;

    /// <summary>Vrai seulement si l'API locale écoute effectivement, pas sur la seule présence de la notification.</summary>
    public bool IsServing
    {
        get { lock (gate) return shouldHost && server?.IsRunning == true; }
    }

    /// <summary>
    /// Retour de l'interface : réparer un service de premier plan disparu sans recréer
    /// la base, ni attendre que l'utilisateur se reconnecte. Minimiser SCOLIA,
    /// verrouiller Android ou ouvrir Tailscale ne sont jamais des commandes Stop().
    /// </summary>
    public void EnsureServingOnResume()
    {
        if (ConnectionStore.Load()?.Mode != ConnectionSettings.Local) return;
        lock (gate)
        {
            if (explicitlyStopped) return;
            shouldHost = true;
            if (!ServeurScolia.IsActive)
            {
                try { ServeurScolia.Begin(context); }
                catch (Exception ex) when (ex is Java.Lang.RuntimeException or InvalidOperationException
                    or System.ComponentModel.Win32Exception)
                {
                    Android.Util.Log.Error("SCOLIA", $"Réactivation du service serveur refusée : {ex.Message}");
                    return;
                }
            }
            if (server?.IsRunning != true) Enqueue(StartEmbeddedAsync);
        }
    }

    /// <summary>
    /// Surveillance exécutée par le service Android et non par la fenêtre Avalonia :
    /// si une panne locale ferme l'API pendant que le téléphone est verrouillé, tenter
    /// de la remettre en service. N'ouvre ni activité, ni VPN, ni second serveur.
    /// </summary>
    internal void EnsureServingFromForegroundService()
    {
        if (ConnectionStore.Load()?.Mode != ConnectionSettings.Local) return;
        lock (gate)
        {
            if (explicitlyStopped || server?.IsRunning == true) return;
            shouldHost = true;
            Enqueue(StartEmbeddedAsync);
        }
    }

    public string DataDirectory => Path.Combine(context.FilesDir?.AbsolutePath ?? Path.GetTempPath(), "serveur");

    public void Start() => StartCore(startForegroundService: true);

    // Appelé seulement par le service Android déjà en avant-plan.
    internal void RestoreAfterServiceRestart()
    {
        if (ConnectionStore.Load()?.Mode != ConnectionSettings.Local) return;
        lock (gate)
        {
            // Le service peut délivrer un événement tardif après un choix
            // explicite « rejoindre un autre établissement ». Le lock est
            // réentrant et empêche Stop() de s'intercaler entre les deux étapes.
            if (!explicitlyStopped) StartCore(startForegroundService: false);
        }
    }

    private void StartCore(bool startForegroundService)
    {
        lock (gate)
        {
            shouldHost = true;
            explicitlyStopped = false;
            if (startForegroundService)
            {
                ServeurScolia.Begin(context);
                // Les autres postes vont dépendre de ce téléphone : Android ne doit pas l'endormir écran éteint.
                try { AndroidBackground.AskOnce(context); }
                catch (Exception ex) { Android.Util.Log.Warn("SCOLIA", $"Demande batterie impossible : {ex.Message}"); }
            }
            Enqueue(StartEmbeddedAsync);
        }
    }

    public void Restart()
    {
        lock (gate)
        {
            shouldHost = true;
            explicitlyStopped = false;
            Enqueue(async () =>
            {
                await StopEmbeddedAsync().ConfigureAwait(false);
                await StartEmbeddedAsync().ConfigureAwait(false);
            });
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            shouldHost = false;
            explicitlyStopped = true;
            Enqueue(async () =>
            {
                await StopEmbeddedAsync().ConfigureAwait(false);
                bool stopNotification;
                lock (gate) stopNotification = !shouldHost;
                if (stopNotification) ServeurScolia.End(context);
            });
        }
    }

    private void Enqueue(Func<Task> action)
    {
        // Le fil UI et le service Android peuvent demander un démarrage en même temps.
        // Chaque étape attend strictement la précédente (et traite ses erreurs).
        var previous = lifecycle;
        lifecycle = Task.Run(async () =>
        {
            try { await previous.ConfigureAwait(false); }
            catch (Exception ex) { Android.Util.Log.Error("SCOLIA", $"Cycle serveur précédent : {ex}"); }
            try { await action().ConfigureAwait(false); }
            catch (Exception ex) { Android.Util.Log.Error("SCOLIA", $"Cycle serveur : {ex}"); }
        });
    }

    private async Task StartEmbeddedAsync()
    {
        lock (gate)
        {
            if (!shouldHost || server is not null) return;
        }

        var created = new EmbeddedServer
        {
            DataDirectory = DataDirectory,
            Port = ConnectionStore.LocalAddress.Port,
            DeviceName = Platform.DeviceName ?? "Téléphone",
            ConfigureServices = services => services.AddSingleton<IDocumentService, SkiaDocumentService>(),
        };
        try
        {
            await created.StartAsync().ConfigureAwait(false);
            // Si l'utilisateur a changé d'établissement pendant le démarrage,
            // fermer le serveur créé au lieu de le rendre accessible.
            bool keep;
            lock (gate)
            {
                keep = shouldHost;
                if (keep) server = created;
            }
            if (!keep) await created.StopAsync().ConfigureAwait(false);
            else
            {
                // Restaurer aussi le relais Internet (s'il avait été activé) :
                // la reconnexion ne doit ni arrêter le serveur ni effacer sa base.
                try { RemoteConnection.StartHostIfMissing(ConnectionStore.LocalAddress); }
                catch (Exception ex)
                { Android.Util.Log.Warn("SCOLIA", $"Relais distant non restauré : {ex.GetType().Name}"); }
            }
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("SCOLIA", $"Démarrage serveur impossible : {ex}");
            try { await created.StopAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { Android.Util.Log.Warn("SCOLIA", $"Nettoyage serveur : {cleanup.Message}"); }
            bool stopNotification;
            lock (gate) stopNotification = !shouldHost;
            if (stopNotification) ServeurScolia.End(context);
        }
    }

    private async Task StopEmbeddedAsync()
    {
        EmbeddedServer? stopping;
        lock (gate)
        {
            stopping = server;
            server = null;
        }
        if (stopping is not null) await stopping.StopAsync().ConfigureAwait(false);
    }
}
