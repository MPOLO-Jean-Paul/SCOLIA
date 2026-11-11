using System.Security.Claims;
using System.Text.Json;
using Scolia.Api.Security;
using Scolia.Infrastructure.Live;

namespace Scolia.Api.Endpoints;

/// <summary>Annonce d'un poste : la page affichée, l'action en cours, et les verrous qu'il tient encore.</summary>
public sealed record PresenceInput(string? Page, string? Activity, List<string>? Locks);

/// <summary>Réservation d'une action : sa clé (« encaissement:… ») et un libellé lisible pour les autres postes.</summary>
public sealed record LockInput(string Key, string? Label);

/// <summary>
/// Le direct entre les postes de l'établissement : flux des changements (Server-Sent Events), présence et verrous.
/// Le flux reste ouvert ; un commentaire toutes les 15 secondes garde la connexion vivante à travers le Wi-Fi.
/// </summary>
public static class LiveEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);

    public static void MapLiveEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/direct").RequireAuthorization();

        g.MapGet("", async (HttpContext http, LiveHub hub, CancellationToken ct) =>
        {
            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";
            var (id, reader) = hub.Subscribe();
            try
            {
                await Write(http, "etat", hub.Snapshot(), ct);
                while (!ct.IsCancellationRequested)
                {
                    using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    wait.CancelAfter(Heartbeat);
                    try
                    {
                        var live = await reader.ReadAsync(wait.Token);
                        await Write(http, live.Kind, live, ct);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        await http.Response.WriteAsync(": en ligne\n\n", ct);
                        await http.Response.Body.FlushAsync(ct);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            finally { hub.Unsubscribe(id); }
        });

        g.MapGet("/etat", (LiveHub hub) => Results.Ok(hub.Snapshot()));

        g.MapPost("/presence", (PresenceInput input, HttpContext http, LiveHub hub) =>
        {
            if (!Identify(http, out var deviceId, out var device, out var user)) return Results.BadRequest(new { title = "Poste non identifié." });
            return Results.Ok(hub.Touch(deviceId, device, user, Clip(input.Page), Clip(input.Activity), input.Locks ?? []));
        });

        g.MapDelete("/presence", (HttpContext http, LiveHub hub) =>
        {
            if (LiveHeaders.DeviceId(http) is { } deviceId) hub.Leave(deviceId);
            return Results.NoContent();
        });

        g.MapPost("/verrous", (LockInput input, HttpContext http, LiveHub hub) =>
        {
            if (!Identify(http, out var deviceId, out var device, out var user)) return Results.BadRequest(new { title = "Poste non identifié." });
            if (string.IsNullOrWhiteSpace(input.Key) || input.Key.Length > 120) return Results.BadRequest(new { title = "Action à réserver invalide." });
            var (granted, holder) = hub.TryLock(input.Key, Clip(input.Label) ?? input.Key, deviceId, device, user);
            return granted ? Results.Ok(holder) : Results.Json(Busy(holder), statusCode: StatusCodes.Status409Conflict);
        });

        g.MapDelete("/verrous", (string cle, HttpContext http, LiveHub hub) =>
        {
            if (LiveHeaders.DeviceId(http) is { } deviceId) hub.Release(cle, deviceId);
            return Results.NoContent();
        });
    }

    /// <summary>
    /// Une écriture faite sous un verrou (en-tête X-Scolia-Verrou) est refusée si un autre poste tient ce verrou :
    /// deux encaissements simultanés du même apprenant ne peuvent pas aboutir tous les deux.
    /// </summary>
    public static IApplicationBuilder UseLiveLocks(this IApplicationBuilder app) => app.Use(async (HttpContext http, RequestDelegate next) =>
    {
        if (!HttpMethods.IsGet(http.Request.Method) && http.Request.Headers[LiveHeaders.LockHeader].ToString() is { Length: > 0 } keys)
        {
            var hub = http.RequestServices.GetRequiredService<LiveHub>();
            var deviceId = LiveHeaders.DeviceId(http);
            foreach (var key in keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (hub.HeldByOther(Uri.UnescapeDataString(key), deviceId) is { } holder)
                {
                    http.Response.StatusCode = StatusCodes.Status409Conflict;
                    await http.Response.WriteAsJsonAsync(Busy(holder));
                    return;
                }
        }
        await next(http);
    });

    private static object Busy(LockEntry holder) => new
    {
        title = $"{holder.Label} : déjà en cours sur {holder.Device} ({holder.User}). Attendez la fin de cette opération.",
        code = "verrou",
        holder,
    };

    private static bool Identify(HttpContext http, out string deviceId, out string device, out string user)
    {
        deviceId = LiveHeaders.DeviceId(http) ?? string.Empty;
        device = LiveHeaders.Device(http) ?? "Poste";
        user = http.User.FindFirstValue(ClaimTypes.Name) ?? "?";
        return deviceId.Length > 0;
    }

    private static string? Clip(string? text) => text is null ? null : text.Length > 80 ? text[..80] : text;

    private static async Task Write(HttpContext http, string kind, object payload, CancellationToken ct)
    {
        await http.Response.WriteAsync($"event: {kind}\ndata: {JsonSerializer.Serialize(payload, Json)}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }
}
