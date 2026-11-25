#if SCOLIA_FLUTTER_EXPERIMENT
using Avalonia.Threading;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Scolia.Flutter.AndroidBinding;

namespace Scolia.Mobile;

/// <summary>Connexion du canal Flutter au Shell natif. L'hôte doit gérer le moteur et la vue.</summary>
internal static class FlutterIntegration
{
    internal static ChannelConnection Connect(Java.Lang.Object engine, ShellViewModel shell, Action nativeFallback)
    {
        var adapter = new FlutterCommandAdapter(shell);
        var dispatcher = new FlutterJsonDispatcher(adapter.ExecuteAsync);
        return new ChannelConnection(engine, (operation, json) =>
        {
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    var response = await dispatcher.DispatchAsync(operation, json);
                    completion.TrySetResult(response);
                    using var message = System.Text.Json.JsonDocument.Parse(response);
                    if (message.RootElement.GetProperty("stage").GetString() == "nativeScreen") nativeFallback();
                }
                catch (Exception ex) { completion.TrySetException(ex); }
            });
            return completion.Task;
        });
    }
}
#endif
