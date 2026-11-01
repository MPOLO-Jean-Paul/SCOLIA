using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>Bouton de format d'un rapport : PDF, Excel ou CSV.</summary>
public sealed record ReportFormatVm(string Label, AsyncCommand Run, MaterialIconKind Icon = MaterialIconKind.Download);

/// <summary>Rapport proposé : ce qu'il contient, ce dont il dépend et ses formats.</summary>
public sealed record ReportCardVm(string Title, string Description, MaterialIconKind Icon, IBrush Brush, string Scope, IReadOnlyList<ReportFormatVm> Formats)
{
    public bool HasScope => Scope.Length > 0;
}

/// <summary>Rubrique de rapports : Finances, Secrétariat, Scolarité, Contrôle.</summary>
public sealed record ReportSectionVm(string Title, IReadOnlyList<ReportCardVm> Reports);

/// <summary>
/// Rapports et exports : tous les documents de SCOLIA réunis au même endroit, selon les droits du compte.
/// La période sert aux reçus et aux dépenses, la promotion à la liste de classe et à la discipline.
/// </summary>
public sealed class ReportsPage : PageViewModel
{
    private DateTime? from;
    private DateTime? to;
    private ChoiceItem? promotion;

    public ReportsPage(ShellViewModel shell) : base(shell)
    {
        Action("Effacer les filtres", MaterialIconKind.FilterRemoveOutline, () =>
        {
            From = null;
            To = null;
            Shell.Notify("Filtres effacés : les documents couvrent toute la période.");
            return Task.CompletedTask;
        });
        Sections = Build();
    }

    public override string Title => "Rapports et exports";
    public override string Subtitle => "Sans période choisie, les reçus et les dépenses sont pris depuis le début";
    public override MaterialIconKind Glyph => MaterialIconKind.FileChartOutline;

