using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

public sealed record SchoolLicenseStatus(
    bool Managed, bool OwnerConfigured, bool ReadOnly, string LicenseStatus,
    int? DaysRemaining, long? ExpiresAt, string Reason,
    bool BillingEnabled, DateTimeOffset? LastVerifiedAt);

/// <summary>Vue de licence propre à l'établissement serveur. Les postes liés
/// consultent le même état, sans pouvoir changer la licence ni les relais.</summary>
public sealed class SchoolLicenseViewModel : Observable
{
    private readonly ShellViewModel shell;
    private SchoolLicenseStatus? current;
    private string invitation = string.Empty;
    private bool consent;
    private string message = "Licence scolaire : chargement en cours.";
    private bool loading;
    private DateTime lastCheckUtc = DateTime.MinValue;

    public SchoolLicenseViewModel(ShellViewModel shell)
    {
        this.shell = shell;
        Refresh = new AsyncCommand(RefreshAsync);
        Enroll = new AsyncCommand(EnrollAsync);
        Open = new AsyncCommand(() =>
            shell.IsServerComputer && shell.Can(Perm.SettingsWrite)
                ? shell.Open(() => new SettingsPage(shell, "licence"))
                : ShowReadOnlyStatusAsync());
    }

    public SchoolLicenseStatus? Current => current;
    public string Badge => current switch
    {
        { ReadOnly: true } => "Licence · Lecture seule",
        { Managed: false } => "Licence",
        { BillingEnabled: false } => "Licence · Gratuite",
        { DaysRemaining: { } days } => $"Licence · {days} j",
        { LicenseStatus: "derogation" } => "Licence · Dérogation",
        _ => "Licence"
    };
    public string Details => current switch
    {
        { ReadOnly: true } => "Licence en lecture seule : consultations autorisées, modifications suspendues sur tous les postes.",
        { Managed: false, OwnerConfigured: false } => "La console JP-Services n'est pas encore activée dans cette distribution. Aucune licence n'est facturée.",
        { Managed: false } => "Votre établissement n'a pas encore été enregistré pour la gestion des licences JP-Services.",
        { BillingEnabled: false } => "SCOLIA Pro : facturation désactivée par JP-Services.",
        { DaysRemaining: { } days } => $"{days} jour(s) restant(s) · {current.LicenseStatus}. Les données restent sur le serveur scolaire.",
        _ => $"Licence {current.LicenseStatus}."
    };
    public bool IsReadOnly => current?.ReadOnly == true;
    public bool IsManaged => current?.Managed == true;
    public bool CanEnroll => shell.IsServerComputer &&
        shell.Can(Perm.SettingsWrite) && !IsManaged && current?.OwnerConfigured == true;
    public string Message { get => message; private set => Set(ref message, value); }
    public string Invitation { get => invitation; set => Set(ref invitation, value); }
    public bool Consent { get => consent; set => Set(ref consent, value); }
    public bool Loading { get => loading; private set => Set(ref loading, value); }
    public AsyncCommand Refresh { get; }
    public AsyncCommand Enroll { get; }
    public AsyncCommand Open { get; }

    private Task ShowReadOnlyStatusAsync()
    {
        shell.Notify(Details);
        return Task.CompletedTask;
    }

    public async Task RefreshAsync()
    {
        if (!shell.IsLoggedIn || loading || DateTime.UtcNow - lastCheckUtc < TimeSpan.FromSeconds(4)) return;
        Loading = true;
        try
        {
            var status = await shell.Api.GetAsync<SchoolLicenseStatus>("api/licence");
            current = status;
            lastCheckUtc = DateTime.UtcNow;
            // La consultation de la licence ne change pas la session. En lecture
            // seule, même le travail hors ligne doit éviter les nouvelles écritures.
            shell.Api.LicenseReadOnly = status.ReadOnly;
            if (shell.Api.Offline is { } local && local.User.Length > 0)
                local.SaveLicenseReadOnlyFor(local.User, status.ReadOnly);
            foreach (var property in new[] { nameof(Current), nameof(Badge),
                nameof(Details), nameof(IsReadOnly), nameof(IsManaged), nameof(CanEnroll) })
                Raise(property);
            Message = Details;
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException
            or IOException or TaskCanceledException)
        {
            if (current is null)
                Message = "Statut de licence momentanément indisponible. Les données locales restent consultables.";
        }
        finally { Loading = false; }
    }

    private async Task EnrollAsync()
    {
        if (!CanEnroll || !Consent)
        {
            Message = "Inscription réservée au responsable du serveur après acceptation des informations de licence.";
            return;
        }
        if (Invitation.Trim().Length < 20)
        {
            Message = "Entrez le code d'invitation de l'établissement fourni par JP-Services.";
            return;
        }
        Loading = true;
        try
        {
            var school = shell.SchoolLine.Split(" · ")[0];
            await shell.Api.SendAsync<SchoolLicenseStatus>(HttpMethod.Post,
                "api/licence/enregistrer", new {
                    SchoolName = school, InviteCode = Invitation.Trim(), Consent = true
                });
            Invitation = string.Empty;
            lastCheckUtc = DateTime.MinValue;
            Message = "Enregistrement du serveur terminé. Les données scolaires ne sont pas transmises.";
        }
        catch (Exception ex) when (ex is ApiException or InvalidOperationException
            or HttpRequestException or TaskCanceledException)
        {
            Message = $"Inscription non terminée : {ex.Message}";
        }
        finally { Loading = false; }
        await RefreshAsync();
    }
}
