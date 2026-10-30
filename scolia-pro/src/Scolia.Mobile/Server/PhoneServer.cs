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

    public string DataDirectory => Path.Combine(context.FilesDir?.AbsolutePath ?? Path.GetTempPath(), "serveur");

    public void Start() => StartCore(startForegroundService: true);

    // Appelé seulement par le service Android déjà en avant-plan.
    internal void RestoreAfterServiceRestart() => StartCore(startForegroundService: false);

    private void StartCore(bool startForegroundService)
    {
        lock (gate)
        {
            shouldHost = true;
            if (startForegroundService) ServeurScolia.Begin(context);
            Enqueue(StartEmbeddedAsync);
        }
    }

    public void Restart()
    {
        lock (gate)
        {
            shouldHost = true;
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
