package com.jpservices.scolia.owner;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.webkit.CookieManager;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.ImageButton;
import android.widget.LinearLayout;
import android.widget.PopupMenu;
import android.widget.TextView;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;

/**
 * SCOLIA JP-Services Administration.
 * No owner token, school data or Cloudflare API credentials are bundled.
 * Cloudflare Access MFA is handled by the protected origin in Android WebView.
 */
public final class MainActivity extends Activity {
    private static final String CONSOLE = "https://scolia-pro-owner.jeanpaulmpolo242.workers.dev/console";
    private static final String RELEASE_API =
        "https://api.github.com/repos/MPOLO-Jean-Paul/SCOLIA/releases?per_page=50";
    private static final String RELEASE_PREFIX = "scolia-owner-v";
    private static final String VERSION = "1.0.0";
    private static final int NAVY = Color.rgb(13, 26, 48);
    private static final int SLATE = Color.rgb(101, 121, 144);
    private static final int BLUE = Color.rgb(51, 117, 238);
    private WebView browser;
    private TextView status;
    private volatile boolean updateChecking;

    private int dp(float amount) {
        return Math.round(amount * getResources().getDisplayMetrics().density);
    }

    private GradientDrawable background(int color, float radius) {
        GradientDrawable d = new GradientDrawable();
        d.setColor(color);
        d.setCornerRadius(dp(radius));
        return d;
    }

    private TextView label(String content, int size, int color, boolean bold) {
        TextView text = new TextView(this);
        text.setText(content);
        text.setTextColor(color);
        text.setTextSize(size);
        if (bold) text.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        return text;
    }

    private TextView pill(String content, int color) {
        TextView chip = label(content, 12, color, true);
        chip.setGravity(Gravity.CENTER);
        chip.setPadding(dp(10), dp(6), dp(10), dp(6));
        chip.setBackground(background(Color.rgb(231, 239, 252), 20));
        return chip;
    }

    private TextView navButton(String content, View.OnClickListener action) {
        TextView b = label(content, 13, Color.rgb(58, 81, 117), true);
        b.setGravity(Gravity.CENTER);
        b.setPadding(dp(12), 0, dp(12), 0);
        b.setOnClickListener(action);
        return b;
    }

