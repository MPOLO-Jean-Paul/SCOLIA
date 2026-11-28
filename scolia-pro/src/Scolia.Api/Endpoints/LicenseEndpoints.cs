using System.Net;
using Scolia.Api.Security;
using Scolia.Domain.Security;

namespace Scolia.Api.Endpoints;

/// <summary>
/// Contrat scolaire : la licence vient d'un service commercial distinct.
/// Chaque poste voit son statut ; seul le responsable sur le serveur scolaire
/// peut approuver un fournisseur, enregistrer ou revalider la licence.
/// </summary>
public static class LicenseEndpoints
{
    /// <summary>Seule l'application du serveur principal peut enregistrer
    /// la licence ou approuver un relais, jamais un appel du réseau ou du tunnel.</summary>
    public static bool IsServerLocal(HttpContext http) =>
        IPAddress.IsLoopback(http.Connection.RemoteIpAddress ?? IPAddress.None)
        && http.Request.Headers["X-Scolia-Relay-Connection"] != "1"
        && !http.Request.Headers.ContainsKey("X-Forwarded-For")
        && !http.Request.Headers.ContainsKey("Forwarded");

    private sealed record EnrollRequest(string SchoolName, string InviteCode, bool Consent);
    private sealed record RelayConsentRequest(bool Enabled, bool DisclosedToSchoolAdmin);
    private sealed record RelayApproveRequest(long Revision, bool Confirm, bool DisclosedToSchoolAdmin);

    public static void MapLicenseEndpoints(this IEndpointRouteBuilder routes)
    {
        var api = routes.MapGroup("/api/licence").RequireAuthorization();

        api.MapGet("", (SchoolLicenseService licenses) => Results.Ok(licenses.Current));

        api.MapPost("/enregistrer", async Task<IResult> (EnrollRequest input,
            HttpContext http, SchoolLicenseService licenses, CancellationToken ct) =>
        {
            if (!IsServerLocal(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (!input.Consent) return Results.BadRequest(new { title = "Consentement du responsable requis." });
            try
            {
                var status = await licenses.EnrollAsync(input.SchoolName, input.InviteCode, input.Consent, ct);
                return Results.Ok(status);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { title = ex.Message });
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                return Results.Problem("Service commercial momentanément inaccessible. Le serveur scolaire reste disponible.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireAuthorization(Permissions.SettingsWrite).RequireRateLimiting("connexion");

        api.MapPost("/actualiser", async Task<IResult> (HttpContext http,
            SchoolLicenseService licenses, CancellationToken ct) =>
        {
            if (!IsServerLocal(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            await licenses.RefreshAsync(ct);
            return Results.Ok(licenses.Current);
        }).RequireAuthorization(Permissions.SettingsWrite);

        // Le relais a une configuration propriétaire, mais une autorisation
        // d'activation distincte. Personne ne peut transformer un changement
        // d'adresse en accès réseau secret à l'insu du responsable.
        api.MapGet("/relais", async Task<IResult> (SchoolLicenseService licenses,
            CancellationToken ct) =>
        {
            try { return Results.Ok(await licenses.RelayAsync(ct)); }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException
                or TaskCanceledException)
            {
                return Results.Problem("Configuration du relais indisponible.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireAuthorization(Permissions.SettingsWrite);

        api.MapPost("/relais/consentement", async Task<IResult> (RelayConsentRequest input,
            HttpContext http, SchoolLicenseService licenses, CancellationToken ct) =>
        {
            if (!IsServerLocal(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (!input.DisclosedToSchoolAdmin)
                return Results.BadRequest(new { title = "Information explicite du responsable requise." });
            try { return Results.Ok(await licenses.RelayConsentAsync(input.Enabled, ct)); }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException
                or TaskCanceledException)
            {
                return Results.Problem("Accord de relais non confirmé.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireAuthorization(Permissions.SettingsWrite);

        api.MapPost("/relais/approuver", async Task<IResult> (RelayApproveRequest input,
            HttpContext http, SchoolLicenseService licenses, CancellationToken ct) =>
        {
            if (!IsServerLocal(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (!input.Confirm || !input.DisclosedToSchoolAdmin || input.Revision < 1)
                return Results.BadRequest(new { title = "Validation du relais requise." });
            try { return Results.Ok(await licenses.RelayApproveAsync(input.Revision, ct)); }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException
                or TaskCanceledException)
            {
                return Results.Problem("Approbation du relais non confirmée.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireAuthorization(Permissions.SettingsWrite);
    }
}
