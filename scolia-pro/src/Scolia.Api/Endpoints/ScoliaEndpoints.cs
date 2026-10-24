using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Scolia.Api.Security;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Application.Services;
using Scolia.Domain.Common;
using Scolia.Domain.Security;
using Scolia.Infrastructure.Backup;
using Scolia.Infrastructure.Import;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Api.Endpoints;

public static class ScoliaEndpoints
{
    private const string Pdf = "application/pdf";
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static IResult Table(TableDocument table, string? format, string baseName, ReportService reports) =>
        string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase)
            ? Results.File(reports.Excel(table), Xlsx, $"{baseName}.xlsx")
            : new InlineFileResult(reports.Pdf(table), Pdf, $"{baseName}.pdf");

    private static string PublicBase(HttpContext http, IConfiguration config) =>
        config["Scolia:UrlPublique"] is { Length: > 0 } configured ? configured : $"{http.Request.Scheme}://{http.Request.Host}";

    public static void MapScoliaEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/sante", async (BackupService backups, CancellationToken ct) => Results.Ok(new { statut = "ok", version = typeof(ScoliaEndpoints).Assembly.GetName().Version?.ToString(), heure = DateTime.Now }));

        // ---------- Installation et session ----------
        api.MapGet("/installation", (SetupService setup, CancellationToken ct) => setup.StatusAsync(ct));
        // Le logo figure sur l'écran de connexion et les pages publiques de vérification.
        api.MapGet("/etablissement/logo", async (SchoolService s, CancellationToken ct) => await s.GetLogoAsync(ct) is { } logo ? Results.File(logo.Content, logo.Type) : Results.Redirect("/scolia.png"));
        api.MapGet("/installation/annee-proposee", (IClock clock) => Results.Ok(new { label = SetupService.DefaultYearLabel(clock.Today) }));
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

        // ---------- Vérification publique des reçus (QR code) ----------
        api.MapGet("/verifier/{code}", (string code, PaymentService payments, CancellationToken ct) => payments.VerifyAsync(code, ct)).RequireRateLimiting("connexion");

        var secured = api.MapGroup("").RequireAuthorization();
        secured.MapGet("/recherche", (string? q, SearchService search, CancellationToken ct) => search.SearchAsync(q, ct)).RequireAuthorization(Permissions.StudentsRead);
        // Révision des données : les postes la comparent pour recharger l'écran après une écriture faite ailleurs.
        secured.MapGet("/revision", (DataRevision revision) => Results.Ok(new { revision = revision.Value }));

        MapSchool(secured);
        MapStudents(secured);
        MapFinance(secured);
        MapReports(secured);
        MapAcademics(secured);
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
        g.MapPost("/annees/{id:guid}/courante", async (Guid id, SchoolService s, CancellationToken ct) => { await s.SetCurrentYearAsync(id, ct); return Results.NoContent(); })
            .RequireAuthorization(Permissions.SettingsWrite);
        g.MapPost("/annees/{id:guid}/cloture", async (Guid id, SchoolService s, CancellationToken ct) => { await s.CloseYearAsync(id, ct); return Results.NoContent(); })
            .RequireAuthorization(Permissions.SettingsWrite);

        g.MapGet("/sections", (SchoolService s, CancellationToken ct) => s.SectionsAsync(ct));
        g.MapPost("/sections", (SectionUpsert input, SchoolService s, CancellationToken ct) => s.UpsertSectionAsync(null, input, ct)).RequireAuthorization(Permissions.SettingsWrite);
        g.MapPut("/sections/{id:guid}", (Guid id, SectionUpsert input, SchoolService s, CancellationToken ct) => s.UpsertSectionAsync(id, input, ct)).RequireAuthorization(Permissions.SettingsWrite);

        g.MapGet("/promotions", (Guid? anneeId, SchoolService s, CancellationToken ct) => s.PromotionsAsync(anneeId, ct));
        g.MapPost("/promotions", (PromotionUpsert input, SchoolService s, CancellationToken ct) => s.UpsertPromotionAsync(null, input, ct)).RequireAuthorization(Permissions.SettingsWrite);
        g.MapPut("/promotions/{id:guid}", (Guid id, PromotionUpsert input, SchoolService s, CancellationToken ct) => s.UpsertPromotionAsync(id, input, ct)).RequireAuthorization(Permissions.SettingsWrite);
        g.MapDelete("/promotions/{id:guid}", async (Guid id, SchoolService s, CancellationToken ct) => { await s.DeletePromotionAsync(id, ct); return Results.NoContent(); })
            .RequireAuthorization(Permissions.SettingsWrite);
    }

    private static void MapStudents(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/apprenants").RequireAuthorization(Permissions.StudentsRead);
        g.MapGet("", (string? q, Guid? promotionId, string? statut, Guid? anneeId, int? page, int? taille, StudentService s, CancellationToken ct) =>
            s.SearchAsync(q, promotionId, statut, anneeId, page ?? 1, taille ?? 50, ct));
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
                if (fichier.ContentType is not ("image/jpeg" or "image/png" or "image/webp")) throw new DomainException("photo", "La photo doit être au format JPEG, PNG ou WebP.");
                using var ms = new MemoryStream();
                await fichier.CopyToAsync(ms, ct);
                content = ms.ToArray();
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
        g.MapPost("/apercu", (PaymentRequest input, PaymentService p, CancellationToken ct) => p.PreviewAsync(input.EnrollmentId, input.FeeItemIds, ct))
            .RequireAuthorization(Permissions.PaymentsCreate);
        g.MapPost("", (PaymentRequest input, PaymentService p, CancellationToken ct) => p.CreateAsync(input, ct)).RequireAuthorization(Permissions.PaymentsCreate);
        g.MapGet("/{id:guid}", (Guid id, PaymentService p, CancellationToken ct) => p.GetAsync(id, ct));
        g.MapPost("/{id:guid}/annulation", (Guid id, CancelRequest input, PaymentService p, CancellationToken ct) => p.CancelAsync(id, input.Reason, ct))
            .RequireAuthorization(Permissions.PaymentsCancel);
        g.MapGet("/{id:guid}/recu.pdf", async (Guid id, PaymentService p, HttpContext http, IConfiguration config, CancellationToken ct) =>
            new InlineFileResult(await p.ReceiptPdfAsync(id, PublicBase(http, config), ct), Pdf, "recu.pdf"));

        var d = api.MapGroup("/depenses").RequireAuthorization(Permissions.PaymentsRead);
        d.MapGet("", (DateOnly? du, DateOnly? au, string? categorie, string? q, int? page, int? taille, CashService c, CancellationToken ct) =>
            c.ExpensesAsync(du, au, categorie, q, page ?? 1, taille ?? 50, ct));
        d.MapGet("/categories", (CashService c, CancellationToken ct) => c.CategoriesAsync(ct));
        d.MapPost("", (ExpenseCreate input, CashService c, CancellationToken ct) => c.CreateExpenseAsync(input, ct)).RequireAuthorization(Permissions.ExpensesWrite);
        d.MapPost("/{id:guid}/annulation", (Guid id, CancelRequest input, CashService c, CancellationToken ct) => c.CancelExpenseAsync(id, input.Reason, ct))
            .RequireAuthorization(Permissions.ExpensesWrite);

        var k = api.MapGroup("/caisse").RequireAuthorization(Permissions.PaymentsRead);
        k.MapGet("/situation", (DateOnly? date, CashService c, CancellationToken ct) => c.SituationAsync(date, ct));
        k.MapGet("/arretes", (CashService c, CancellationToken ct) => c.ClosingsAsync(ct));
        k.MapPost("/arretes", (CashClosingRequest input, CashService c, CancellationToken ct) => c.CloseAsync(input, ct)).RequireAuthorization(Permissions.CashClose);
    }

    private static void MapReports(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/rapports").RequireAuthorization(Permissions.ReportsRead);
        g.MapGet("/tableau-de-bord", (Guid? promotionId, ReportService r, CancellationToken ct) => r.DashboardAsync(promotionId, ct));
        g.MapGet("/debiteurs", ([AsParameters] DebtorQuery q, ReportService r, CancellationToken ct) => r.DebtorsAsync(q, ct));
        g.MapGet("/debiteurs/export", async ([AsParameters] DebtorQuery q, string? format, ReportService r, CancellationToken ct) =>
            Table(await r.DebtorsTableAsync(q, ct), format, "retards-de-paiement", r));
        g.MapGet("/liste-classe/{promotionId:guid}", async (Guid promotionId, string? format, ReportService r, CancellationToken ct) =>
            Table(await r.ClassListAsync(promotionId, ct), format, "liste-nominative", r));
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
        backups.MapPost("", async (BackupService b, IAuditLog log, ScoliaDbContext db, CancellationToken ct) =>
        {
            var info = await b.CreateAsync("manuelle", ct);
            log.Record("sauvegarde.creee", "Sauvegarde", info.Name, $"Sauvegarde manuelle {info.Name}.");
            await db.SaveChangesAsync(ct);
            return info;
        });
        backups.MapGet("/{name}", (string name, BackupService b) => Results.File(b.PathOf(name), "application/vnd.sqlite3", name));
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
