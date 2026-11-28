using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>Organisation commune à l'école, accessible depuis le compte du secrétariat.</summary>
public sealed class ManagementModePage : PageViewModel
{
    private string currentMode, selectedMode, notice = "Chargement de l'organisation de l'école…";
    private bool supported;
    private int accountants;

    public ManagementModePage(ShellViewModel shell) : base(shell)
    {
        currentMode = selectedMode = shell.Session?.ManagementMode ?? "Mixte";
        Apply = new AsyncCommand(() => Shell.Run(ApplyAsync));
        OpenAccounts = new AsyncCommand(() => Shell.Run(() => Shell.Open(() => new UsersPage(Shell))));
        ActionRefresh();
    }

    public static bool CanAccess(ShellViewModel shell) => shell.Can(Perm.SettingsWrite)
        && shell.Session?.Role is "Secretariat" or "Administrateur" or "Direction";
    public override string Title => "Type de gestion";
    public override string Subtitle => "Organisation du secrétariat et de la comptabilité · toute l'école";
    public override MaterialIconKind Glyph => MaterialIconKind.AccountSwitch;
    public bool Supported { get => supported; private set { if (Set(ref supported, value)) RaiseChoices(); } }
    public string CurrentLabel => currentMode == "Separee" ? "Gestion séparée" : "Gestion mixte";
    public string AccountantsLine => accountants == 0 ? "Aucun compte Comptabilité actif" : Format.Count(accountants, "compte Comptabilité actif", "comptes Comptabilité actifs");
    public string Notice { get => notice; private set => Set(ref notice, value); }
    public string SelectedMode
    {
        get => selectedMode;
        set { if (value is "Mixte" or "Separee" && Set(ref selectedMode, value)) RaiseChoices(); }
    }
    public bool MixedSelected { get => SelectedMode == "Mixte"; set { if (value) SelectedMode = "Mixte"; } }
    public bool SeparatedSelected { get => SelectedMode == "Separee"; set { if (value) SelectedMode = "Separee"; } }
    public bool NeedsAccountant => Supported && SeparatedSelected && accountants == 0;
    public bool CanApply => Supported && Shell.Api.IsOnline && SelectedMode != currentMode && !NeedsAccountant;
    public string Responsibilities => SeparatedSelected
        ? "Le secrétariat gère les dossiers, la scolarité, le personnel administratif et les paramètres. Il consulte la situation d'un apprenant. La comptabilité assure les encaissements, la caisse, les prises en charge et la paie."
        : "Le secrétariat conserve toutes ses fonctions et les opérations comptables du fonctionnement actuel. Les autres comptes gardent leurs responsabilités habituelles.";
    public AsyncCommand Apply { get; }
    public AsyncCommand OpenAccounts { get; }

    private void RaiseChoices()
    {
        foreach (var name in new[] { nameof(MixedSelected), nameof(SeparatedSelected), nameof(NeedsAccountant), nameof(CanApply), nameof(Responsibilities) }) Raise(name);
    }

    public override async Task LoadAsync()
    {
        if (!CanAccess(Shell)) throw new InvalidOperationException("Ce réglage est réservé au secrétariat.");
        try
        {
            var status = await Shell.Api.GetAsync<ManagementModeStatusDto>("api/organisation/gestion");
            Supported = status.Version >= 1 && status.Mode is "Mixte" or "Separee";
            currentMode = status.Mode;
            accountants = status.ActiveAccountants;
            SelectedMode = currentMode;
            Notice = Shell.Api.IsOnline
                ? "Ce choix s'applique à tous les comptes de l'école. Les dossiers et les opérations déjà enregistrées sont conservés."
                : "Connectez-vous au serveur pour modifier l'organisation de l'école.";
            Raise(nameof(CurrentLabel)); Raise(nameof(AccountantsLine)); RaiseChoices();
        }
        catch (ApiException ex) when (ex.Status == 404)
        {
            Supported = false;
            Notice = "Mettez à jour le serveur SCOLIA de l'école pour choisir le type de gestion.";
        }
        catch (OfflineException)
        {
            Supported = false;
            Notice = "Connectez-vous au serveur de l'école, puis utilisez Actualiser pour modifier le type de gestion.";
        }
    }

    private async Task ApplyAsync()
    {
        if (!CanApply)
        {
            if (!Shell.Api.IsOnline) Shell.Notify("Connectez-vous au serveur pour modifier le type de gestion.");
            return;
        }
        var chosen = SelectedMode;
        SessionDto? updated = null;
        var password = string.Empty;
        var form = new FormViewModel("Appliquer le type de gestion",
            $"{(chosen == "Separee" ? "Gestion séparée" : "Gestion mixte")} pour toute l'école. Les autres comptes Secrétariat et Comptabilité devront se reconnecter pour actualiser leurs droits.", async f =>
        {
            password = f.Find("code")?.Text ?? string.Empty;
            updated = await Shell.Api.SendAsync<SessionDto>(HttpMethod.Put, "api/organisation/gestion", new { mode = chosen, currentPassword = password });
            return true;
        }) { SubmitText = "Appliquer" };
        form.Add("code", "Votre code ou mot de passe actuel", FieldKind.Secret, required: true);
        if (await Shell.Ask(form) && updated is not null)
        {
            await Shell.ApplyManagementSessionAsync(updated, password);
            Shell.Notify($"{(chosen == "Separee" ? "Gestion séparée" : "Gestion mixte")} activée. Les droits de votre espace sont à jour.");
        }
    }
}
