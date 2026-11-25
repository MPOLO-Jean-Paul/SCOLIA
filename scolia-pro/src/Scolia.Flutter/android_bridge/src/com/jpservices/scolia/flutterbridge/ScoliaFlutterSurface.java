package com.jpservices.scolia.flutterbridge;

import android.app.Activity;
import android.view.View;
import io.flutter.embedding.android.FlutterTextureView;
import io.flutter.embedding.android.FlutterView;
import io.flutter.embedding.engine.FlutterEngine;
import io.flutter.embedding.engine.dart.DartExecutor;

/** Engine and texture view share the existing Activity; no second database or process. */
public final class ScoliaFlutterSurface {
    private FlutterEngine engine;
    private FlutterView view;
    public ScoliaFlutterSurface(Activity activity) {
        engine = new FlutterEngine(activity);
        try {
            view = new FlutterView(activity, new FlutterTextureView(activity));
            view.attachToFlutterEngine(engine);
        } catch (RuntimeException | Error ex) {
            engine.destroy();
            engine = null;
            throw ex;
        }
    }
    public Object getEngine() { return engine; }
    public View getSurfaceView() { return view; }
    public void start() {
        if (engine == null) throw new IllegalStateException("Engine closed");
        engine.getLifecycleChannel().appIsResumed();
        engine.getDartExecutor().executeDartEntrypoint(DartExecutor.DartEntrypoint.createDefault());
    }
    public void close() {
        if (engine == null) return;
        engine.getLifecycleChannel().appIsPaused();
        if (view != null) view.detachFromFlutterEngine();
        engine.destroy();
        engine = null;
        view = null;
    }
}
