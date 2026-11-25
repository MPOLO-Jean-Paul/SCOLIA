#if SCOLIA_FLUTTER_UI
using Avalonia.Threading;
using System.Runtime.CompilerServices;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Scolia.Flutter.AndroidBinding;

namespace Scolia.Mobile;

/// <summary>Connexion du canal Flutter au Shell natif. L'hôte doit gérer le moteur et la vue.</summary>
internal static class FlutterIntegration
{
    // Le Shell survit à la vue Flutter et peut être conservé lors d'une rotation.
    // Une seule instance garde la sérialisation des commandes à travers les reprises Android.
    private static readonly ConditionalWeakTable<ShellViewModel, FlutterCommandAdapter> Adapters = new();

    internal static ChannelConnection Connect(Java.Lang.Object engine, ShellViewModel shell,
        Func<bool> isCurrent, Action nativeFallback)
    {
        var adapter = Adapters.GetValue(shell, static current => new FlutterCommandAdapter(current));
        var dispatcher = new FlutterJsonDispatcher(adapter.ExecuteAsync);
        return new ChannelConnection(engine, (operation, json) =>
        {
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    if (!isCurrent()) throw new OperationCanceledException("Vue Flutter fermée.");
                    var response = await dispatcher.DispatchAsync(operation, json);
                    // Une ancienne commande peut terminer après le retour de l'appareil photo.
                    // Ne jamais transférer sa réponse ou fermer la nouvelle vue.
                    if (!isCurrent()) throw new OperationCanceledException("Vue Flutter remplacée.");
                    using var message = System.Text.Json.JsonDocument.Parse(response);
                    var stage = message.RootElement.GetProperty("stage").GetString();
                    // Repère de test sans identité, contenu de formulaire ni donnée d'établissement.
                    if (operation == "snapshot" && stage is "home" or "login" or "pairing")
                        Android.Util.Log.Info("SCOLIA-Flutter", "validation:bridge-ready");
                    completion.TrySetResult(response);
                    if (stage == "nativeScreen") nativeFallback();
                }
                catch (Exception ex) { completion.TrySetException(ex); }
            });
            return completion.Task;
        });
    }
}
#endif
