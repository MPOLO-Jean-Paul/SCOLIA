using Com.Jpservices.Scolia.Flutterbridge;

namespace Scolia.Flutter.AndroidBinding;

/// <summary>Retient le callback JNI aussi longtemps que le canal est ouvert.</summary>
public sealed class ChannelConnection : IDisposable
{
    private readonly Callback callback;
    private readonly ScoliaFlutterChannel channel;
    private bool disposed;

    public ChannelConnection(Java.Lang.Object engine, Func<string, string, Task<string>> dispatch)
    {
        callback = new Callback(dispatch);
        channel = new ScoliaFlutterChannel(engine, callback);
        callback.Channel = channel;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        callback.Channel = null;
        channel.Close();
        // La fermeture Java est exécutée sur le thread Android par le canal.
        channel.Dispose();
        callback.Dispose();
    }

    private sealed class Callback(Func<string, string, Task<string>> dispatch) : Java.Lang.Object, ICommandHandler
    {
        internal ScoliaFlutterChannel? Channel { get; set; }

        public void OnCommand(string? requestId, string? operation, string? argumentsJson)
        {
            if (requestId is null || operation is null || argumentsJson is null) return;
            _ = ReplyAsync(requestId, operation, argumentsJson);
        }

        private async Task ReplyAsync(string id, string operation, string json)
        {
            string? response = null;
            var error = "";
            try { response = await dispatch(operation, json); }
            catch (Exception) { error = "unavailable"; }
            try { Channel?.Complete(id, response ?? "", error); }
            catch (Exception) { /* Canal détruit ou réponse tardive ; aucune donnée journalisée. */ }
        }
    }
}
