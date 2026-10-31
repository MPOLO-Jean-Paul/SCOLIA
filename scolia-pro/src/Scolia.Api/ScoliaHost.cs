using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using Microsoft.AspNetCore.ResponseCompression;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Scolia.Api;
using Scolia.Api.Endpoints;
using Scolia.Api.Security;
using Scolia.Application.Abstractions;
using Scolia.Domain.Security;
using Scolia.Infrastructure;
using Scolia.Infrastructure.Backup;
using Serilog;
using Microsoft.AspNetCore.DataProtection;

namespace Scolia.Api;

/// <summary>
/// Serveur SCOLIA d'un établissement, prêt à démarrer. Sur l'ordinateur, c'est le programme SCOLIA-Serveur.
/// Sur un téléphone qui sert d'ordinateur principal, l'application le démarre en elle-même (<see cref="EmbeddedServer"/>) :
/// mêmes données, mêmes règles, même accès des autres postes par le Wi-Fi.
/// </summary>
public static class ScoliaHost
{
    /// <summary>
    /// Prépare le serveur. Rend null quand un serveur tourne déjà sur ce poste de bureau : sa fenêtre est alors rouverte.
    /// </summary>
    public static async Task<WebApplication?> CreateAsync(string[] args, EmbeddedServer? embedded = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = embedded?.DataDirectory ?? AppContext.BaseDirectory,
        });
        if (embedded is not null) builder.Configuration.AddInMemoryCollection(embedded.Settings());

        // Données à côté de l'exécutable (copie portable), sauf configuration contraire.
        var defaultData = Path.Combine(AppContext.BaseDirectory, "donnees");

        builder.Host.UseSerilog((context, configuration) =>
        {
            var dataDirectory = context.Configuration["Scolia:Stockage:DataDirectory"] is { Length: > 0 } configured ? Environment.ExpandEnvironmentVariables(configured) : defaultData;
            Directory.CreateDirectory(Path.Combine(dataDirectory, "journaux"));
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File(Path.Combine(dataDirectory, "journaux", "scolia-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 60,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
        });

        var port = builder.Configuration.GetValue("Scolia:Reseau:Port", 5080);
        var lan = builder.Configuration.GetValue("Scolia:Reseau:AccesReseauLocal", false);
        var desktop = embedded is null && OperatingSystem.IsWindows() && Environment.UserInteractive && !Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService();

        // Deuxième lancement sur le même poste : on rouvre la fenêtre au lieu de démarrer un second serveur.
        if (desktop && DesktopLauncher.AlreadyRunning(port))
        {
            // La fenêtre existe déjà : on la fait apparaître au lieu d'en ouvrir une seconde.
            if (!DesktopLauncher.ShowRunningInstance(port)) DesktopLauncher.OpenInBrowser($"http://localhost:{port}");
            return null;
        }
        if (embedded is null) builder.Host.UseWindowsService(o => o.ServiceName = "SCOLIA");
        if (string.IsNullOrEmpty(builder.Configuration["urls"]) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
            builder.WebHost.UseUrls(lan ? $"http://0.0.0.0:{port}" : $"http://localhost:{port}");
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 60 * 1024 * 1024);
        // Compression des réponses texte (interface, JSON) : Brotli d'abord, gzip sinon.
        builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
        builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
        builder.Services.AddResponseCompression(o =>
        {
            o.EnableForHttps = true;
            o.Providers.Add<BrotliCompressionProvider>();
            o.Providers.Add<GzipCompressionProvider>();
            o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/javascript", "image/svg+xml", "application/json"]);
        });

        builder.Services.AddScolia(builder.Configuration, defaultData);
        if (embedded is not null)
        {
            // Serveur dans l'application : les clés des sessions restent avec les données, une session survit à un redémarrage.
            builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(embedded.DataDirectory, "cles")));
            embedded.ConfigureServices?.Invoke(builder.Services);
        }
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ProblemHandler>();
        builder.Services.AddOpenApi();
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        });

        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
        {
            o.Cookie.Name = "scolia.session";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = TimeSpan.FromHours(10);
            o.SlidingExpiration = true;
            o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            o.Events.OnValidatePrincipal = SessionFactory.ValidateAsync;
        });
        builder.Services.AddAuthorization(o =>
        {
            foreach (var permission in Permissions.All) o.AddPolicy(permission, p => p.RequireClaim(ScoliaClaims.Permission, permission));
        });
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy("connexion", http => RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "local",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        builder.Services.Configure<ForwardedHeadersOptions>(o => o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

        var app = builder.Build();
        await app.Services.InitializeDatabaseAsync();

        app.UseExceptionHandler();
        app.UseResponseCompression();
        app.UseForwardedHeaders();
        app.Use(async (http, next) =>
        {
            var h = http.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            h["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=()";
            h["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data: blob:; font-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'; frame-src 'self' blob:; object-src 'self' blob:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
            if (http.Request.Path.StartsWithSegments("/api")) h["Cache-Control"] = "no-store";
            await next();
        });
        // Protection CSRF : toute écriture de l'API exige un en-tête personnalisé, impossible à envoyer depuis un autre site.
        app.Use(async (http, next) =>
        {
            if (http.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method)
                && http.Request.Headers["X-Scolia"] != "1")
            {
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                await http.Response.WriteAsJsonAsync(new { title = "Requête refusée : en-tête de sécurité manquant." });
                return;
            }
            await next();
        });
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = c =>
            {
                if (c.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) c.Context.Response.Headers.CacheControl = "no-cache";
                else if (c.Context.Request.Path.StartsWithSegments("/assets")) c.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            },
        });
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        // Une écriture déjà faite rend sa réponse d'origine avant tout autre contrôle.
        app.UseIdempotency();
        app.UseLiveLocks();

        if (app.Environment.IsDevelopment()) app.MapOpenApi();
        app.MapScoliaEndpoints();
        app.MapLiveEndpoints();
        // Demande d'un second lancement : seule une requête venant de ce poste est acceptée.
        if (desktop)
            app.MapGet("/api/bureau/afficher", (HttpContext http) =>
            {
                if (!IPAddress.IsLoopback(http.Connection.RemoteIpAddress ?? IPAddress.None)) return Results.NotFound();
                DesktopLauncher.Open($"http://localhost:{port}");
                return Results.NoContent();
            });
        app.MapFallbackToFile("index.html");

        var storage = app.Services.GetRequiredService<IOptions<ScoliaStorageOptions>>().Value;
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var listening = app.Urls.FirstOrDefault() ?? $"http://localhost:{port}";
            var url = listening.Replace("0.0.0.0", "localhost", StringComparison.Ordinal).Replace("[::]", "localhost", StringComparison.Ordinal);
            Log.Information("SCOLIA est prêt : {Url} · données : {Data} · accès réseau local : {Lan}", url, storage.DataDirectory, lan);
            if (!desktop) return;
        #if SCOLIA_DESKTOP
            // L'icône est toujours présente sur un poste de bureau : c'est elle qui permet de quitter SCOLIA.
            // Désactivable : le client natif lance ce serveur sans icône (il en a déjà une).
            if (app.Configuration.GetValue("Scolia:Icone", true)) TrayHost.Start(app, url, storage.DataDirectory, lan, port);
        #endif
            if (app.Configuration.GetValue("Scolia:OuvrirNavigateur", true)) DesktopLauncher.Open(url);
        });
        return app;
    }
}
