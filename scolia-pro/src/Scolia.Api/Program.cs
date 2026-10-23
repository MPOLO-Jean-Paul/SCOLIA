using System.Diagnostics;
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

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });

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
var desktop = OperatingSystem.IsWindows() && Environment.UserInteractive && !Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService();

// Deuxième lancement sur le même poste : on rouvre la fenêtre au lieu de démarrer un second serveur.
if (desktop && DesktopLauncher.AlreadyRunning(port))
{
    DesktopLauncher.Open($"http://localhost:{port}");
    return;
}
builder.Host.UseWindowsService(o => o.ServiceName = "SCOLIA");
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
    var listening = app.Urls.FirstOrDefault() ?? $"http://localhost:{port}";
    var url = listening.Replace("0.0.0.0", "localhost", StringComparison.Ordinal).Replace("[::]", "localhost", StringComparison.Ordinal);
    Log.Information("SCOLIA est prêt : {Url} · données : {Data} · accès réseau local : {Lan}", url, storage.DataDirectory, lan);
    if (!desktop) return;
#if SCOLIA_DESKTOP
    // L'icône est toujours présente sur un poste de bureau : c'est elle qui permet de quitter SCOLIA.
    TrayHost.Start(app, url, storage.DataDirectory, lan, port);
#endif
    if (app.Configuration.GetValue("Scolia:OuvrirNavigateur", true)) DesktopLauncher.Open(url);
});

await app.RunAsync();

public partial class Program;
