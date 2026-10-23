using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Scolia.Api.Endpoints;
using Scolia.Api.Security;
using Scolia.Application.Abstractions;
using Scolia.Domain.Security;
using Scolia.Infrastructure;
using Scolia.Infrastructure.Backup;
using Serilog;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });

// Données à côté de l'exécutable (copie portable), sauf configuration contraire.
var defaultData = Path.Combine(AppContext.BaseDirectory, "donnees");
var dataDirectory = builder.Configuration["Scolia:Stockage:DataDirectory"] is { Length: > 0 } configured ? Environment.ExpandEnvironmentVariables(configured) : defaultData;
Directory.CreateDirectory(Path.Combine(dataDirectory, "journaux"));

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(dataDirectory, "journaux", "scolia-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 60,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"));

var port = builder.Configuration.GetValue("Scolia:Reseau:Port", 5080);
var lan = builder.Configuration.GetValue("Scolia:Reseau:AccesReseauLocal", false);
if (string.IsNullOrEmpty(builder.Configuration["urls"]) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    builder.WebHost.UseUrls(lan ? $"http://0.0.0.0:{port}" : $"http://localhost:{port}");
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 60 * 1024 * 1024);

builder.Services.AddScolia(builder.Configuration, defaultData);
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
app.UseForwardedHeaders();
app.Use(async (http, next) =>
{
    var h = http.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=()";
    h["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'; frame-src 'self' blob:; object-src 'self' blob:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
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

if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapScoliaEndpoints();
app.MapFallbackToFile("index.html");

var storage = app.Services.GetRequiredService<IOptions<ScoliaStorageOptions>>().Value;
app.Lifetime.ApplicationStarted.Register(() =>
{
    var url = $"http://localhost:{port}";
    Log.Information("SCOLIA est prêt : {Url} · données : {Data} · accès réseau local : {Lan}", url, storage.DataDirectory, lan);
    if (app.Configuration.GetValue("Scolia:OuvrirNavigateur", true) && OperatingSystem.IsWindows() && Environment.UserInteractive)
        DesktopLauncher.Open(url);
});

await app.RunAsync();

/// <summary>Ouvre SCOLIA dans une fenêtre d'application (Edge ou Chrome en mode application), sinon dans le navigateur par défaut.</summary>
internal static class DesktopLauncher
{
    public static void Open(string url)
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
        };
        try
        {
            var browser = candidates.FirstOrDefault(File.Exists);
            if (browser is not null) Process.Start(new ProcessStartInfo(browser, $"--app={url} --window-size=1366,860") { UseShellExecute = false });
            else Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warning(ex, "Ouverture automatique impossible : ouvrez {Url} dans votre navigateur.", url);
        }
    }
}

public partial class Program;
