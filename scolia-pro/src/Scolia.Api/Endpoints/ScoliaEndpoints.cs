using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Scolia.Api.Security;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Application.Services;
using Scolia.Domain.Common;
using Scolia.Domain.Security;
using Scolia.Domain.Students;
using Scolia.Infrastructure.Backup;
using Scolia.Infrastructure.Import;
using Scolia.Infrastructure.Persistence;
using Scolia.Infrastructure.Documents;

namespace Scolia.Api.Endpoints;

public static class ScoliaEndpoints
{
    private const string Pdf = "application/pdf";
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static IResult Table(TableDocument table, string? format, string baseName, ReportService reports) =>
        string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase)
            ? Results.File(reports.Excel(table), Xlsx, $"{baseName}.xlsx")
            : new InlineFileResult(reports.Pdf(table), Pdf, $"{baseName}.pdf");

    // Les mots de passe des archives et les bases téléchargées ne doivent jamais
    // transiter en clair depuis un autre appareil du réseau local.
    private static bool TrustedBackupTransport(HttpContext http) =>
        http.Request.IsHttps || IPAddress.IsLoopback(http.Connection.RemoteIpAddress ?? IPAddress.None);

    private static string PublicBase(HttpContext http, IConfiguration config) =>
        config["Scolia:UrlPublique"] is { Length: > 0 } configured ? configured : $"{http.Request.Scheme}://{http.Request.Host}";

    public static void MapScoliaEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/sante", (CancellationToken ct) => Results.Ok(new { statut = "ok", version = typeof(ScoliaEndpoints).Assembly.GetName().Version?.ToString(), cardGenerator = DocumentService.CardGeneratorId, heure = DateTime.Now }));

        // ---------- Installation et session ----------
        api.MapGet("/installation", (SetupService setup, CancellationToken ct) => setup.StatusAsync(ct));
        // Le logo figure sur l'écran de connexion et les pages publiques de vérification.
        api.MapGet("/etablissement/logo", async (SchoolService s, CancellationToken ct) => await s.GetLogoAsync(ct) is { } logo ? Results.File(logo.Content, logo.Type)
            : Results.NotFound());
        api.MapGet("/installation/annee-proposee", (IClock clock) => Results.Ok(new { label = SetupService.DefaultYearLabel(clock.Today) }));
        // Préremplir l'identifiant sans publier la liste des comptes sur le réseau.
        // Seul un client sur le même appareil que le serveur peut lire cette suggestion.
        api.MapGet("/installation/compte-local", async (HttpContext http, ScoliaDbContext db, CancellationToken ct) =>
        {
            if (!IPAddress.IsLoopback(http.Connection.RemoteIpAddress ?? IPAddress.None)) return Results.NotFound();
            var userName = await db.Users.AsNoTracking().Where(u => u.IsActive)
                .OrderByDescending(u => u.LastLoginUtc).ThenBy(u => u.UserName)
                .Select(u => u.UserName).FirstOrDefaultAsync(ct);
            return Results.Ok(new { userName });
        });
        api.MapPost("/installation", async (SetupRequest input, SetupService setup, ScoliaDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var admin = await setup.InitializeAsync(input, ct);
            await SessionFactory.SignInAsync(http, admin);
            return Results.Ok(await SessionFactory.DescribeAsync(db, admin, ct));
        }).RequireRateLimiting("connexion");

        api.MapPost("/session", async (LoginRequest input, UserService users, ScoliaDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var user = await users.LoginAsync(input.UserName, input.Password, ct);
            await SessionFactory.SignInAsync(http, user);
            return Results.Ok(await SessionFactory.DescribeAsync(db, user, ct));
        }).RequireRateLimiting("connexion");
        // Restauration depuis la connexion : code confidentiel détenu par le responsable du serveur.
        api.MapPost("/session/recuperation", async Task<IResult> (RecoveryInput input, RecoveryKeyStore codes,
            UserService users, CancellationToken ct) =>
        {
            if (input.NewPassword != input.Confirmation)
                return Results.BadRequest(new { title = "Les mots de passe ne correspondent pas." });
            var accepted = await codes.ValidateAndRotateAsync(input.RecoveryCode,
                () => users.RecoverPasswordAsync(input.UserName, input.NewPassword, ct), ct);
            return accepted ? Results.NoContent() : Results.Unauthorized();
        }).RequireRateLimiting("connexion");
        api.MapGet("/session", async (ICurrentUser current, ScoliaDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == current.Id, ct);
            return user is null ? Results.Unauthorized() : Results.Ok(await SessionFactory.DescribeAsync(db, user, ct));
        }).RequireAuthorization();
        api.MapDelete("/session", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
        api.MapPost("/session/mot-de-passe", async (PasswordChange input, UserService users, ICurrentUser current, ScoliaDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var user = await users.ChangeOwnPasswordAsync(current.Id!.Value, input, ct);
            await SessionFactory.SignInAsync(http, user);
            return Results.Ok(await SessionFactory.DescribeAsync(db, user, ct));
        }).RequireAuthorization();

        // ---------- Invitation d'un appareil : preuve à usage unique, pas le mot de passe du serveur ----------
        api.MapPost("/jumelage/inviter", async Task<IResult> (HttpContext http, ICurrentUser current,
            ScoliaDbContext db, DevicePairingRegistry registry, IHostEnvironment host, CancellationToken ct) =>
        {
            // L'invitation n'autorise que l'appareil et le serveur : jamais les droits du responsable.
            if (!IPAddress.IsLoopback(http.Connection.RemoteIpAddress ?? IPAddress.None)
                && !(host.IsEnvironment("Testing") && http.Connection.RemoteIpAddress is null))
                return Results.Forbid();
            var owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == current.Id && u.IsActive, ct);
            if (owner is null) return Results.Unauthorized();
            var invitation = registry.Invite(owner.Id, owner.SecurityStamp);
            return Results.Ok(new PairingInvitation(invitation.Token, invitation.ExpiresUtc));
        }).RequireAuthorization(Permissions.UsersManage).RequireRateLimiting("connexion");

        api.MapPost("/jumelage/accepter", async Task<IResult> (PairingAccept request, HttpContext http,
            DevicePairingRegistry registry, ScoliaDbContext db, CancellationToken ct) =>
        {
            if (request.DeviceId != LiveHeaders.DeviceId(http)) return Results.Unauthorized();
            var accepted = registry.Redeem(request.InvitationToken, request.DeviceId, request.DeviceName);
            if (accepted is null) return Results.Unauthorized();
            var a = accepted.Value;
            var account = await db.Users.AsNoTracking().FirstOrDefaultAsync(
                u => u.Id == a.Owner && u.IsActive && u.SecurityStamp == a.Stamp, ct);
            if (account is null) { registry.Revoke(a.GrantId, a.Owner); return Results.Unauthorized(); }
            // AUCUN cookie de session émis ici. Chaque utilisateur doit fournir
            // son identifiant et son mot de passe sur l'écran de connexion.
            var school = await db.Schools.AsNoTracking().OrderBy(s => s.CreatedAtUtc)
                .Select(s => s.Name).FirstOrDefaultAsync(ct);
            return Results.Ok(new PairingAccepted(a.GrantId, a.Secret, a.ExpiresUtc,
                school ?? "SCOLIA"));
        }).RequireRateLimiting("connexion");

        // Découverte UDP seule = non fiable ; ce défi permet de reconnaître
        // le serveur autorisé si son adresse change, sans exposer le secret client.
        api.MapPost("/jumelage/preuve-serveur", (DeviceProofRequest request, HttpContext http,
            DevicePairingRegistry registry) =>
        {
            if (request.DeviceId != LiveHeaders.DeviceId(http)) return Results.Unauthorized();
            var proof = registry.ProveIdentity(request.GrantId, request.DeviceId, request.Nonce);
            return proof is null ? Results.Unauthorized() : Results.Ok(new DeviceProofReply(proof));
        }).RequireRateLimiting("connexion");

        // Les anciennes versions pouvaient transformer un QR en session administrateur.
        // Fermer explicitement ce chemin : une clé d'appareil n'est PAS un mot de passe.
        api.MapPost("/session/appareil", () => Results.Unauthorized())
            .RequireRateLimiting("connexion");
        api.MapPost("/session/appareil/renouveler", () => Results.Unauthorized())
            .RequireRateLimiting("connexion");

        // Reconnaître le poste déjà lié sans ouvrir de session utilisateur.
        // La preuve reste limitée à ce poste ; elle sert à la reconnexion LAN/WSS.
        api.MapPost("/jumelage/appareil/verifier", (DeviceLoginRequest request, HttpContext http,
            DevicePairingRegistry registry) =>
        {
            if (request.DeviceId != LiveHeaders.DeviceId(http)) return Results.Unauthorized();
            var linked = registry.AuthorizeAndRefresh(request.GrantId, request.DeviceId, request.Secret);
            return linked is null ? Results.Unauthorized()
                : Results.Ok(new DeviceLinkVerified(linked.Value.ExpiresUtc));
        }).RequireRateLimiting("connexion");

        // Première connexion du Promoteur : mémoriser uniquement l'appareil
        // authentifié et son compte en lecture seule, sans privilèges administratifs.
        api.MapPost("/jumelage/mon-appareil", async Task<IResult> (HttpContext http,
            ICurrentUser current, DevicePairingRegistry registry, ScoliaDbContext db,
            CancellationToken ct) =>
        {
            var deviceId = LiveHeaders.DeviceId(http);
            if (string.IsNullOrWhiteSpace(deviceId)) return Results.Unauthorized();
            var account = await db.Users.AsNoTracking().FirstOrDefaultAsync(
                u => u.Id == current.Id && u.IsActive, ct);
            if (account is null) return Results.Unauthorized();
            if (account.Role != Role.Promoteur) return Results.Forbid();
            var issued = registry.EnrollOwnDevice(account.Id, account.SecurityStamp,
                deviceId, LiveHeaders.Device(http));
            return issued is null ? Results.BadRequest()
                : Results.Ok(new DeviceSelfRegistration(issued.Value.GrantId,
                    issued.Value.Secret, issued.Value.ExpiresUtc));
        }).RequireAuthorization(Permissions.PilotageRead).RequireRateLimiting("connexion");

        api.MapGet("/jumelage/appareils", (DevicePairingRegistry registry) =>
            Results.Ok(registry.ListAll())).RequireAuthorization(Permissions.UsersManage);

        api.MapPost("/jumelage/appareils/{grantId:guid}/revoquer", (Guid grantId,
            DevicePairingRegistry registry) =>
            registry.RevokeAny(grantId) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(Permissions.UsersManage);

        // ---------- Vérification publique des reçus (QR code) ----------
        api.MapGet("/verifier/{code}", (string code, PaymentService payments, CancellationToken ct) => payments.VerifyAsync(code, ct)).RequireRateLimiting("connexion");
        // Vérification publique des bulletins de délibération officiels (QR code) : nom masqué, résultat figé.
        api.MapGet("/verifier-bulletin/{code}", (string code, DeliberationService s, CancellationToken ct) => s.VerifyBulletinAsync(code, ct)).RequireRateLimiting("connexion");

        var secured = api.MapGroup("").RequireAuthorization();
        secured.MapGet("/recherche", (string? q, SearchService search, CancellationToken ct) => search.SearchAsync(q, ct)).RequireAuthorization(Permissions.StudentsRead);
        // Révision des données : les postes la comparent pour recharger l'écran après une écriture faite ailleurs.
        secured.MapGet("/revision", (DataRevision revision) => Results.Ok(new { revision = revision.Value, epoch = revision.Epoch }));

        MapCards(secured);
        MapSchool(secured);
        MapStudents(secured);
        MapFinance(secured);
        MapReports(secured);
        // Endpoint de pilotage séparé : aucun rapport nominatif ni fichier téléchargeable avec ce droit.
        secured.MapGet("/pilotage/synthese", (ReportService reports, CancellationToken ct) =>
            reports.PromoterDashboardAsync(ct)).RequireAuthorization(Permissions.PilotageRead);
        MapAcademics(secured);
        MapDeliberation(secured);
        MapStaff(secured);
        MapAdministration(secured);
    }

    private static void MapSchool(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/etablissement");
        g.MapGet("", (SchoolService s, CancellationToken ct) => s.GetSchoolAsync(ct));
        g.MapPut("", (SchoolUpdate input, SchoolService s, CancellationToken ct) => s.UpdateSchoolAsync(input, ct)).RequireAuthorization(Permissions.SettingsWrite);
        g.MapPut("/logo", async (IFormFile? fichier, SchoolService s, CancellationToken ct) =>
        {
            if (fichier is null) { await s.SetLogoAsync(null, null, ct); return Results.NoContent(); }
            using var ms = new MemoryStream();
            await fichier.CopyToAsync(ms, ct);
            await s.SetLogoAsync(ms.ToArray(), fichier.ContentType, ct);
            return Results.NoContent();
        }).RequireAuthorization(Permissions.SettingsWrite).DisableAntiforgery();

        g.MapGet("/annees", (SchoolService s, CancellationToken ct) => s.YearsAsync(ct));
        g.MapPost("/annees", (AcademicYearCreate input, SchoolService s, CancellationToken ct) => s.CreateYearAsync(input, ct)).RequireAuthorization(Permissions.SettingsWrite);
        g.MapPut("/annees/{id:guid}/calendrier", (Guid id, AcademicYearScheduleUpdate input, SchoolService s, CancellationToken ct) =>
            s.UpdateYearScheduleAsync(id, input, ct)).RequireAuthorization(Permissions.SettingsWrite);
        g.MapPost("/annees/{id:guid}/courante", async (Guid id, SchoolService s, CancellationToken ct) => { await s.SetCurrentYearAsync(id, ct); return Results.NoContent(); })
            .RequireAuthorization(Permissions.SettingsWrite);
        g.MapPost("/annees/{id:guid}/cloture", async (Guid id, SchoolService s, BackupService backups, CancellationToken ct) =>
        {
            // La clôture est volontaire : une sauvegarde complète est faite avant que l'année ne soit figée.
            await backups.CreateAsync("avantcloture", ct);
            await s.CloseYearAsync(id, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Permissions.SettingsWrite);

        // Même administration / établissement : les cycles sont un sous-module
        // de configuration protégé, sans accès à la caisse ou aux données ITM.
        g.MapGet("/cycles", (SchoolCycleConfigurationService s, CancellationToken ct) =>
            s.GetAsync(ct)).RequireAuthorization(Permissions.SettingsWrite);
        g.MapPut("/cycles", (SchoolCycleConfigurationInput input, SchoolCycleConfigurationService s,
            CancellationToken ct) => s.ConfigureAsync(input, ct))
            .RequireAuthorization(Permissions.SettingsWrite);

        // Registre scolaire distinct de l'ITM : aucun seuil, moyenne ou décision
        // inventés dans un aperçu. L'écriture est interdite aux profils non activés.
        g.MapGet("/cycles/evaluations/{enrollmentId:guid}/apercu",
            (Guid enrollmentId, SchoolCycleAssessmentService s, CancellationToken ct) =>
                s.PreviewAsync(enrollmentId, ct)).RequireAuthorization(Permissions.AcademicsRead);
        g.MapPost("/cycles/evaluations/{enrollmentId:guid}",
            (Guid enrollmentId, SchoolCycleAssessmentInput input, SchoolCycleAssessmentService s, CancellationToken ct) =>
                s.SaveAsync(enrollmentId, input, ct)).RequireAuthorization(Permissions.AcademicsWrite);
        // Document de consultation distinct du bulletin officiel et des
        // délibérations ITM. Les droits sont ceux de la lecture pédagogique.
        g.MapGet("/cycles/evaluations/{enrollmentId:guid}/apercu.pdf",
            async (Guid enrollmentId, SchoolCycleAssessmentService s,
                HttpContext http, CancellationToken ct) =>
            {
                // Document nominatif d'un mineur : interdit aux caches partagés.
                http.Response.Headers.CacheControl = "private, no-store, max-age=0";
                http.Response.Headers.Pragma = "no-cache";
                var preview = await s.PrintPreviewAsync(enrollmentId, ct);
                return new InlineFileResult(
                    SchoolCycleBulletinPdf.Build(preview),
                    Pdf, "apercu-pedagogique-non-officiel.pdf");
            })
            .RequireAuthorization(Permissions.AcademicsRead);


        g.MapGet("/sections", (SchoolService s, CancellationToken ct) => s.SectionsAsync(ct));
        g.MapPost("/sections", (SectionUpsert input, SchoolService s, CancellationToken ct) => s.UpsertSectionAsync(null, input, ct)).RequireAuthorization(Permissions.SettingsWrite);
        g.MapPut("/sections/{id:guid}", (Guid id, SectionUpsert input, SchoolService s, CancellationToken ct) => s.UpsertSectionAsync(id, input, ct)).RequireAuthorization(Permissions.SettingsWrite);
        g.MapDelete("/sections/{id:guid}", async (Guid id, SchoolService s, CancellationToken ct) => { await s.DeleteSectionAsync(id, ct); return Results.NoContent(); })
            .RequireAuthorization(Permissions.SettingsWrite);

        g.MapGet("/categories-frais", (FeeCategoryService s, CancellationToken ct) => s.ListAsync(ct));
        g.MapPost("/categories-frais", (FeeCategoryUpsert input, FeeCategoryService s, CancellationToken ct) => s.UpsertAsync(null, input, ct))
            .RequireAuthorization(Permissions.SettingsWrite);
        g.MapPut("/categories-frais/{id:guid}", (Guid id, FeeCategoryUpsert input, FeeCategoryService s, CancellationToken ct) => s.UpsertAsync(id, input, ct))
            .RequireAuthorization(Permissions.SettingsWrite);
        g.MapDelete("/categories-frais/{id:guid}", async (Guid id, FeeCategoryService s, CancellationToken ct) => { await s.DeleteAsync(id, ct); return Results.NoContent(); })
            .RequireAuthorization(Permissions.SettingsWrite);

        g.MapGet("/promotions", (Guid? anneeId, SchoolService s, CancellationToken ct) => s.PromotionsAsync(anneeId, ct));
        g.MapPost("/promotions", (PromotionUpsert input, SchoolService s, CancellationToken ct) => s.UpsertPromotionAsync(null, input, ct)).RequireAuthorization(Permissions.SettingsWrite);
        g.MapPut("/promotions/{id:guid}", (Guid id, PromotionUpsert input, SchoolService s, CancellationToken ct) => s.UpsertPromotionAsync(id, input, ct)).RequireAuthorization(Permissions.SettingsWrite);
        g.MapDelete("/promotions/{id:guid}", async (Guid id, SchoolService s, CancellationToken ct) => { await s.DeletePromotionAsync(id, ct); return Results.NoContent(); })
            .RequireAuthorization(Permissions.SettingsWrite);
    }

    private static void MapStudents(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/apprenants").RequireAuthorization(Permissions.StudentsRead);
        g.MapGet("", (string? q, Guid? promotionId, string? statut, Guid? anneeId, int? page, int? taille, Guid? sectionId, StudentService s, CancellationToken ct) =>
            s.SearchAsync(q, promotionId, statut, anneeId, page ?? 1, taille ?? 50, sectionId, ct));
        g.MapGet("/{id:guid}", (Guid id, StudentService s, CancellationToken ct) => s.GetAsync(id, ct));
        g.MapGet("/doublons", (string? nom, string? postNom, string? prenom, Guid? exclure, StudentService s, CancellationToken ct) =>
            s.FindDuplicatesAsync(nom ?? string.Empty, postNom ?? string.Empty, prenom ?? string.Empty, exclure, ct));
        g.MapGet("/doublons/groupes", (StudentService s, CancellationToken ct) => s.DuplicateGroupsAsync(ct));
        g.MapPost("", (StudentCreate input, StudentService s, CancellationToken ct) => s.CreateAsync(input, ct)).RequireAuthorization(Permissions.StudentsWrite);
        g.MapPut("/{id:guid}", (Guid id, StudentUpsert input, StudentService s, CancellationToken ct) => s.UpdateAsync(id, input, ct)).RequireAuthorization(Permissions.StudentsWrite);
        g.MapDelete("/{id:guid}", async (Guid id, StudentService s, CancellationToken ct) => { await s.DeleteAsync(id, ct); return Results.NoContent(); })
            .RequireAuthorization(Permissions.SettingsWrite);
        g.MapGet("/{id:guid}/photo", async (Guid id, StudentService s, CancellationToken ct) => await s.GetPhotoAsync(id, ct) is { Length: > 0 } photo ? Results.File(photo, "image/jpeg") : Results.NotFound());
        g.MapPut("/{id:guid}/photo", async (Guid id, IFormFile? fichier, StudentService s, CancellationToken ct) =>
        {
            byte[]? content = null;
            if (fichier is not null)
            {
                using var ms = new MemoryStream();
                await fichier.CopyToAsync(ms, ct);
                content = ms.ToArray();
                // Type lu dans le fichier, pas dans l'en-tête de l'envoi (souvent absent depuis les applications).
                if (ImageKind.Detect(content) is not ("image/jpeg" or "image/png" or "image/webp"))
                    throw new DomainException("photo", "La photo doit être au format JPEG, PNG ou WebP.");
            }
            await s.SetPhotoAsync(id, content, ct);
            return Results.NoContent();
        }).RequireAuthorization(Permissions.StudentsWrite).DisableAntiforgery();
        g.MapPost("/{id:guid}/inscriptions", (Guid id, EnrollRequest input, StudentService s, CancellationToken ct) => s.EnrollAsync(id, input, ct)).RequireAuthorization(Permissions.StudentsWrite);

        var e = api.MapGroup("/inscriptions").RequireAuthorization(Permissions.StudentsRead);
        e.MapPut("/{id:guid}/statut", async (Guid id, StatusChange input, StudentService s, CancellationToken ct) => { await s.ChangeStatusAsync(id, input, ct); return Results.NoContent(); })
            .RequireAuthorization(Permissions.StudentsWrite);
        e.MapPut("/{id:guid}/promotion", async (Guid id, [FromBody] EnrollRequest input, StudentService s, CancellationToken ct) => { await s.ChangePromotionAsync(id, input.PromotionId, ct); return Results.NoContent(); })
            .RequireAuthorization(Permissions.StudentsWrite);
        e.MapGet("/{id:guid}/compte", (Guid id, AccountService a, CancellationToken ct) => a.AccountDtoAsync(id, ct)).RequireAuthorization(Permissions.PaymentsRead);
        e.MapGet("/{id:guid}/releve.pdf", async (Guid id, PaymentService p, CancellationToken ct) => new InlineFileResult(await p.StatementPdfAsync(id, ct), Pdf, "releve.pdf"))
            .RequireAuthorization(Permissions.PaymentsRead);
        e.MapGet("/{id:guid}/prises-en-charge", (Guid id, SponsorshipService s, CancellationToken ct) => s.ListAsync(id, ct)).RequireAuthorization(Permissions.PaymentsRead);
        e.MapPost("/{id:guid}/prises-en-charge", (Guid id, SponsorshipUpsert input, SponsorshipService s, CancellationToken ct) => s.UpsertAsync(id, null, input, ct))
            .RequireAuthorization(Permissions.StudentsWrite);
        e.MapPut("/{id:guid}/prises-en-charge/{sid:guid}", (Guid id, Guid sid, SponsorshipUpsert input, SponsorshipService s, CancellationToken ct) => s.UpsertAsync(id, sid, input, ct))
            .RequireAuthorization(Permissions.StudentsWrite);
        e.MapPost("/{id:guid}/prises-en-charge/{sid:guid}/fin", async (Guid id, Guid sid, CancelRequest input, SponsorshipService s, CancellationToken ct) =>
        {
            await s.EndAsync(id, sid, input.Reason, ct);
            return Results.NoContent();
        }).RequireAuthorization(Permissions.StudentsWrite);

        api.MapGet("/prises-en-charge", (SponsorshipService s, CancellationToken ct) => s.AllAsync(ct)).RequireAuthorization(Permissions.PaymentsRead);
        api.MapGet("/reinscriptions/plan", (Guid anneeId, StudentService s, CancellationToken ct) => s.ReEnrollmentPlanAsync(anneeId, ct)).RequireAuthorization(Permissions.StudentsWrite);
        api.MapPost("/reinscriptions", (ReEnrollmentRequest input, StudentService s, CancellationToken ct) => s.ReEnrollAsync(input, ct)).RequireAuthorization(Permissions.StudentsWrite);
    }

    private static void MapFinance(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/paiements").RequireAuthorization(Permissions.PaymentsRead);
        g.MapGet("", ([AsParameters] ReceiptQuery q, PaymentService p, CancellationToken ct) => p.ListAsync(q, ct));
        g.MapGet("/export", async ([AsParameters] ReceiptQuery q, string? format, PaymentService p, ReportService r, CancellationToken ct) =>
            Table(await r.ReceiptsTableAsync(p, q, ct), format, "journal-encaissements", r));
        g.MapPost("/apercu", (PaymentRequest input, PaymentService p, CancellationToken ct) => p.PreviewAsync(input.EnrollmentId, input.FeeItemIds, input.Amount == 0m ? null : input.Amount, ct))
            .RequireAuthorization(Permissions.PaymentsCreate);
        g.MapPost("", (PaymentRequest input, PaymentService p, CancellationToken ct) => p.CreateAsync(input, ct)).RequireAuthorization(Permissions.PaymentsCreate);
        // Encaissement par lot : un reçu par apprenant, contrôle avant écriture, résultat ligne par ligne.
        g.MapGet("/lot/candidats", (Guid? promotionId, string? q, PaymentBatchService b, CancellationToken ct) => b.CandidatesAsync(promotionId, q, ct));
        g.MapPost("/lot/apercu", (BatchPaymentRequest input, PaymentBatchService b, CancellationToken ct) => b.PreviewAsync(input, ct))
            .RequireAuthorization(Permissions.PaymentsCreate);
        g.MapPost("/lot", (BatchPaymentRequest input, PaymentBatchService b, CancellationToken ct) => b.RecordAsync(input, ct))
            .RequireAuthorization(Permissions.PaymentsCreate);
        g.MapGet("/{id:guid}", (Guid id, PaymentService p, CancellationToken ct) => p.GetAsync(id, ct));
        g.MapPost("/{id:guid}/annulation", (Guid id, CancelRequest input, PaymentService p, CancellationToken ct) => p.CancelAsync(id, input.Reason, ct))
            .RequireAuthorization(Permissions.PaymentsCancel);
        g.MapGet("/{id:guid}/recu.pdf", async (Guid id, PaymentService p, HttpContext http, IConfiguration config, CancellationToken ct) =>
            new InlineFileResult(await p.ReceiptPdfAsync(id, PublicBase(http, config), ct), Pdf, "recu.pdf"));

        var d = api.MapGroup("/depenses").RequireAuthorization(Permissions.PaymentsRead);
        d.MapGet("", (DateOnly? du, DateOnly? au, string? categorie, string? q, int? page, int? taille, CashService c, CancellationToken ct) =>
            c.ExpensesAsync(du, au, categorie, q, page ?? 1, taille ?? 50, ct));
        d.MapGet("/categories", (CashService c, CancellationToken ct) => c.CategoriesAsync(ct));
        d.MapGet("/export", async (DateOnly? du, DateOnly? au, string? format, ReportService r, CancellationToken ct) =>
            Table(await r.ExpensesTableAsync(du, au, ct), format, "registre-des-depenses", r));
        d.MapPost("", (ExpenseCreate input, CashService c, CancellationToken ct) => c.CreateExpenseAsync(input, ct)).RequireAuthorization(Permissions.ExpensesWrite);
        d.MapPost("/{id:guid}/annulation", (Guid id, CancelRequest input, CashService c, CancellationToken ct) => c.CancelExpenseAsync(id, input.Reason, ct))
            .RequireAuthorization(Permissions.PaymentsCancel); // Annuler n'est pas saisir : même droit que l'annulation d'un reçu.

        var k = api.MapGroup("/caisse").RequireAuthorization(Permissions.PaymentsRead);
        k.MapGet("/situation", (DateOnly? date, CashService c, CancellationToken ct) => c.SituationAsync(date, ct));
        k.MapGet("/arretes", (CashService c, CancellationToken ct) => c.ClosingsAsync(ct));
        k.MapPost("/arretes", (CashClosingRequest input, CashService c, CancellationToken ct) => c.CloseAsync(input, ct)).RequireAuthorization(Permissions.CashClose);
    }

    private static void MapReports(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/rapports").RequireAuthorization(Permissions.ReportsRead);
        g.MapGet("/tableau-de-bord", (Guid? promotionId, ReportService r, CancellationToken ct) => r.DashboardAsync(promotionId, ct));
        g.MapGet("/aujourdhui", (ReportService r, CancellationToken ct) => r.TodayAsync(ct));
        g.MapGet("/debiteurs", ([AsParameters] DebtorQuery q, ReportService r, CancellationToken ct) => r.DebtorsAsync(q, ct));
        g.MapGet("/debiteurs/export", async ([AsParameters] DebtorQuery q, string? format, ReportService r, CancellationToken ct) =>
            Table(await r.DebtorsTableAsync(q, ct), format, "retards-de-paiement", r));
        g.MapGet("/suivi-mensuel", ([AsParameters] MonthlyQuery q, ReportService r, CancellationToken ct) => r.MonthlyAsync(q, ct));
        g.MapGet("/suivi-mensuel/export", async ([AsParameters] MonthlyQuery q, string? format, ReportService r, CancellationToken ct) =>
            Table(await r.MonthlyTableAsync(q, ct), format, "suivi-mensuel-paiements", r));
        g.MapGet("/promotions/export", async (string? format, ReportService r, CancellationToken ct) =>
            Table(await r.PromotionsTableAsync(ct), format, "situation-par-promotion", r));
        g.MapGet("/apprenants/export", async (string? format, ReportService r, CancellationToken ct) =>
            Table(await r.StudentsTableAsync(ct), format, "liste-generale-apprenants", r));
        g.MapGet("/liste-classe/{promotionId:guid}", async (Guid promotionId, string? format, ReportService r, CancellationToken ct) =>
            Table(await r.ClassListAsync(promotionId, ct), format, "liste-nominative", r));

        // ---------- Rapport journalier : PDF du jour, envoi automatique à l'heure choisie ----------
        g.MapGet("/journalier", async (DateOnly? date, DailyReportService r, IClock clock, CancellationToken ct) =>
        {
            var day = date ?? clock.Today;
            var (pdf, _) = await r.BuildAsync(day, ct);
            return Results.File(pdf, "application/pdf", DailyReportService.FileName(day));
        });
        var daily = g.MapGroup("/journalier").RequireAuthorization(Permissions.SettingsWrite);
        daily.MapGet("/reglages", (Scolia.Infrastructure.Reports.DailyReportStore store) => DailyReportView(store.Load()));
        daily.MapPut("/reglages", async (DailyReportInput input, Scolia.Infrastructure.Reports.DailyReportStore store, IAuditLog log, ScoliaDbContext db, CancellationToken ct) =>
        {
            if (!Scolia.Infrastructure.Reports.DailyReportSettings.TryTime(input.Time, out var at))
                throw new DomainException("heure", "Heure invalide : choisissez par exemple 18:00.");
            var numbers = (input.Recipients ?? []).Select(n => n.Trim()).Where(n => n.Length > 0).Distinct().ToList();
            foreach (var n in numbers)
                if (n.Count(char.IsDigit) is < 9 or > 15) throw new DomainException("numeros", $"Numéro invalide : {n}. Exemple : 0812345678 ou +243812345678.");
            var emails = (input.EmailRecipients ?? []).Select(e => e.Trim()).Where(e => e.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var e in emails)
                if (!System.Net.Mail.MailAddress.TryCreate(e, out var parsed) || parsed.Address != e || !e.Contains('.'))
                    throw new DomainException("emails", $"Adresse e-mail invalide : {e}. Exemple : direction@ecole.cd");
            if (input.Enabled && numbers.Count == 0 && emails.Count == 0)
                throw new DomainException("numeros", "Ajoutez au moins un numéro WhatsApp ou une adresse e-mail qui recevra le rapport.");
            if (!Scolia.Infrastructure.Reports.WhatsAppCloudSender.IsValidVersion(input.WhatsAppApiVersion))
                throw new DomainException("version", $"Version de l'API WhatsApp invalide : {input.WhatsAppApiVersion}. Exemple : v24.0, ou laissez vide.");
            if (input.SmtpPort is { } port && port is < 1 or > 65535)
                throw new DomainException("smtpPort", "Port de messagerie invalide : 587 en général.");
            if (!string.IsNullOrWhiteSpace(input.SmtpFrom) && !System.Net.Mail.MailAddress.TryCreate(input.SmtpFrom.Trim(), out _))
                throw new DomainException("smtpFrom", $"Adresse d'envoi invalide : {input.SmtpFrom}.");
            store.Update(s =>
            {
                s.Enabled = input.Enabled;
                s.Time = at.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                s.Recipients = numbers;
                s.WhatsAppPhoneNumberId = string.IsNullOrWhiteSpace(input.WhatsAppPhoneNumberId) ? null : input.WhatsAppPhoneNumberId.Trim();
                s.WhatsAppTemplate = string.IsNullOrWhiteSpace(input.WhatsAppTemplate) ? null : input.WhatsAppTemplate.Trim();
                if (!string.IsNullOrWhiteSpace(input.WhatsAppLanguage)) s.WhatsAppLanguage = input.WhatsAppLanguage.Trim();
                s.WhatsAppApiVersion = string.IsNullOrWhiteSpace(input.WhatsAppApiVersion) ? null : input.WhatsAppApiVersion.Trim();
                // Le jeton ne revient jamais à l'écran : vide le garde, « effacer » le retire.
                if (input.ClearToken) s.WhatsAppToken = null;
                else if (!string.IsNullOrWhiteSpace(input.WhatsAppToken)) s.WhatsAppToken = input.WhatsAppToken.Trim();
                // Messagerie : même règle pour le mot de passe, jamais renvoyé à l'écran.
                s.EmailRecipients = emails;
                s.SmtpHost = string.IsNullOrWhiteSpace(input.SmtpHost) ? null : input.SmtpHost.Trim();
                s.SmtpPort = input.SmtpPort ?? 587;
                s.SmtpSsl = input.SmtpSsl ?? true;
                s.SmtpUser = string.IsNullOrWhiteSpace(input.SmtpUser) ? null : input.SmtpUser.Trim();
                s.SmtpFrom = string.IsNullOrWhiteSpace(input.SmtpFrom) ? null : input.SmtpFrom.Trim();
                if (input.ClearSmtpPassword) s.SmtpPassword = null;
                else if (!string.IsNullOrEmpty(input.SmtpPassword)) s.SmtpPassword = input.SmtpPassword;
                // Réglage changé : le rapport du jour part à la nouvelle heure, sans renvoyer ceux des jours passés.
                s.LastDate = DateOnly.FromDateTime(DateTime.Now).AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            });
            log.Record("rapport.journalier.reglages", "RapportJournalier", null,
                input.Enabled ? $"Rapport journalier programmé à {at.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)} pour {numbers.Count} numéro(s) et {emails.Count} adresse(s) e-mail." : "Rapport journalier automatique désactivé.");
            await db.SaveChangesAsync(ct);
            return DailyReportView(store.Load());
        });
        daily.MapPost("/envoyer", async (DateOnly? date, Scolia.Infrastructure.Reports.DailyReportRunner runner, IClock clock, CancellationToken ct) =>
        {
            var run = await runner.RunAsync(date ?? clock.Today, send: true, "manuel", ct);
            return new DailyReportResult(run.FileName, run.Status, run.Summary.Text);
        });
    }

    private static DailyReportViewDto DailyReportView(Scolia.Infrastructure.Reports.DailyReportSettings s) =>
        new(s.Enabled, s.Time, s.Recipients, s.WhatsAppPhoneNumberId, s.WhatsAppTemplate, s.WhatsAppLanguage,
            !string.IsNullOrWhiteSpace(s.WhatsAppToken), s.WhatsAppReady, s.LastStatus,
            s.EmailRecipients, s.SmtpHost, s.SmtpPort, s.SmtpSsl, s.SmtpUser, s.SmtpFrom, !string.IsNullOrEmpty(s.SmtpPassword), s.EmailReady,
            s.WhatsAppApiVersion);

    public sealed record DailyReportInput(bool Enabled, string? Time, List<string>? Recipients, string? WhatsAppPhoneNumberId,
        string? WhatsAppToken, bool ClearToken, string? WhatsAppTemplate, string? WhatsAppLanguage,
        List<string>? EmailRecipients = null, string? SmtpHost = null, int? SmtpPort = null, bool? SmtpSsl = null,
        string? SmtpUser = null, string? SmtpPassword = null, bool ClearSmtpPassword = false, string? SmtpFrom = null,
        string? WhatsAppApiVersion = null);
    public sealed record DailyReportViewDto(bool Enabled, string Time, List<string> Recipients, string? WhatsAppPhoneNumberId,
        string? WhatsAppTemplate, string WhatsAppLanguage, bool HasToken, bool WhatsAppReady, string? LastStatus,
        List<string> EmailRecipients, string? SmtpHost, int SmtpPort, bool SmtpSsl, string? SmtpUser, string? SmtpFrom,
        bool HasSmtpPassword, bool EmailReady, string? WhatsAppApiVersion);
    public sealed record DailyReportResult(string FileName, string Status, string Summary);

    private static void MapCards(RouteGroupBuilder api)
    {
        // Cartes d'accès aux examens (V79 : Scolarité > Cartes). Export réservé aux comptes qui peuvent écrire les apprenants.
        var g = api.MapGroup("/cartes").RequireAuthorization(Permissions.StudentsRead);
        g.MapGet("/candidats", (Guid? promotionId, string? recherche, bool? sortis, CardService s, CancellationToken ct) =>
            s.CandidatesAsync(promotionId, recherche, sortis ?? false, ct));
        g.MapPost("/export", async (CardExportRequest input, CardService s, HttpContext http, CancellationToken ct) =>
        {
            var result = await s.ExportAsync(input, ct);
            http.Response.Headers["X-Cartes-Ecartees"] = result.Skipped.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var word = result.FileName.EndsWith(".docx", StringComparison.Ordinal);
            return Results.File(result.Bytes, word ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document" : "application/pdf", result.FileName);
        }).RequireAuthorization(Permissions.StudentsWrite);
        g.MapGet("/lots", (CardService s, CancellationToken ct) => s.BatchesAsync(ct));
    }

    /// <summary>Délibération (fusion d'ITMCD) : codes anonymes, encodage des cotes, jury, clôture et documents officiels.</summary>
    private static void MapDeliberation(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/deliberation").RequireAuthorization(Permissions.AcademicsRead);
        g.MapPut("/reglages", (DeliberationSettingsDto input, DeliberationService s, CancellationToken ct) => s.UpdateSettingsAsync(input, ct))
            .RequireAuthorization(Permissions.DeliberationManage);

        // Autorités et épreuves du jury (projet Délibération).
        g.MapGet("/autorites", (DeliberationService s, CancellationToken ct) => s.AuthoritiesAsync(ct));
        g.MapPut("/autorites", (AuthoritiesDto input, DeliberationService s, CancellationToken ct) => s.UpdateAuthoritiesAsync(input, ct))
            .RequireAuthorization(Permissions.DeliberationManage);
        g.MapPost("/epreuves-jury", (Guid promotionId, DeliberationService s, CancellationToken ct) => s.CreateJuryEvaluationsAsync(promotionId, ct))
            .RequireAuthorization(Permissions.DeliberationManage);

        // Codes anonymes : les apprenants déjà inscrits reçoivent un code officiel reconnu par la délibération.
        g.MapGet("/codes", (Guid? promotionId, DeliberationService s, CancellationToken ct) => s.CodesAsync(promotionId, ct)).RequireAuthorization(Permissions.DeliberationManage);
        g.MapPost("/codes/attribution", (CodeAssignmentRequest input, DeliberationService s, CancellationToken ct) => s.AssignCodesAsync(input, ct))
            .RequireAuthorization(Permissions.DeliberationManage);
        g.MapPut("/codes/{enrollmentId:guid}", (Guid enrollmentId, CodeChangeRequest input, DeliberationService s, CancellationToken ct) => s.SetCodeAsync(enrollmentId, input, ct))
            .RequireAuthorization(Permissions.DeliberationManage);
        g.MapGet("/codes/liste", async (Guid? promotionId, string? format, DeliberationService s, ReportService reports, CancellationToken ct) =>
            Table(await s.CodesTableAsync(promotionId, ct), format, "codes-anonymes", reports)).RequireAuthorization(Permissions.DeliberationManage);
        g.MapPost("/codes/import", (CodeImportRequest input, DeliberationService s, CancellationToken ct) => s.ImportCodesAsync(input, ct))
            .RequireAuthorization(Permissions.DeliberationManage);
        g.MapGet("/codes/resolution", (string code, DeliberationService s, CancellationToken ct) => s.LookupAsync(code, ct)).RequireAuthorization(Permissions.AcademicsWrite);

        // Encodage : feuille par cours, ou saisie anonyme par code.
        g.MapGet("/cours", (Guid promotionId, DeliberationService s, CancellationToken ct) => s.CoursesAsync(promotionId, ct));
        // Remise officielle des cotes (ITMCD) : feuille complète, puis seul le jury la corrige ou la renvoie avec un motif.
        g.MapPost("/renvoi", async (Guid coursId, string nature, MarkReturnRequest input, DeliberationService s, CancellationToken ct) =>
        {
            await s.ReturnAsync(coursId, nature, input, ct);
            return Results.NoContent();
        }).RequireAuthorization(Permissions.DeliberationManage);
        // Suivi de l'encodage et relevés par cours (moniteur et export d'ITMCD).
        g.MapGet("/suivi", (Guid? promotionId, string? periode, DeliberationService s, CancellationToken ct) => s.ProgressAsync(promotionId, periode, ct));
        g.MapGet("/suivi/export", async (Guid? promotionId, string? periode, string? format, DeliberationService s, ReportService reports, CancellationToken ct) =>
            Table(await s.ProgressTableAsync(promotionId, periode, ct), format, "suivi-encodage", reports));

        // Jury.
        g.MapGet("/resultats", (Guid promotionId, string periode, DeliberationService s, CancellationToken ct) => s.BoardAsync(promotionId, periode, ct));
        g.MapGet("/resultats/{enrollmentId:guid}", (Guid enrollmentId, Guid promotionId, string periode, DeliberationService s, CancellationToken ct) =>
            s.StudentAsync(promotionId, periode, enrollmentId, ct));
        g.MapPut("/decisions", (JuryDecisionRequest input, DeliberationService s, CancellationToken ct) => s.DecideAsync(input, ct)).RequireAuthorization(Permissions.DeliberationManage);
        g.MapPost("/cloture", (SessionCloseRequest input, DeliberationService s, CancellationToken ct) => s.CloseAsync(input, ct))
            .RequireAuthorization(Permissions.DeliberationManage).RequireRateLimiting("connexion");
        g.MapPost("/reouverture", (SessionReopenRequest input, DeliberationService s, CancellationToken ct) => s.ReopenAsync(input, ct))
            .RequireAuthorization(Permissions.DeliberationManage).RequireRateLimiting("connexion");

        // Feuilles de cotes : le secrétariat (scolarite.lire / scolarite.ecrire) ou l'enseignant sur ses seuls cours
        // attribués (cotes.encoder) ; le service vérifie l'attribution à chaque appel et masque les noms à l'enseignant.
        var sheets = api.MapGroup("/deliberation");
        // Les règles de l'encodage sont lisibles par tout compte qui encode : l'enseignant sait ce qui lui est permis.
        sheets.MapGet("/reglages", (DeliberationService s, CancellationToken ct) => s.SettingsAsync(ct));
        sheets.MapGet("/annonces", (DeliberationService s, CancellationToken ct) => s.AnnouncementsAsync(ct));
        sheets.MapGet("/mes-cours", (DeliberationService s, CancellationToken ct) => s.MyCoursesAsync(ct)).RequireAuthorization(Permissions.MarksEncode);
        sheets.MapGet("/feuille", (Guid coursId, string nature, bool? anonyme, DeliberationService s, CancellationToken ct) => s.SheetAsync(coursId, nature, anonyme ?? false, ct));
        sheets.MapPut("/feuille", async (Guid coursId, string nature, List<MarkInput> input, DeliberationService s, CancellationToken ct) =>
            Results.Ok(new { enregistrees = await s.SaveSheetAsync(coursId, nature, input, ct) }));
        sheets.MapPost("/cotes-par-code", (MarkByCodeRequest input, DeliberationService s, CancellationToken ct) => s.SaveByCodeAsync(input, ct));
        sheets.MapPost("/cotes/import", (MarkImportRequest input, DeliberationService s, CancellationToken ct) => s.ImportMarksAsync(input, ct));
        sheets.MapPost("/remise", (Guid coursId, string nature, DeliberationService s, CancellationToken ct) => s.SubmitAsync(coursId, nature, ct));
        sheets.MapGet("/feuille/export", async (Guid coursId, string nature, bool? echecs, string? format, DeliberationService s, ReportService reports, CancellationToken ct) =>
            Table(await s.SheetTableAsync(coursId, nature, echecs ?? false, ct), format, echecs == true ? "liste-des-echecs" : "releve-des-cotes", reports));

        // Documents officiels.
        g.MapGet("/proces-verbal.pdf", async (Guid promotionId, string periode, DeliberationService s, CancellationToken ct) =>
            new InlineFileResult(await s.MinutesPdfAsync(promotionId, periode, ct), Pdf, "proces-verbal.pdf"));
        g.MapGet("/grille", async (Guid promotionId, string periode, string? format, DeliberationService s, ReportService reports, CancellationToken ct) =>
            Table(await s.GridAsync(promotionId, periode, ct), format, "grille-des-cotes", reports));
        // Export global : un onglet par promotion réellement configurée dans l'année courante.
        // Les codes et identités ne sont visibles qu'aux responsables de la délibération.
        g.MapGet("/grilles.xlsx", async (string periode, DeliberationService s, ISpreadsheetService excel, CancellationToken ct) =>
            Results.File(excel.ExportGrids(await s.GridsAsync(periode, ct)), Xlsx, "grilles-deliberation-toutes-promotions.xlsx"))
            .RequireAuthorization(Permissions.DeliberationManage);

        g.MapGet("/palmares", async (Guid promotionId, string periode, string? format, DeliberationService s, ReportService reports, CancellationToken ct) =>
            Table(await s.RankingAsync(promotionId, periode, ct), format, "palmares", reports));
        g.MapGet("/bulletin.pdf", async (Guid promotionId, string periode, Guid inscriptionId, HttpContext http, IConfiguration config, DeliberationService s, CancellationToken ct) =>
            new InlineFileResult(await s.BulletinPdfAsync(promotionId, periode, inscriptionId, PublicBase(http, config), ct), Pdf, "bulletin-deliberation.pdf"));
        g.MapGet("/bulletins.pdf", async (Guid promotionId, string periode, HttpContext http, IConfiguration config, DeliberationService s, CancellationToken ct) =>
            new InlineFileResult(await s.BulletinsPdfAsync(promotionId, periode, PublicBase(http, config), ct), Pdf, "bulletins-deliberation.pdf"));
    }

    private static void MapAcademics(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/scolarite").RequireAuthorization(Permissions.AcademicsRead);
        // Cours, attributions et charges des enseignants (V79 : Gestion des cours, Attribution des cours, Charges).
        g.MapGet("/cours", (Guid promotionId, bool? archives, TeachingService s, CancellationToken ct) => s.CoursesAsync(promotionId, archives ?? false, ct));
        g.MapGet("/cours/numero-suivant", async (Guid promotionId, TeachingService s, CancellationToken ct) => new { numero = await s.NextNumberAsync(promotionId, ct) });
        g.MapPost("/cours", (CourseUpsert input, TeachingService s, CancellationToken ct) => s.UpsertCourseAsync(null, input, ct)).RequireAuthorization(Permissions.AcademicsWrite);
        g.MapPut("/cours/{id:guid}", (Guid id, CourseUpsert input, TeachingService s, CancellationToken ct) => s.UpsertCourseAsync(id, input, ct)).RequireAuthorization(Permissions.AcademicsWrite);
        g.MapPost("/cours/{id:guid}/archivage", (Guid id, TeachingService s, CancellationToken ct) => s.SetArchivedAsync(id, true, ct)).RequireAuthorization(Permissions.AcademicsWrite);
        g.MapPost("/cours/{id:guid}/reactivation", (Guid id, TeachingService s, CancellationToken ct) => s.SetArchivedAsync(id, false, ct)).RequireAuthorization(Permissions.AcademicsWrite);
        g.MapPut("/cours/{id:guid}/attribution", (Guid id, AssignmentRequest input, TeachingService s, CancellationToken ct) => s.AssignAsync(id, input, ct)).RequireAuthorization(Permissions.AcademicsWrite);
        g.MapPost("/cours/{id:guid}/retrait-attribution", (Guid id, CourseArchiveRequest input, TeachingService s, CancellationToken ct) => s.UnassignAsync(id, input, ct)).RequireAuthorization(Permissions.AcademicsWrite);
        g.MapGet("/cours/{id:guid}/attributions", (Guid id, TeachingService s, CancellationToken ct) => s.HistoryAsync(id, ct));
        g.MapGet("/enseignants/charges", (TeachingService s, CancellationToken ct) => s.ChargesAsync(ct));
        g.MapGet("/enseignants/{id:guid}/cours", (Guid id, TeachingService s, CancellationToken ct) => s.TeacherCoursesAsync(id, ct));
        g.MapGet("/cours/{id:guid}/evaluations", (Guid id, AcademicService s, CancellationToken ct) => s.AssessmentsAsync(id, ct));
        g.MapPost("/evaluations", (AssessmentUpsert input, AcademicService s, CancellationToken ct) => s.UpsertAssessmentAsync(null, input, ct)).RequireAuthorization(Permissions.AcademicsWrite);
        g.MapPut("/evaluations/{id:guid}", (Guid id, AssessmentUpsert input, AcademicService s, CancellationToken ct) => s.UpsertAssessmentAsync(id, input, ct)).RequireAuthorization(Permissions.AcademicsWrite);
        g.MapGet("/evaluations/{id:guid}/notes", (Guid id, AcademicService s, CancellationToken ct) => s.GradeSheetAsync(id, ct));
        g.MapPut("/evaluations/{id:guid}/notes", async (Guid id, List<GradeInput> input, AcademicService s, CancellationToken ct) => Results.Ok(new { modifiees = await s.SaveGradesAsync(id, input, ct) }))
            .RequireAuthorization(Permissions.AcademicsWrite);
        g.MapGet("/resultats", (Guid promotionId, int? periode, AcademicService s, CancellationToken ct) => s.ResultsAsync(promotionId, periode, ct));
        g.MapGet("/bulletin.pdf", async (Guid promotionId, Guid inscriptionId, int? periode, AcademicService s, CancellationToken ct) =>
            new InlineFileResult(await s.ReportCardPdfAsync(promotionId, inscriptionId, periode, ct), Pdf, "bulletin.pdf"));
        g.MapGet("/presences", (Guid promotionId, DateOnly date, AcademicService s, CancellationToken ct) => s.AttendanceSheetAsync(promotionId, date, ct));
        g.MapPut("/presences", async (Guid promotionId, DateOnly date, List<AttendanceInput> input, AcademicService s, CancellationToken ct) =>
            Results.Ok(new { enregistrees = await s.SaveAttendanceAsync(promotionId, date, input, ct) })).RequireAuthorization(Permissions.AcademicsWrite);
        g.MapGet("/presences/bilan", (Guid promotionId, DateOnly du, DateOnly au, AcademicService s, CancellationToken ct) => s.AttendanceSummaryAsync(promotionId, du, au, ct));
        // Jours de cours confirmés : seul un jour confirmé et passé, sans appel, devient une absence non signée.
        g.MapGet("/jours-de-cours", (Guid promotionId, DateOnly du, DateOnly au, AcademicService s, CancellationToken ct) => s.CourseDaysAsync(promotionId, du, au, ct));
        g.MapPost("/jours-de-cours", (Guid promotionId, List<DateOnly> dates, AcademicService s, CancellationToken ct) => s.ConfirmCourseDaysAsync(promotionId, dates, ct))
            .RequireAuthorization(Permissions.AcademicsWrite);
        g.MapPost("/jours-de-cours/retrait", (Guid promotionId, List<DateOnly> dates, AcademicService s, CancellationToken ct) => s.ReleaseCourseDaysAsync(promotionId, dates, ct))
            .RequireAuthorization(Permissions.AcademicsWrite);
        g.MapGet("/presences/alertes", (Guid promotionId, DateOnly du, DateOnly au, AcademicService s, CancellationToken ct) => s.AttendanceAlertsAsync(promotionId, du, au, ct));
        g.MapGet("/discipline", (Guid? promotionId, AcademicService s, CancellationToken ct) => s.IncidentsAsync(promotionId, ct));
        g.MapPost("/discipline", (IncidentCreate input, AcademicService s, CancellationToken ct) => s.CreateIncidentAsync(input, ct)).RequireAuthorization(Permissions.AcademicsWrite);

        // Registre disciplinaire : renvois, convocations, réception des parents, annulations et historique.
        g.MapGet("/discipline/registre", (Guid? promotionId, string? mesure, DisciplineService s, CancellationToken ct) => s.RegistryAsync(promotionId, mesure, ct))
            .RequireAuthorization(Permissions.AcademicsRead);
        g.MapGet("/discipline/inscriptions/{id:guid}/historique", (Guid id, DisciplineService s, CancellationToken ct) => s.HistoryAsync(id, ct))
            .RequireAuthorization(Permissions.AcademicsRead);
        g.MapPost("/discipline/registre", (DisciplineRequest input, DisciplineService s, CancellationToken ct) => s.RegisterAsync(input, ct))
            .RequireAuthorization(Permissions.AcademicsWrite);
        g.MapPut("/discipline/registre/{id:guid}/reception", (Guid id, DisciplineReceptionInput input, DisciplineService s, CancellationToken ct) => s.ReceptionAsync(id, input, ct))
            .RequireAuthorization(Permissions.AcademicsWrite);
        g.MapPost("/discipline/registre/{id:guid}/annulation", (Guid id, DisciplineCancelInput input, DisciplineService s, CancellationToken ct) => s.CancelAsync(id, input, ct))
            .RequireAuthorization(Permissions.DisciplineCancel);
        g.MapGet("/discipline/registre/{id:guid}/convocation", async (Guid id, DisciplineService s, CancellationToken ct) =>
            Results.Content(await s.ConvocationHtmlAsync(id, ct), "text/html", Encoding.UTF8));
        g.MapGet("/discipline/registre/export.csv", async (Guid? promotionId, string? mesure, DisciplineService s, CancellationToken ct) =>
            Results.File(new UTF8Encoding(true).GetBytes(await s.CsvAsync(promotionId, mesure, ct)), "text/csv; charset=utf-8", "discipline.csv"))
            .RequireAuthorization(Permissions.AcademicsRead);
    }

    private static void MapStaff(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/personnel").RequireAuthorization(Permissions.StaffManage);
        // Pointage : signatures d'entrée et de sortie, justifications, statut des journées et bilan pour la paie.
        g.MapPost("/{id:guid}/signature/{kind}", (Guid id, string kind, StaffAttendanceService s, CancellationToken ct) => s.MarkAsync(id, kind, ct));
        g.MapGet("/{id:guid}/pointage", (Guid id, DateOnly du, DateOnly au, StaffAttendanceService s, CancellationToken ct) => s.DaysAsync(id, du, au, ct));
        g.MapPost("/{id:guid}/justifications", (Guid id, StaffJustifyRequest input, StaffAttendanceService s, CancellationToken ct) => s.JustifyAsync(id, input.Date, input.Reason, ct));
        g.MapPost("/justifications/{id:guid}/annulation", (Guid id, StaffCancelRequest input, StaffAttendanceService s, CancellationToken ct) => s.CancelJustificationAsync(id, input.Reason, ct));
        g.MapGet("/pointage/bilan", (DateOnly du, DateOnly au, StaffAttendanceService s, CancellationToken ct) => s.BilanAsync(du, au, ct));
        g.MapGet("/pointage/jour", (DateOnly date, StaffAttendanceService s, CancellationToken ct) => s.DayOfAllAsync(date, ct));
        g.MapGet("", (StaffService s, CancellationToken ct) => s.ListAsync(ct));
        g.MapPost("", (StaffUpsert input, StaffService s, CancellationToken ct) => s.UpsertAsync(null, input, ct));
        g.MapPut("/{id:guid}", (Guid id, StaffUpsert input, StaffService s, CancellationToken ct) => s.UpsertAsync(id, input, ct));
        g.MapGet("/paie/{periode}", (string periode, StaffService s, CancellationToken ct) => s.PayrollAsync(periode, ct));
        g.MapPost("/paie/{periode}/preparation", async (string periode, StaffService s, CancellationToken ct) => Results.Ok(new { crees = await s.GenerateAsync(periode, ct) }));
        g.MapPut("/paie/bulletins/{id:guid}", (Guid id, PayrollUpdate input, StaffService s, CancellationToken ct) => s.UpdateEntryAsync(id, input, ct));
        g.MapPost("/paie/bulletins/{id:guid}/paiement", (Guid id, string? mode, StaffService s, CancellationToken ct) => s.PayAsync(id, mode ?? "Especes", ct));
        api.MapGet("/enseignants", (TeachingService s, CancellationToken ct) => s.AvailableTeachersAsync(ct))
            .RequireAuthorization(Permissions.AcademicsRead);
    }

    private static void MapAdministration(RouteGroupBuilder api)
    {
        var users = api.MapGroup("/utilisateurs").RequireAuthorization(Permissions.UsersManage);
        users.MapGet("", (UserService u, CancellationToken ct) => u.ListAsync(ct));
        users.MapPost("", (UserCreate input, UserService u, CancellationToken ct) => u.CreateAsync(input, ct));
        users.MapPut("/{id:guid}", (Guid id, UserUpdate input, UserService u, CancellationToken ct) => u.UpdateAsync(id, input, ct));
        users.MapPost("/{id:guid}/mot-de-passe", async (Guid id, PasswordReset input, UserService u, CancellationToken ct) => { await u.ResetPasswordAsync(id, input.NewPassword, ct); return Results.NoContent(); });

        // Comptes enseignants (gestion des délibérations) : identifiants et codes provisoires générés par le secrétariat.
        var teachers = api.MapGroup("/comptes-enseignants").RequireAuthorization(Permissions.UsersManage);
        teachers.MapGet("", (TeacherAccountService t, CancellationToken ct) => t.ListAsync(ct));
        teachers.MapPost("", (TeacherAccountRequest input, TeacherAccountService t, CancellationToken ct) => t.IssueAsync(input, ct));
        teachers.MapPost("/nouvel-enseignant", (NewTeacherRequest input, TeacherAccountService t, CancellationToken ct) => t.CreateTeacherAsync(input, ct))
            .RequireAuthorization(Permissions.StaffManage);
        teachers.MapPost("/{id:guid}/code-provisoire", (Guid id, TeacherAccountService t, CancellationToken ct) => t.ResetAsync(id, ct));
        teachers.MapPut("/{id:guid}", (Guid id, TeacherAccountUpdate input, TeacherAccountService t, CancellationToken ct) => t.UpdateAsync(id, input, ct));

        var audit = api.MapGroup("/audit").RequireAuthorization(Permissions.AuditRead);
        audit.MapGet("", (string? q, string? type, DateOnly? du, DateOnly? au, int? page, int? taille, AuditService a, CancellationToken ct) => a.ListAsync(q, type, du, au, page ?? 1, taille ?? 100, ct));
        audit.MapGet("/verification", (AuditService a, CancellationToken ct) => a.VerifyAsync(ct));
        // Export CSV du journal : l'export lui-même est tracé (consulter le journal est une action sensible).
        audit.MapGet("/export", async (string? q, string? type, DateOnly? du, DateOnly? au, AuditService a, IAuditLog log, ScoliaDbContext db, CancellationToken ct) =>
        {
            var csv = await a.ExportCsvAsync(q, type, du, au, ct);
            log.Record("audit.exporte", "Audit", null, "Export du journal d’audit au format CSV.");
            await db.SaveChangesAsync(ct);
            // BOM UTF-8 explicite : sans lui, Excel affiche mal les accents.
            var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray();
            return Results.File(bytes, "text/csv; charset=utf-8", $"journal-audit-{DateTime.Today:yyyyMMdd}.csv");
        });

        var backups = api.MapGroup("/sauvegardes").RequireAuthorization(Permissions.BackupManage);
        backups.MapGet("", (BackupService b) => b.List());
        backups.MapPost("", async (BackupService b, IAuditLog log, ScoliaDbContext db, ICurrentUser user,
            Scolia.Infrastructure.Live.LiveHub live, DataRevision revision, CancellationToken ct) =>
        {
            var info = await b.CreateAsync("manuelle", ct);
            log.Record("sauvegarde.creee", "Sauvegarde", info.Name, $"Sauvegarde manuelle {info.Name}.");
            await db.SaveChangesAsync(ct);
            // Les autres postes l'apprennent tout de suite : bannière et notification du téléphone.
            live.Changed(revision.Value, user.DeviceId, user.Device, user.Name, "Sauvegarde manuelle enregistrée et vérifiée.", ["Sauvegarde"]);
            return info;
        });
        backups.MapGet("/{name}", (string name, BackupService b) => Results.File(b.PathOf(name), "application/vnd.sqlite3", name));
        // Export portable chiffré, réservé à une liaison HTTPS vérifiée ou au serveur lui-même.
        backups.MapPost("/{name}/export-chiffre", async Task<IResult> (
            string name, EncryptedBackupRequest input, BackupService b, HttpContext http,
            IAuditLog log, ScoliaDbContext db, CancellationToken ct) =>
        {
            if (!TrustedBackupTransport(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var data = await b.ExportEncryptedAsync(name, input.Passphrase, ct);
            log.Record("sauvegarde.export-chiffre", "Sauvegarde", name, $"Export protégé de {name}.");
            await db.SaveChangesAsync(ct);
            return Results.File(data, "application/octet-stream", name + ".scolia-enc");
        });
        // Déchiffrement dans une NOUVELLE copie contrôlée : aucune restauration immédiate.
        backups.MapPost("/import-chiffre", async Task<IResult> (
            HttpContext http, BackupService b, IAuditLog log, ScoliaDbContext db, CancellationToken ct) =>
        {
            if (!TrustedBackupTransport(http)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (!http.Request.HasFormContentType)
                throw new DomainException("sauvegardes", "Envoyez un formulaire avec archive et passphrase.");
            var form = await http.Request.ReadFormAsync(ct);
            var upload = form.Files.GetFile("archive");
            if (upload is null || upload.Length is 0 or > BackupProtection.MaximumPlainBytes + 256)
                throw new DomainException("sauvegardes", "Archive absente ou supérieure à 48 Mio.");
            await using var data = new MemoryStream();
            await upload.CopyToAsync(data, ct);
            var info = await b.ImportEncryptedAsync(data.ToArray(), form["passphrase"].ToString(), ct);
            log.Record("sauvegarde.import-chiffre", "Sauvegarde", info.Name,
                $"Archive chiffrée importée sous {info.Name} après vérification d'intégrité.");
            await db.SaveChangesAsync(ct);
            return Results.Ok(info);
        }).DisableAntiforgery();
        backups.MapPost("/{name}/restauration", async (string name, BackupService b, IAuditLog log, ScoliaDbContext db, CancellationToken ct) =>
        {
            await b.RestoreAsync(name, ct);
            // La trace est écrite dans la base restaurée : la chaîne d'audit reprend à partir de son dernier maillon.
            log.Record("sauvegarde.restauree", "Sauvegarde", name, $"Base restaurée depuis {name}.");
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Permissions.SettingsWrite);
        backups.MapGet("/integrite", async (BackupService b, CancellationToken ct) => Results.Ok(new { intacte = await b.CheckLiveDatabaseAsync(ct) }));
        // Vérification d'une copie : contrôle SQLite et empreinte SHA-256. Le nettoyage n'a lieu qu'à la demande.
        backups.MapGet("/{name}/verification", (string name, BackupService b, CancellationToken ct) => b.VerifyAsync(name, ct));
        backups.MapPost("/nettoyage", async (BackupCleanup input, BackupService b, IAuditLog log, ScoliaDbContext db, CancellationToken ct) =>
        {
            var removed = b.Cleanup(input.Garder, input.PlusAnciennesQue);
            log.Record("sauvegarde.nettoyage", "Sauvegarde", null, $"{removed} sauvegarde(s) ancienne(s) supprimée(s) ; {input.Garder} plus récente(s) gardée(s).");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { supprimees = removed });
        });

        // Effacement volontaire : le mot de passe du compte connecté confirme, une sauvegarde précède, le journal garde la trace.
        const string motDePasseRefuse = "Mot de passe incorrect : aucune donnée n'a été effacée.";
        var erase = api.MapGroup("/effacement").RequireAuthorization(Permissions.BackupManage);
        erase.MapPost("/paiements", async (ErasureRequest input, DataErasureService s, BackupService backups, CancellationToken ct) =>
        {
            if (!await s.ConfirmAsync(input.Password, "paiements", ct)) throw new DomainException("motDePasse", motDePasseRefuse);
            await backups.CreateAsync("avant-effacement-paiements", ct);
            return await s.ErasePaymentsAsync(input, ct);
        });
        erase.MapPost("/apprenants", async (ErasureRequest input, DataErasureService s, BackupService backups, CancellationToken ct) =>
        {
            if (!await s.ConfirmAsync(input.Password, "apprenants", ct)) throw new DomainException("motDePasse", motDePasseRefuse);
            await backups.CreateAsync("avant-effacement-apprenants", ct);
            return await s.EraseStudentsAsync(input, ct);
        });

        api.MapGet("/import/modele", () => Results.File(LegacyImportService.Template(), Xlsx, "modele-import-scolia.xlsx"))
            .RequireAuthorization(Permissions.SettingsWrite);
        api.MapPost("/import/scolia", async (HttpRequest request, bool? appliquer, LegacyImportService importer, CancellationToken ct) =>
        {
            if (!request.HasFormContentType) throw new DomainException("fichiers", "Envoyez les classeurs Excel à importer.");
            var form = await request.ReadFormAsync(ct);
            if (form.Files.Count == 0) throw new DomainException("fichiers", "Choisissez au moins un classeur SCOLIA (.xlsm ou .xlsx).");
            var streams = new List<(string, Stream)>();
            try
            {
                foreach (var f in form.Files)
                {
                    if (!f.FileName.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase) && !f.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                        throw new DomainException("fichiers", $"{f.FileName} n'est pas un classeur Excel (.xlsm ou .xlsx).");
                    var ms = new MemoryStream();
                    await f.CopyToAsync(ms, ct);
                    ms.Position = 0;
                    streams.Add((f.FileName, ms));
                }
                return await importer.ImportAsync(streams, appliquer == true, ct);
            }
            finally
            {
                foreach (var (_, s) in streams) await s.DisposeAsync();
            }
        }).RequireAuthorization(Permissions.SettingsWrite).DisableAntiforgery();
    }
}

/// <summary>Phrase secrète jamais persistée et non acceptée sur une liaison HTTP distante.</summary>
public sealed record EncryptedBackupRequest(string Passphrase);

/// <summary>Fichier affiché dans le navigateur (PDF) avec un nom proposé à l'enregistrement.</summary>
public sealed class InlineFileResult(byte[] content, string contentType, string fileName) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.ContentType = contentType;
        httpContext.Response.Headers.ContentDisposition = $"inline; filename=\"{fileName}\"";
        httpContext.Response.ContentLength = content.Length;
        await httpContext.Response.Body.WriteAsync(content);
    }
}