    public IReadOnlyList<ReportSectionVm> Sections { get; }
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];
    public ChoiceItem? Promotion { get => promotion; set => Set(ref promotion, value); }
    public DateTime? From { get => from; set => Set(ref from, value); }
    public DateTime? To { get => to; set => Set(ref to, value); }

    public override async Task LoadAsync()
    {
        if (Promotions.Count > 0) return;
        Promotions = await PromotionChoicesAsync(withAll: false);
        Raise(nameof(Promotions));
        Promotion ??= Promotions.Count > 0 ? Promotions[0] : null;
    }

    private IReadOnlyList<ReportSectionVm> Build()
    {
        var sections = new List<ReportSectionVm>();
        void Section(string title, params ReportCardVm?[] cards)
        {
            var list = cards.OfType<ReportCardVm>().ToList();
            if (list.Count > 0) sections.Add(new ReportSectionVm(title, list));
        }

        Section("Finances",
            Shell.Can(Perm.SettingsWrite)
                ? Card(Perm.ReportsRead, "Rapport journalier", "Encaissements, dépenses, inscriptions et absences de la journée, détaillés par promotion. Envoi automatique chaque jour, à l'heure et au numéro choisis.",
                    MaterialIconKind.FileChartOutline, "ToneIndigo", "le jour « Au » ou aujourd'hui",
                    new ReportFormatVm("PDF", Command(DailyPath, "rapport-journalier.pdf")),
                    new ReportFormatVm("Planifier", new AsyncCommand(() => Shell.Run(ScheduleDailyAsync)), MaterialIconKind.CalendarClock),
                    new ReportFormatVm("Envoyer", new AsyncCommand(() => Shell.Run(SendDailyAsync)), MaterialIconKind.SendOutline))
                : Card(Perm.ReportsRead, "Rapport journalier", "Encaissements, dépenses, inscriptions et absences de la journée, détaillés par promotion.",
                    MaterialIconKind.FileChartOutline, "ToneIndigo", "le jour « Au » ou aujourd'hui",
                    new ReportFormatVm("PDF", Command(DailyPath, "rapport-journalier.pdf"))),
            Card(Perm.ReportsRead, "Situation par promotion", "Effectif, attendu à ce jour, encaissé, reste et taux de recouvrement de chaque promotion.",
                MaterialIconKind.ChartBox, "ToneBlue", "", Pdf(() => "api/rapports/promotions/export", "situation-par-promotion"), Excel(() => "api/rapports/promotions/export", "situation-par-promotion")),
            Card(Perm.PaymentsRead, "Journal des encaissements", "Tous les reçus de la période, annulés compris, avec le total valide.",
                MaterialIconKind.ReceiptText, "ToneGreen", "la période", Pdf(() => "api/paiements/export" + Period("from", "to"), "journal-des-encaissements"), Excel(() => "api/paiements/export" + Period("from", "to"), "journal-des-encaissements")),
            Card(Perm.ReportsRead, "Suivi mensuel des paiements", "Pour le mois en cours, qui a payé et qui reste à payer, promotion choisie.",
                MaterialIconKind.CalendarCheck, "ToneIndigo", "la promotion",
                Pdf(() => "api/rapports/suivi-mensuel/export" + ScoliaClient.Query(("promotionId", PromotionId())), "suivi-mensuel-paiements"),
                Excel(() => "api/rapports/suivi-mensuel/export" + ScoliaClient.Query(("promotionId", PromotionId())), "suivi-mensuel-paiements")),
            Card(Perm.ReportsRead, "Retards de paiement", "Apprenants en retard, périodes dues, ancienneté et contact du parent.",
                MaterialIconKind.AlertCircle, "ToneOrange", "", Pdf(() => "api/rapports/debiteurs/export", "retards-de-paiement"), Excel(() => "api/rapports/debiteurs/export", "retards-de-paiement")),
            Card(Perm.PaymentsRead, "Registre des dépenses", "Sorties de caisse et de banque de la période, numérotées, avec leur motif.",
                MaterialIconKind.Wallet, "ToneRed", "la période", Pdf(() => "api/depenses/export" + Period("du", "au"), "registre-des-depenses"), Excel(() => "api/depenses/export" + Period("du", "au"), "registre-des-depenses")));

        Section("Secrétariat",
            Card(Perm.ReportsRead, "Liste générale des apprenants", "Toutes les promotions de l'année, avec statut, contact du parent et solde.",
                MaterialIconKind.AccountGroup, "ToneIndigo", "", Pdf(() => "api/rapports/apprenants/export", "liste-generale-apprenants"), Excel(() => "api/rapports/apprenants/export", "liste-generale-apprenants")),
            Card(Perm.ReportsRead, "Liste de classe", "Liste nominative d'enregistrement de la promotion choisie.",
                MaterialIconKind.FormatListNumbered, "TonePurple", "la promotion", Pdf(ClassList, "liste-de-classe"), Excel(ClassList, "liste-de-classe")));

        Section("Scolarité",
            Card(Perm.AcademicsRead, "Registre de discipline", "Renvois, convocations et suites données, pour la promotion choisie.",
                MaterialIconKind.AlertOctagon, "ToneRed", "la promotion",
                new ReportFormatVm("CSV", Command(() => "api/scolarite/discipline/registre/export.csv" + ScoliaClient.Query(("promotionId", PromotionId())), "registre-discipline.csv"))));

        Section("Contrôle",
            Card(Perm.AuditRead, "Journal d'audit", "Chaque opération enregistrée : qui, quand, quoi. Chaîne d'empreintes vérifiable.",
                MaterialIconKind.ShieldCheck, "ToneNeutral", "",
                new ReportFormatVm("CSV", Command(() => "api/audit/export?format=csv", "journal-audit.csv"))));
        return sections;
    }

    // ---------- Rapport journalier ----------
    private string DailyDay => ToDay(to ?? DateTime.Today).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    private string DailyPath() => $"api/rapports/journalier?date={DailyDay}";

    /// <summary>Heures proposées pour l'envoi automatique : de midi à 22 h, toutes les demi-heures.</summary>
    private static readonly IReadOnlyList<ChoiceItem> DailyTimes = Enumerable.Range(24, 21)
        .Select(i => $"{i / 2:00}:{(i % 2) * 30:00}").Select(t => new ChoiceItem(t, t.Replace(":", " h "))).ToList();

    /// <summary>Envoi automatique : chaque jour, à l'heure choisie, l'ordinateur principal prépare le PDF et l'envoie.</summary>
    private async Task ScheduleDailyAsync()
    {
        var current = await Shell.Api.GetAsync<DailyReportViewDto>("api/rapports/journalier/reglages");
        var status = current.LastStatus is { Length: > 0 } last ? $"Dernier passage : {last}" : "Aucun envoi pour l'instant.";
        var form = new FormViewModel("Programmer le rapport journalier", status, async f =>
        {
            var numbers = f.Text("numeros").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            await Shell.Api.SendAsync<DailyReportViewDto>(HttpMethod.Put, "api/rapports/journalier/reglages", new
            {
                enabled = f.Find("actif").Flag,
                time = f.Find("heure").Choice?.Value ?? "18:00",
                recipients = numbers,
                whatsAppPhoneNumberId = f.Text("phoneId"),
                whatsAppToken = f.Find("jeton").Text,
                clearToken = f.Fields.Any(x => x.Key == "effacer") && f.Find("effacer").Flag,
                whatsAppTemplate = f.Text("modele"),
                whatsAppLanguage = f.Text("langue"),
            });
            Shell.Notify(f.Find("actif").Flag
                ? $"Rapport journalier programmé chaque jour à {f.Find("heure").Choice?.Label}."
                : "Envoi automatique du rapport journalier désactivé.", SoundKind.Success);
            return true;
        });
        form.AddFlag("actif", "Envoyer automatiquement chaque jour", current.Enabled);
        var times = DailyTimes.Any(t => t.Value == current.Time) ? DailyTimes : [.. DailyTimes, new ChoiceItem(current.Time, current.Time)];
        form.AddChoice("heure", "Heure d'envoi", times, current.Time, required: true);
        form.Add("numeros", "Numéros qui reçoivent le rapport", FieldKind.Text, string.Join(", ", current.Recipients),
            hint: "Un ou plusieurs numéros séparés par une virgule : 0812345678, +243991234567.");
        form.AddHeader("Envoi WhatsApp automatique",
            current.WhatsAppReady
                ? "Configuré : le PDF part tout seul à l'heure choisie."
                : "Pour un envoi sans personne, renseignez le compte WhatsApp Business de l'établissement (Meta). Sans lui, le PDF est préparé chaque jour et une notification propose de le partager.");
        form.Add("phoneId", "Identifiant du numéro WhatsApp (Phone number ID)", FieldKind.Text, current.WhatsAppPhoneNumberId);
        form.Add("jeton", current.HasToken ? "Jeton d'accès (déjà enregistré)" : "Jeton d'accès", FieldKind.Secret, null,
            hint: current.HasToken ? "Laissez vide pour garder le jeton enregistré." : "Fourni par Meta for Developers, jamais affiché ensuite.");
        if (current.HasToken) form.AddFlag("effacer", "Effacer le jeton enregistré", false);
        form.Add("modele", "Modèle de message approuvé (facultatif)", FieldKind.Text, current.WhatsAppTemplate,
            hint: "Modèle avec un document en en-tête, approuvé par Meta : il permet d'écrire en premier, à toute heure.");
        form.Add("langue", "Langue du modèle", FieldKind.Text, current.WhatsAppLanguage);
        await Shell.Ask(form);
    }

    /// <summary>Envoyer maintenant : le PDF du jour est fait, envoyé si WhatsApp est configuré, puis ouvert ici.</summary>
    private async Task SendDailyAsync()
    {
        var result = await Shell.Api.SendAsync<DailyReportResultDto>(HttpMethod.Post, $"api/rapports/journalier/envoyer?date={DailyDay}");
        Shell.Notify($"Rapport du {DailyDay} : {result.Status}.", SoundKind.Success);
        await Shell.Download(DailyPath(), result.FileName);
    }

    private ReportCardVm? Card(string perm, string title, string description, MaterialIconKind icon, string tone, string scope, params ReportFormatVm[] formats) =>
        Shell.Can(perm) ? new ReportCardVm(title, description, icon, ThemeBrush.Get(tone), scope, formats) : null;

    private ReportFormatVm Pdf(Func<string> path, string name) => new("PDF", Command(() => With(path(), "pdf"), name + ".pdf"));

    private ReportFormatVm Excel(Func<string> path, string name) => new("Excel", Command(() => With(path(), "xlsx"), name + ".xlsx"));

    private AsyncCommand Command(Func<string> path, string file) => new(() => Shell.Run(() => Shell.Download(path(), file)));

    private static string With(string path, string format) => path + (path.Contains('?') ? "&" : "?") + "format=" + format;

    private string Period(string fromName, string toName) =>
        ScoliaClient.Query((fromName, from is null ? null : ToDay(from)), (toName, to is null ? null : ToDay(to)));

    private Guid? PromotionId() => Promotion is { Value.Length: > 0 } p ? Guid.Parse(p.Value) : null;

    private string ClassList() => PromotionId() is { } id
        ? $"api/rapports/liste-classe/{id}"
        : throw new ApiException("Choisissez d'abord une promotion.", 400);
}
