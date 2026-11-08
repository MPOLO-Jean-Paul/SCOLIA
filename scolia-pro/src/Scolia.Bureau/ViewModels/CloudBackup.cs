using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

public sealed record CloudBackupRowVm(Guid Id, string Created, string Size, string Key, CloudBackupDto Source);

public sealed record CloudRefusalRowVm(string When, string Label, string User, string Reason);

/// <summary>
/// Sauvegarde et relève en ligne. Activée une fois par le responsable, elle tourne ensuite seule :
/// copie chiffrée par l'école toutes les heures si des données ont changé, au moins une fois par jour,
/// et reprise automatique des opérations faites sur les appareils pendant une coupure du serveur.
/// </summary>
public sealed class CloudBackupPage : PageViewModel
{
    private CloudBackupRowVm? selected;
    private CloudStatusDto? status;
    private string subtitle = string.Empty;
    private string notice = string.Empty;

    public CloudBackupPage(ShellViewModel shell) : base(shell)
    {
        Action("Activer ou arrêter", MaterialIconKind.CloudLock, ToggleAsync, primary: true);
        Action("Sauvegarder maintenant", MaterialIconKind.CloudUpload, BackupNowAsync);
        Action("Reprendre les opérations en ligne", MaterialIconKind.CloudSync, DrainAsync);
        Action("Code de secours", MaterialIconKind.KeyChain, ShowRecoveryCodeAsync);
        Action("Récupérer cette copie", MaterialIconKind.CloudDownload, RecoverAsync, open: true);
        Action("Sauvegardes locales", MaterialIconKind.DatabaseExportOutline, () => Shell.Open(() => new DataPage(Shell)));
        ActionRefresh();
    }

    public override string Title => "Sauvegarde en ligne";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.CloudLock;

    public ObservableCollection<KpiVm> Stats { get; } = [];
    public ObservableCollection<CloudBackupRowVm> Items { get; } = [];
    public ObservableCollection<CloudRefusalRowVm> Refusals { get; } = [];
    public CloudBackupRowVm? Selected { get => selected; set => Set(ref selected, value); }

    /// <summary>Ce qu'il faut savoir ou faire maintenant (inscription, code à noter, dernier incident).</summary>
    public string Notice { get => notice; private set { if (Set(ref notice, value)) Raise(nameof(HasNotice)); } }
    public bool HasNotice => notice.Length > 0;
    public bool HasRefusals => Refusals.Count > 0;

    private static string Size(long bytes) => bytes >= 1_048_576
        ? (bytes / 1_048_576d).ToString("0.0", CultureInfo.CurrentCulture) + " Mo"
        : Math.Max(1, bytes / 1024).ToString(CultureInfo.CurrentCulture) + " Ko";

    public override async Task LoadAsync()
    {
        status = await Shell.Api.GetAsync<CloudStatusDto>("api/nuage");
        Stats.Clear();
        Items.Clear();
        Refusals.Clear();
        Stats.Add(new KpiVm("Sauvegarde en ligne", !status.Registered ? "Non inscrite" : status.Enabled ? "Active" : "Arrêtée",
            status.Enabled ? "automatique, chiffrée par l'école" : "rien n'est envoyé", ThemeBrush.Get(status.Enabled ? "ToneGreen" : "ToneNeutral"), MaterialIconKind.CloudLock));
        Stats.Add(new KpiVm("Dernière copie", status.LastBackupAt is { } at ? Format.DateTime(at.UtcDateTime) : "—",
            status.LastBackupAt is null ? "pas encore envoyée" : Size(status.LastBackupBytes) + " chiffrés", ThemeBrush.Get("ToneBlue"), MaterialIconKind.CloudUpload));
        Stats.Add(new KpiVm("Opérations reprises", status.Applied.ToString("N0", CultureInfo.CurrentCulture),
            status.Refused > 0 ? $"{status.Refused} refusée(s), motif ci-dessous" : status.LastDrainAt is { } d ? "dernier relevé " + Format.DateTime(d.UtcDateTime) : "aucune pour l'instant",
            ThemeBrush.Get(status.Refused > 0 ? "ToneOrange" : "ToneTeal"), MaterialIconKind.CloudSync));
        Stats.Add(new KpiVm("Code de secours", status.RecoveryConfirmed ? "Noté" : status.KeyFingerprint is null ? "—" : "À noter",
            status.RecoveryConfirmed ? "gardé hors de l'ordinateur" : "indispensable pour restaurer ailleurs", ThemeBrush.Get(status.RecoveryConfirmed ? "ToneGreen" : "ToneRed"), MaterialIconKind.KeyChain));
        foreach (var r in status.Refusals)
            Refusals.Add(new CloudRefusalRowVm(Format.DateTime(r.At.UtcDateTime), r.Label, r.User, r.Reason));
        Raise(nameof(HasRefusals));

        Notice = !status.Registered
            ? "Ce serveur n'est pas encore inscrit auprès de JP-Services (Paramètres › Licence). La sauvegarde en ligne passe par sa console."
            : !status.Enabled
                ? "Activez la sauvegarde en ligne une fois : ensuite, tout se fait seul. La copie est chiffrée ici avec la clé de l'école ; JP-Services ne peut pas la lire."
                : !status.RecoveryConfirmed
                    ? "Notez le code de secours (« Code de secours ») et rangez-le hors de cet ordinateur : sans lui, une copie en ligne ne peut pas être restaurée sur un autre poste."
                    : status.LastError is { Length: > 0 } error
                        ? $"Dernier incident ({(status.LastErrorAt is { } e ? Format.DateTime(e.UtcDateTime) : "—")}) : {error}"
                        : string.Empty;

        if (status.Registered)
        {
            try
            {
                foreach (var b in await Shell.Api.GetAsync<List<CloudBackupDto>>("api/nuage/sauvegardes"))
                    Items.Add(new CloudBackupRowVm(b.Id, Format.DateTime(b.CreatedAt.UtcDateTime), Size(b.SizeBytes),
                        b.SameKey ? "Clé de ce serveur" : "Autre clé : code de secours", b));
            }
            catch (ApiException) { /* Console injoignable : l'état local reste affiché. */ }
        }
        subtitle = status.Enabled
            ? $"{Format.Count(Items.Count, "copie en ligne", "copies en ligne")} · toutes les heures si les données changent"
            : "Arrêtée";
        Raise(nameof(Subtitle));
    }

