package com.jpservices.scolia.flutterbridge;

import android.app.Activity;
import android.view.View;
import io.flutter.embedding.android.FlutterTextureView;
import io.flutter.embedding.android.FlutterView;
import io.flutter.embedding.engine.FlutterEngine;
import io.flutter.embedding.engine.dart.DartExecutor;
import io.flutter.embedding.engine.renderer.FlutterUiDisplayListener;

/** Engine and texture view share the existing Activity; no second database or process. */
public final class ScoliaFlutterSurface {
    private FlutterEngine engine;
    private FlutterView view;
    public ScoliaFlutterSurface(Activity activity) {
        String phase = "engine";
        try {
            // Flutter runs inside a host that already owns its plugins and lifecycle.
            // No generated plugin registrant is required for SCOLIA's explicit channel.
            engine = new FlutterEngine(activity, null, false);
            phase = "texture-view";
            view = new FlutterView(activity, new FlutterTextureView(activity));
            view.addOnFirstFrameRenderedListener(new FlutterUiDisplayListener() {
                @Override public void onFlutterUiDisplayed() {
                    android.util.Log.i("SCOLIA-Flutter", "validation:first-frame");
                }
                @Override public void onFlutterUiNoLongerDisplayed() {}
            });
            phase = "attach-engine";
            view.attachToFlutterEngine(engine);
        } catch (RuntimeException | Error ex) {
            android.util.Log.w("SCOLIA-Flutter", "Flutter initialization failed at " + phase + ": " + ex.getClass().getSimpleName());
            if (activity.getPackageName().endsWith(".fluttervalidation")) {
                android.util.Log.e("SCOLIA-Flutter", "Isolated Flutter initialization failure", ex);
            }
            if (engine != null) engine.destroy();
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
