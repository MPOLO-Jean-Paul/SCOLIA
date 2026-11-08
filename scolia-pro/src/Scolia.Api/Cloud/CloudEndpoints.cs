using Microsoft.EntityFrameworkCore;
using Scolia.Api.Endpoints;
using Scolia.Application.Abstractions;
using Scolia.Domain.Security;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Api.Cloud;

/// <summary>
/// Sauvegarde et relève en ligne (« nuage »). Activation, code de secours et récupération d'une copie se font
/// sur l'ordinateur principal lui-même, comme l'inscription de la licence ; l'état se consulte de partout.
/// Chaque compte connecté reçoit son accès de dépôt pour les opérations faites pendant une coupure.
/// </summary>
public static class CloudEndpoints
{
    private sealed record ActivationRequest(bool Enabled, bool Confirm);
    private sealed record RecoverRequest(string? RecoveryCode);

    private static bool ServerLocal(HttpContext http) =>
        LicenseEndpoints.IsServerLocal(http)
        || (http.RequestServices.GetRequiredService<IHostEnvironment>().IsEnvironment("Testing") && http.Connection.RemoteIpAddress is null);

    private static IResult LocalOnly() => Results.Json(new
    {
        title = "Opération réservée à l'ordinateur principal : faites-la depuis SCOLIA sur ce poste.",
        code = "serveur_local",
    }, statusCode: StatusCodes.Status403Forbidden);

    public static void MapCloudEndpoints(this IEndpointRouteBuilder routes)
    {
        var api = routes.MapGroup("/api/nuage").RequireAuthorization(Permissions.BackupManage);

        api.MapGet("", (CloudContinuityService cloud) => Results.Ok(cloud.Status));

        api.MapPost("/activation", async Task<IResult> (ActivationRequest input, HttpContext http, CloudContinuityService cloud,
            IAuditLog audit, ScoliaDbContext db, CancellationToken ct) =>
        {
            if (!ServerLocal(http)) return LocalOnly();
            if (!input.Confirm) return Results.BadRequest(new { title = "Confirmation du responsable de l'établissement requise." });
            var status = await cloud.EnableAsync(input.Enabled, ct);
            audit.Record(input.Enabled ? "nuage.active" : "nuage.arrete", "Nuage", null,
                input.Enabled ? "Sauvegarde et relève en ligne activées (chiffrées par l'école)." : "Sauvegarde et relève en ligne arrêtées.");
            await db.SaveChangesAsync(ct);
            return Results.Ok(status);
        });

        api.MapGet("/code-secours", async Task<IResult> (HttpContext http, CloudContinuityService cloud, IAuditLog audit,
            ScoliaDbContext db, CancellationToken ct) =>
        {
            if (!ServerLocal(http)) return LocalOnly();
            var code = cloud.RecoveryCode();
            audit.Record("nuage.code.consulte", "Nuage", null, "Code de secours de la sauvegarde en ligne affiché.");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { code, keyFingerprint = cloud.Status.KeyFingerprint });
        });

        api.MapPost("/code-secours/confirmation", (HttpContext http, CloudContinuityService cloud) =>
            ServerLocal(http) ? Results.Ok(cloud.ConfirmRecovery()) : LocalOnly());

        api.MapPost("/sauvegarder", async (CloudContinuityService cloud, CancellationToken ct) => Results.Ok(await cloud.BackupNowAsync(ct)));

        api.MapPost("/releve", async (CloudContinuityService cloud, CancellationToken ct) =>
        {
            var applied = await cloud.DrainAsync(ct);
            return Results.Ok(new { applied, status = cloud.Status });
        });

        api.MapGet("/sauvegardes", async (CloudContinuityService cloud, CancellationToken ct) => Results.Ok(await cloud.ListAsync(ct)));

        api.MapPost("/sauvegardes/{id:guid}/recuperer", async Task<IResult> (Guid id, RecoverRequest input, HttpContext http,
            CloudContinuityService cloud, IAuditLog audit, ScoliaDbContext db, CancellationToken ct) =>
        {
            if (!ServerLocal(http)) return LocalOnly();
            var info = await cloud.RecoverAsync(id, input.RecoveryCode, ct);
            audit.Record("nuage.recuperee", "Sauvegarde", info.Name, $"Copie en ligne récupérée en sauvegarde locale ({info.Name}).");
            await db.SaveChangesAsync(ct);
            return Results.Ok(info);
        });

        // Accès de dépôt : tout compte connecté, pour ses propres opérations hors ligne.
        routes.MapGet("/api/nuage/depot", async Task<IResult> (ICurrentUser current, ScoliaDbContext db, CloudContinuityService cloud, CancellationToken ct) =>
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == current.Id && u.IsActive, ct);
            return user is null ? Results.Unauthorized() : Results.Ok(cloud.DepositAccess(user));
        }).RequireAuthorization();
    }
}