    @Override public void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        getWindow().setStatusBarColor(NAVY);
        getWindow().setNavigationBarColor(Color.rgb(250, 251, 254));
        getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR);

        LinearLayout main = new LinearLayout(this);
        main.setOrientation(LinearLayout.VERTICAL);
        main.setBackgroundColor(Color.rgb(248, 250, 253));
        setContentView(main);

        LinearLayout header = new LinearLayout(this);
        header.setOrientation(LinearLayout.VERTICAL);
        header.setPadding(dp(18), dp(13), dp(18), dp(12));
        header.setBackgroundColor(NAVY);
        main.addView(header, new LinearLayout.LayoutParams(-1, dp(90)));

        LinearLayout top = new LinearLayout(this);
        top.setGravity(Gravity.CENTER_VERTICAL);
        header.addView(top, new LinearLayout.LayoutParams(-1, dp(33)));
        TextView monogram = label("S", 21, Color.WHITE, true);
        monogram.setGravity(Gravity.CENTER);
        monogram.setBackground(background(BLUE, 12));
        top.addView(monogram, new LinearLayout.LayoutParams(dp(35), dp(35)));

        LinearLayout words = new LinearLayout(this);
        words.setOrientation(LinearLayout.VERTICAL);
        words.setPadding(dp(10), 0, 0, 0);
        top.addView(words, new LinearLayout.LayoutParams(0, -2, 1));
        words.addView(label("JP-SERVICES", 15, Color.WHITE, true));
        words.addView(label("SCOLIA · Administration propriétaire", 11,
                Color.rgb(177, 198, 224), false));

        TextView menu = label("⋮", 28, Color.WHITE, true);
        menu.setGravity(Gravity.CENTER);
        top.addView(menu, new LinearLayout.LayoutParams(dp(40), dp(38)));
        menu.setOnClickListener(v -> showMenu(menu));

        status = label("●  Connexion sécurisée · Cloudflare Access", 11,
                Color.rgb(181, 211, 247), false);
        status.setPadding(dp(4), dp(6), 0, 0);
        header.addView(status);

        browser = new WebView(this);
        browser.setBackgroundColor(Color.WHITE);
        WebSettings settings = browser.getSettings();
        settings.setJavaScriptEnabled(true); // Required by the owner console UI.
        settings.setDomStorageEnabled(true); // Cloudflare Access uses browser state.
        settings.setAllowFileAccess(false);
        settings.setAllowContentAccess(false);
        settings.setJavaScriptCanOpenWindowsAutomatically(false);
        settings.setMixedContentMode(WebSettings.MIXED_CONTENT_NEVER_ALLOW);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) settings.setSafeBrowsingEnabled(true);
        WebView.setWebContentsDebuggingEnabled(false);
        CookieManager.getInstance().setAcceptCookie(true);
        CookieManager.getInstance().setAcceptThirdPartyCookies(browser, false);
        browser.setWebChromeClient(new WebChromeClient());
        browser.setWebViewClient(new WebViewClient() {
            @Override public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                Uri uri = request.getUrl();
                if (!"https".equalsIgnoreCase(uri.getScheme())) return true;
                String h = uri.getHost() == null ? "" : uri.getHost().toLowerCase(java.util.Locale.ROOT);
                if (h.equals("scolia-pro-owner.jeanpaulmpolo242.workers.dev") ||
                    h.equals("rapid-bird-7b6b.cloudflareaccess.com") ||
                    h.endsWith(".cloudflareaccess.com") ||
                    h.equals("dash.cloudflare.com") ||
                    h.equals("accounts.google.com") ||
                    h.equals("login.microsoftonline.com")) return false;
                openExternal(uri.toString());
                return true;
            }
            @Override public void onPageFinished(WebView view, String url) {
                status.setText("●  Connexion HTTPS sécurisée · Cloudflare Access");
            }
            @Override public void onReceivedError(WebView view,
                WebResourceRequest req, android.webkit.WebResourceError error) {
                if (req.isForMainFrame()) status.setText("●  Connexion indisponible · Réessayer");
            }
        });
        main.addView(browser, new LinearLayout.LayoutParams(-1, 0, 1));

        LinearLayout bottom = new LinearLayout(this);
        bottom.setGravity(Gravity.CENTER_VERTICAL);
        bottom.setPadding(dp(8), 0, dp(8), 0);
        bottom.setBackgroundColor(Color.WHITE);
        main.addView(bottom, new LinearLayout.LayoutParams(-1, dp(57)));

        bottom.addView(navButton("Accueil", v -> browser.loadUrl(CONSOLE)),
            new LinearLayout.LayoutParams(0, -1, 1));
        bottom.addView(navButton("Actualiser", v -> browser.reload()),
            new LinearLayout.LayoutParams(0, -1, 1));
        bottom.addView(navButton("Mises à jour", v -> checkUpdates(true)),
            new LinearLayout.LayoutParams(0, -1, 1));

        browser.loadUrl(CONSOLE);
        checkUpdates(false);
    }

    private void showMenu(View anchor) {
        PopupMenu popup = new PopupMenu(this, anchor);
        popup.getMenu().add("Rechercher les mises à jour");
        popup.getMenu().add("Ouvrir dans Chrome");
        popup.getMenu().add("Verrouiller et effacer la session");
        popup.getMenu().add("À propos · " + VERSION);
        popup.setOnMenuItemClickListener(item -> {
            String s = String.valueOf(item.getTitle());
            if (s.startsWith("Rechercher")) checkUpdates(true);
            else if (s.startsWith("Ouvrir")) openExternal(CONSOLE);
            else if (s.startsWith("Verrouiller")) {
                new AlertDialog.Builder(this)
                    .setTitle("Verrouiller la console ?")
                    .setMessage("La session Cloudflare de ce navigateur sera supprimée. Aucun jeton propriétaire n'est conservé dans l'application.")
                    .setNegativeButton("Annuler", null)
                    .setPositiveButton("Verrouiller", (dialog, which) -> {
                        CookieManager.getInstance().removeAllCookies(ok -> {
                            CookieManager.getInstance().flush();
                            browser.clearCache(true);
                            browser.loadUrl(CONSOLE);
                        });
                    }).show();
            } else new AlertDialog.Builder(this)
                    .setTitle("JP-Services · SCOLIA Admin")
                    .setMessage("Version " + VERSION + "\nCanal indépendant des versions scolaires.\nConnexion sécurisée Cloudflare Access + MFA.")
                    .setPositiveButton("Fermer", null).show();
            return true;
        });
        popup.show();
    }

    private void openExternal(String url) {
        Intent intent = new Intent(Intent.ACTION_VIEW, Uri.parse(url));
        try { startActivity(intent); } catch (Exception ignored) { }
    }

    private int compareVersions(String left, String right) {
        String[] a=left.split("\\."), b=right.split("\\.");
        for (int i=0;i<3;i++) {
            int av=i<a.length ? Integer.parseInt(a[i]) : 0;
            int bv=i<b.length ? Integer.parseInt(b[i]) : 0;
            if (av != bv) return Integer.compare(av, bv);
        }
        return 0;
    }

    private void checkUpdates(boolean interactive) {
        if (updateChecking) return;
        updateChecking = true;
        new Thread(() -> {
            String tag = null, releaseUrl = null, message = null;
            HttpURLConnection c = null;
            try {
                c = (HttpURLConnection) new URL(RELEASE_API).openConnection();
                c.setConnectTimeout(8000); c.setReadTimeout(8000);
                c.setRequestProperty("User-Agent", "SCOLIA-JPServices-Admin/" + VERSION);
                c.setRequestProperty("Accept", "application/vnd.github+json");
                if (c.getResponseCode() != 200) throw new Exception("Service de mises à jour indisponible.");
                try (BufferedReader reader=new BufferedReader(new InputStreamReader(
                    c.getInputStream(), StandardCharsets.UTF_8))) {
                    StringBuilder json=new StringBuilder(); String line;
                    while ((line=reader.readLine())!=null) json.append(line);
                    JSONArray list=new JSONArray(json.toString());
                    for(int i=0;i<list.length();i++) {
                        JSONObject item=list.getJSONObject(i);
                        String t=item.optString("tag_name");
                        if(!item.optBoolean("draft") && !item.optBoolean("prerelease") &&
                           t.startsWith(RELEASE_PREFIX) &&
                           (tag==null || compareVersions(t.substring(RELEASE_PREFIX.length()),tag.substring(RELEASE_PREFIX.length()))>0)) {
                            tag=t; releaseUrl=item.optString("html_url");
                        }
                    }
                }
            } catch (Exception ex) { message = "Impossible de vérifier les mises à jour. Vérifiez votre connexion."; }
            finally { if(c!=null)c.disconnect(); updateChecking=false; }
            final String latest=tag, url=releaseUrl, error=message;
            runOnUiThread(() -> {
                if (isFinishing()) return;
                if (error!=null) { if(interactive) new AlertDialog.Builder(this)
                    .setMessage(error).setPositiveButton("Fermer", null).show(); return; }
                boolean newer=latest!=null &&
                    compareVersions(latest.substring(RELEASE_PREFIX.length()), VERSION)>0;
                if (newer) new AlertDialog.Builder(this).setTitle("Mise à jour JP-Services")
                    .setMessage("Nouvelle version administrative " + latest.substring(RELEASE_PREFIX.length()) +
                        ". La version SCOLIA des établissements n'est pas concernée.")
                    .setNegativeButton("Plus tard",null)
                    .setPositiveButton("Voir la mise à jour",(d,w)->openExternal(url)).show();
                else if(interactive) new AlertDialog.Builder(this)
                    .setMessage(latest==null
                        ? "Aucune version officielle JP-Services Admin n'est encore publiée. Cette installation est une version de prévisualisation."
                        : "SCOLIA Admin " + VERSION + " est à jour sur le canal JP-Services.")
                    .setPositiveButton("Fermer",null).show();
            });
        }, "scolia-admin-update").start();
    }

    @Override public void onBackPressed() {
        if(browser!=null && browser.canGoBack()) browser.goBack();
        else super.onBackPressed();
    }

    @Override protected void onDestroy() {
        if (browser != null) { browser.stopLoading(); browser.destroy(); browser=null; }
        super.onDestroy();
    }
}