    private async Task ToggleAsync()
    {
        if (status is null) await LoadAsync();
        if (status is null) return;
        if (!status.Registered)
        {
            Shell.Notify("Inscrivez d'abord ce serveur auprès de JP-Services (Paramètres › Licence).");
            return;
        }
        var enable = !status.Enabled;
        if (!await Shell.Confirm(enable ? "Activer la sauvegarde en ligne" : "Arrêter la sauvegarde en ligne",
                enable
                    ? "Toutes les heures si des données ont changé, et au moins une fois par jour, une copie de la base est compressée, chiffrée ici avec la clé de l'école, puis envoyée à la console JP-Services. Pendant une coupure de ce serveur, les appareils y déposent aussi leurs opérations ; il les applique à son retour. JP-Services voit la date et la taille, jamais le contenu."
                    : "Plus aucune copie ni opération ne sera envoyée. Les copies déjà en ligne restent récupérables avec le code de secours.",
                enable ? "Activer" : "Arrêter", danger: !enable))
            return;
        status = await Shell.Api.SendAsync<CloudStatusDto>(HttpMethod.Post, "api/nuage/activation", new { enabled = enable, confirm = true });
        Shell.Notify(enable ? "Sauvegarde en ligne activée : la première copie part dans quelques minutes." : "Sauvegarde en ligne arrêtée.");
        if (enable && !status.RecoveryConfirmed) await ShowRecoveryCodeAsync();
        await RefreshAsync();
    }

    private async Task ShowRecoveryCodeAsync()
    {
        var code = await Shell.Api.GetAsync<CloudRecoveryCodeDto>("api/nuage/code-secours");
        if (!await Shell.Confirm("Code de secours de l'école",
                "Écrivez ce code sur papier et rangez-le en lieu sûr, hors de cet ordinateur. Il est le seul moyen de déchiffrer les copies en ligne sur un nouvel ordinateur ; JP-Services ne peut pas le retrouver."
                + Environment.NewLine + Environment.NewLine + code.Code,
                "Je l'ai noté"))
            return;
        await Shell.Api.SendAsync<CloudStatusDto>(HttpMethod.Post, "api/nuage/code-secours/confirmation", new { });
        await RefreshAsync();
    }

    private async Task BackupNowAsync()
    {
        await Shell.Api.SendAsync<CloudStatusDto>(HttpMethod.Post, "api/nuage/sauvegarder", new { });
        Shell.Notify("Copie chiffrée envoyée et vérifiée.");
        await RefreshAsync();
    }

    private async Task DrainAsync()
    {
        await Shell.Api.SendAsync(HttpMethod.Post, "api/nuage/releve", new { });
        Shell.Notify("Opérations en ligne relevées.");
        await RefreshAsync();
    }

    private async Task RecoverAsync()
    {
        if (Selected?.Source is not { } copy) return;
        var form = new FormViewModel("Récupérer une copie en ligne",
            $"Copie du {Format.DateTime(copy.CreatedAt.UtcDateTime)}. Elle est déchiffrée ici et rangée parmi les sauvegardes locales ; rien n'est restauré tant que vous ne le décidez pas dans « Sauvegardes ».",
            async f =>
            {
                await Shell.Api.SendAsync(HttpMethod.Post, $"api/nuage/sauvegardes/{copy.Id}/recuperer",
                    new { recoveryCode = string.IsNullOrWhiteSpace(f.Text("code")) ? null : f.Text("code") });
                return true;
            })
        { SubmitText = "Récupérer" };
        form.Add("code", "Code de secours de l'école", required: !copy.SameKey,
            hint: copy.SameKey ? "Facultatif : cette copie a été faite avec la clé de ce serveur." : "Obligatoire : cette copie vient d'un autre ordinateur (14 groupes de 4 caractères).");
        if (!await Shell.Ask(form)) return;
        Shell.Notify("Copie récupérée et vérifiée : elle figure dans « Sauvegardes », prête à être restaurée si besoin.");
        await Shell.Open(() => new DataPage(Shell));
    }
}
