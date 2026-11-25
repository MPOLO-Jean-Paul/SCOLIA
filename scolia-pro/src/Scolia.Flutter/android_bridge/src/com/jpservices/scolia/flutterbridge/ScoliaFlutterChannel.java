package com.jpservices.scolia.flutterbridge;

import android.os.Handler;
import android.os.Looper;
import io.flutter.embedding.engine.FlutterEngine;
import io.flutter.plugin.common.JSONMethodCodec;
import io.flutter.plugin.common.MethodChannel;
import org.json.JSONObject;
import org.json.JSONException;
import java.util.UUID;

/** Transport de présentation uniquement. Les autorisations restent dans le moteur .NET. */
public final class ScoliaFlutterChannel {
    private final MethodChannel channel;
    private final Handler main = new Handler(Looper.getMainLooper());
    private final CommandHandler handler;
    private String pendingId;
    private MethodChannel.Result pending;
    private Runnable deadline;
    private boolean closed;

    public ScoliaFlutterChannel(Object engine, CommandHandler handler) {
        if (Looper.myLooper() != Looper.getMainLooper()) throw new IllegalStateException("Main thread required");
        if (!(engine instanceof FlutterEngine) || handler == null) throw new IllegalArgumentException("Invalid host");
        this.handler = handler;
        channel = new MethodChannel(((FlutterEngine)engine).getDartExecutor().getBinaryMessenger(),
            "com.jpservices.scolia/mobile/v1", JSONMethodCodec.INSTANCE);
        channel.setMethodCallHandler((call, result) -> {
            if (closed) { result.error("closed", "Moteur indisponible.", null); return; }
            if (pending != null) { result.error("busy", "Opération en cours.", null); return; }
            if (!(call.arguments instanceof JSONObject) && call.arguments != null) {
                result.error("invalid", "Arguments invalides.", null); return;
            }
            final String args = call.arguments == null ? "{}" : call.arguments.toString();
            if (args.length() > 8192 || call.method.length() > 40) {
                result.error("invalid", "Arguments invalides.", null); return;
            }
            final String id = UUID.randomUUID().toString();
            pendingId = id;
            pending = result;
            deadline = () -> finish(id, null, "timeout");
            main.postDelayed(deadline, 55000);
            try { this.handler.onCommand(id, call.method, args); }
            catch (RuntimeException ex) { finish(id, null, "unavailable"); }
        });
    }

    public void complete(String requestId, String responseJson, String errorCode) {
        main.post(() -> finish(requestId, responseJson, errorCode));
    }

    private void finish(String id, String json, String error) {
        if (closed || pending == null || !pendingId.equals(id)) return;
        MethodChannel.Result result = pending;
        main.removeCallbacks(deadline);
        deadline = null;
        pending = null;
        pendingId = null;
        if (error != null && !error.isEmpty()) {
            result.error("unavailable", "Commande indisponible.", null);
            return;
        }
        try { result.success(new JSONObject(json)); }
        catch (JSONException | NullPointerException ex) { result.error("invalid", "Réponse invalide.", null); }
    }

    public void close() {
        if (Looper.myLooper() != Looper.getMainLooper()) { main.post(this::close); return; }
        if (closed) return;
        closed = true;
        channel.setMethodCallHandler(null);
        if (deadline != null) main.removeCallbacks(deadline);
        if (pending != null) pending.error("closed", "Moteur indisponible.", null);
        pending = null;
        pendingId = null;
        deadline = null;
    }
}
