using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using Scolia.Api;
using Scolia.Application.Abstractions;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Mobile.Server;

/// <summary>
/// Ce téléphone comme serveur de l'établissement : le serveur SCOLIA tourne dans l'application, ses données dans le dossier
/// privé de l'application, et un service de premier plan le garde actif quand SCOLIA passe en arrière-plan.
/// </summary>
public sealed class PhoneServer(Context context) : ILocalServer
{
    private readonly object gate = new();
    private EmbeddedServer? server;
    private Task? running;

    /// <summary>Dossier des données de l'établissement sur ce téléphone, sauvegardes comprises.</summary>
    public string DataDirectory => Path.Combine(context.FilesDir?.AbsolutePath ?? Path.GetTempPath(), "serveur");

    public void Start()
    {
        lock (gate)
        {
            if (server is not null) return;
            ServeurScolia.Begin(context);
            var created = new EmbeddedServer
            {
                DataDirectory = DataDirectory,
                Port = ConnectionStore.LocalAddress.Port,
                DeviceName = Platform.DeviceName ?? "Téléphone",
                // QuestPDF n'existe pas pour Android : les PDF sont dessinés avec SkiaSharp, même charte.
                ConfigureServices = s => s.AddSingleton<IDocumentService, SkiaDocumentService>(),
            };
            server = created;
            running = Task.Run(async () =>
            {
                try { await created.StartAsync(); }
                catch (Exception ex)
                {
                    Android.Util.Log.Error("SCOLIA", $"Serveur du téléphone non démarré : {ex}");
                    lock (gate) { if (ReferenceEquals(server, created)) server = null; }
                    ServeurScolia.End(context);
                }
            });
        }
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    public void Stop()
    {
        EmbeddedServer? stopping;
        lock (gate)
        {
            stopping = server;
            server = null;
        }
        if (stopping is not null) _ = Task.Run(async () =>
        {
            if (running is { } start) await start.ConfigureAwait(false);
            await stopping.StopAsync().ConfigureAwait(false);
        });
        ServeurScolia.End(context);
    }
}
