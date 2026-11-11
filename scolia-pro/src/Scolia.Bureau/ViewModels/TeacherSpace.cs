using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>Enseignant et état de son compte, sur une ligne lisible.</summary>
public sealed record TeacherAccountRowVm(string FullName, string Number, string Function, string Courses, string UserName,
    string State, IBrush StateBrush, string Jury, string LastLogin, TeacherAccountDto Source);

/// <summary>
/// Comptes enseignants (gestion des délibérations) : le secrétariat crée l'enseignant, lui ouvre son compte avec un
/// identifiant proposé et un code provisoire généré, qu'il remplacera à sa première connexion. Même page : nouveau
/// code en cas d'oubli, privilège jury, désactivation. Le code provisoire n'est affiché qu'une fois, ici.
/// </summary>
public sealed class TeacherAccountsPage : PageViewModel
{
    private readonly List<TeacherAccountRowVm> all = [];
    private TeacherAccountRowVm? selected;
    private string search = string.Empty;
    private bool withoutAccount;
    private string subtitle = string.Empty;
    private string credentials = string.Empty;
    private string credentialsTitle = string.Empty;

    public TeacherAccountsPage(ShellViewModel shell) : base(shell)
    {
        if (Shell.Can(Perm.StaffManage))
            Action("Nouvel enseignant", MaterialIconKind.AccountPlus, NewTeacherAsync, primary: true);
        Action("Ouvrir le compte", MaterialIconKind.AccountKey, IssueAsync, open: true);
        Action("Nouveau code provisoire", MaterialIconKind.KeyChange, ResetAsync, row: true);
        Action("Privilège jury", MaterialIconKind.Gavel, ToggleJuryAsync, row: true);
        Action("Activer ou désactiver", MaterialIconKind.AccountCancel, ToggleActiveAsync, row: true);
        if (Shell.Can(Perm.AcademicsRead))
            Action("Attribution des cours", MaterialIconKind.AccountSchool, () => Shell.Open(() => new TeachingPage(Shell)));
        ActionRefresh();
    }

    public override string Title => "Comptes enseignants";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.AccountKey;

    public ObservableCollection<TeacherAccountRowVm> Items { get; } = [];
    public ObservableCollection<KpiVm> Stats { get; } = [];
    public TeacherAccountRowVm? Selected { get => selected; set => Set(ref selected, value); }

    public string Search { get => search; set { if (Set(ref search, value)) Filter(); } }
    public bool WithoutAccount { get => withoutAccount; set { if (Set(ref withoutAccount, value)) Filter(); } }

    /// <summary>Identifiants à remettre en main propre ; effacés en quittant la page, jamais enregistrés sur le poste.</summary>
    public string Credentials { get => credentials; private set { if (Set(ref credentials, value)) Raise(nameof(HasCredentials)); } }
    public string CredentialsTitle { get => credentialsTitle; private set => Set(ref credentialsTitle, value); }
    public bool HasCredentials => credentials.Length > 0;
    public RelayCommand HideCredentials => new(() => Credentials = string.Empty);

    public override async Task LoadAsync()
    {
        var list = await Shell.Api.GetAsync<List<TeacherAccountDto>>("api/comptes-enseignants");
        all.Clear();
        foreach (var t in list) all.Add(Row(t));
        Stats.Clear();
        var accounts = list.Count(t => t.HasAccount && t.AccountActive);
        Stats.Add(new KpiVm("Enseignants", list.Count.ToString("N0", CultureInfo.CurrentCulture), Format.Count(list.Count(t => t.Courses > 0), "a des cours", "ont des cours") + " cette année",
            ThemeBrush.Get("ToneBlue"), MaterialIconKind.AccountSchool));
        Stats.Add(new KpiVm("Comptes actifs", accounts.ToString("N0", CultureInfo.CurrentCulture), Format.Count(list.Count(t => !t.HasAccount || !t.AccountActive), "sans compte actif", "sans compte actif"),
            ThemeBrush.Get("ToneGreen"), MaterialIconKind.AccountKey));
        Stats.Add(new KpiVm("Codes provisoires", list.Count(t => t.AccountActive && t.MustChangePassword).ToString("N0", CultureInfo.CurrentCulture), "à remplacer à la première connexion",
            ThemeBrush.Get("ToneOrange"), MaterialIconKind.KeyChange));
        Stats.Add(new KpiVm("Membres du jury", list.Count(t => t.AccountActive && t.JuryAccess).ToString("N0", CultureInfo.CurrentCulture), "privilège accordé par le secrétariat",
            ThemeBrush.Get("ToneRed"), MaterialIconKind.Gavel));
        subtitle = $"{Format.Count(accounts, "compte actif", "comptes actifs")} · {Format.Count(list.Count, "enseignant", "enseignants")}";
        Raise(nameof(Subtitle));
        Filter();
    }

    private static TeacherAccountRowVm Row(TeacherAccountDto t)
    {
        var (state, tone) = !t.HasAccount ? ("Pas de compte", "ToneNeutral")
            : !t.AccountActive ? ("Désactivé", "ToneNeutral")
            : t.IsLocked ? ("Verrouillé", "ToneRed")
            : t.MustChangePassword ? ("Code provisoire", "ToneOrange")
            : ("Actif", "ToneGreen");
        return new TeacherAccountRowVm(t.FullName, t.Number, t.Function, t.Courses == 0 ? "—" : Format.Count(t.Courses, "cours", "cours"),
            t.UserName ?? "—", state, ThemeBrush.Get(tone), t.AccountActive && t.JuryAccess ? "Membre du jury" : "—",
            t.LastLoginUtc is { } at ? Format.DateTime(at) : t.HasAccount ? "jamais" : "—", t);
    }

    private void Filter()
    {
        var key = search.Trim();
        var keep = Selected?.Source.StaffMemberId;
        Items.Clear();
        foreach (var r in all.Where(r => (!withoutAccount || !r.Source.HasAccount || !r.Source.AccountActive)
                     && (key.Length == 0 || r.FullName.Contains(key, StringComparison.CurrentCultureIgnoreCase)
                         || r.UserName.Contains(key, StringComparison.CurrentCultureIgnoreCase) || r.Number.Contains(key, StringComparison.CurrentCultureIgnoreCase))))
            Items.Add(r);
        Selected = Items.FirstOrDefault(r => r.Source.StaffMemberId == keep);
    }

    private void Show(TeacherCredentialsDto c, string how)
    {
        CredentialsTitle = $"{how} · {c.FullName}";
        Credentials = $"Identifiant : {c.UserName}{Environment.NewLine}Code provisoire : {c.ProvisionalCode}{Environment.NewLine}"
            + "À la première connexion, SCOLIA demande de choisir son propre code PIN ou mot de passe : le code provisoire ne sert qu'une fois "
            + "et ne permet pas d'encoder des cotes.";
    }

    private async Task NewTeacherAsync()
    {
        NewTeacherResult? result = null;
        var form = new FormViewModel("Nouvel enseignant",
            "Crée sa fiche (fonction « Enseignant ») et, si coché, son compte : identifiant proposé « prénom.nom » et code provisoire généré, à lui remettre en main propre.", async f =>
        {
            result = await Shell.Api.SendAsync<NewTeacherResult>(HttpMethod.Post, "api/comptes-enseignants/nouvel-enseignant", new
            {
                lastName = f.Text("nom"), middleName = f.Text("postnom"), firstName = f.Text("prenom"), gender = f.ChoiceValue("sexe"),
                phone = f.Text("telephone"), email = f.Text("email"), function = f.Text("fonction"),
                createAccount = f.Flag("compte"), userName = f.Text("identifiant"), juryAccess = f.Flag("jury"),
            });
            return true;
        });
        form.Add("nom", "Nom", required: true);
        form.Add("postnom", "Post-nom");
        form.Add("prenom", "Prénom", required: true);
        form.AddChoice("sexe", "Sexe", Labels.Genders, "M", required: true);
        form.Add("telephone", "Téléphone", FieldKind.Phone);
        form.Add("email", "E-mail");
        form.Add("fonction", "Fonction", value: "Enseignant", required: true);
        form.AddFlag("compte", "Ouvrir son compte maintenant (identifiant et code provisoire)", true);
        form.Add("identifiant", "Identifiant (facultatif)", hint: "Laissez vide : « prénom.nom » est proposé, sans accents, rendu unique si besoin.");
        form.AddFlag("jury", "Membre du jury : encode aussi les épreuves du jury", false);
        if (!await Shell.Ask(form) || result is not { } created) return;
        if (created.Account is { } account) Show(account, "Compte créé");
        Shell.Notify(created.Account is null ? $"{created.FullName} est enregistré ({created.Number})." : $"Compte de {created.FullName} créé : remettez-lui ses identifiants.");
        await RefreshAsync();
        var staffId = created.StaffMemberId;
        Selected = Items.FirstOrDefault(r => r.Source.StaffMemberId == staffId);
    }

    private async Task IssueAsync()
    {
        if (Selected?.Source is not { } t) return;
        if (t.HasAccount && t.AccountActive)
        {
            Shell.Notify($"{t.FullName} a déjà le compte « {t.UserName} ». S'il a oublié son code : « Nouveau code provisoire ».");
            return;
        }
        TeacherCredentialsDto? issued = null;
        var form = new FormViewModel(t.HasAccount ? "Réactiver le compte" : "Ouvrir le compte",
            t.HasAccount
                ? $"Le compte « {t.UserName} » de {t.FullName} est réactivé avec un nouveau code provisoire."
                : $"{t.FullName} recevra un identifiant et un code provisoire à remplacer à sa première connexion.", async f =>
        {
            issued = await Shell.Api.SendAsync<TeacherCredentialsDto>(HttpMethod.Post, "api/comptes-enseignants", new
            {
                staffMemberId = t.StaffMemberId, userName = f.Text("identifiant"), juryAccess = f.Flag("jury"),
            });
            return true;
        });
        if (!t.HasAccount)
            form.Add("identifiant", "Identifiant (facultatif)", hint: "Laissez vide : « prénom.nom » est proposé, sans accents, rendu unique si besoin.");
        form.AddFlag("jury", "Membre du jury : encode aussi les épreuves du jury", t.JuryAccess);
        if (!await Shell.Ask(form) || issued is not { } given) return;
        Show(given, t.HasAccount ? "Compte réactivé" : "Compte ouvert");
        await RefreshAsync();
    }

    private async Task ResetAsync()
    {
        if (Selected?.Source is not { } t) return;
        if (t.UserId is not { } id || !t.AccountActive)
        {
            Shell.Notify($"{t.FullName} n'a pas de compte actif : « Ouvrir le compte ».");
            return;
        }
        if (!await Shell.Confirm("Nouveau code provisoire",
                $"Le code actuel de « {t.UserName} » cesse de fonctionner et ses sessions ouvertes sont fermées. Un nouveau code provisoire est généré.",
                "Générer le code"))
            return;
        Show(await Shell.Api.SendAsync<TeacherCredentialsDto>(HttpMethod.Post, $"api/comptes-enseignants/{id}/code-provisoire", new { }), "Nouveau code");
        await RefreshAsync();
    }

    private async Task ToggleJuryAsync()
    {
        if (Selected?.Source is not { } t) return;
        if (t.UserId is not { } id || !t.AccountActive)
        {
            Shell.Notify("Le privilège jury s'accorde à un compte enseignant actif.");
            return;
        }
        var grant = !t.JuryAccess;
        if (!await Shell.Confirm(grant ? "Accorder le privilège jury" : "Retirer le privilège jury",
                grant ? $"{t.FullName} pourra encoder les épreuves du jury de toutes les promotions, sans en devenir titulaire."
                      : $"{t.FullName} ne pourra plus encoder les épreuves du jury. Les cotes déjà encodées restent.",
                grant ? "Accorder" : "Retirer", danger: !grant))
            return;
        await Shell.Api.SendAsync(HttpMethod.Put, $"api/comptes-enseignants/{id}", new { isActive = true, juryAccess = grant });
        Shell.Notify(grant ? "Privilège jury accordé." : "Privilège jury retiré ; les cotes encodées restent.");
        await RefreshAsync();
    }

    private async Task ToggleActiveAsync()
    {
        if (Selected?.Source is not { } t) return;
        if (t.UserId is not { } id)
        {
            Shell.Notify($"{t.FullName} n'a pas encore de compte : « Ouvrir le compte ».");
            return;
        }
        if (!t.AccountActive)
        {
            await IssueAsync();
            return;
        }
        if (!await Shell.Confirm("Désactiver le compte",
                $"« {t.UserName} » ne pourra plus se connecter ; ses cotes et remises restent. Vous pourrez le réactiver avec un nouveau code.",
                "Désactiver", danger: true))
            return;
        await Shell.Api.SendAsync(HttpMethod.Put, $"api/comptes-enseignants/{id}", new { isActive = false, juryAccess = false });
        Shell.Notify("Compte désactivé.");
        await RefreshAsync();
    }
}

/// <summary>Cours de l'enseignant, en carte : où il en est et ce qu'il lui reste à faire.</summary>
public sealed record TeacherCourseCardVm(string Promotion, string Title, string Details, string Progress, double Percent,
    string State, IBrush StateBrush, AsyncCommand Open);

public sealed record TeacherNoticeVm(string When, string Title, string Text);

/// <summary>Règle affichée sur Android dans les mêmes lignes que l'accueil du secrétariat.</summary>
public sealed record TeacherRuleRowVm(MaterialIconKind Icon, IBrush Brush, string Text);

/// <summary>
/// Espace enseignant : un accueil à lui, aussi clair que celui du secrétariat. Ses cours et leur avancement,
/// les annonces de la délibération, les règles de l'encodage en vigueur et les actions de son métier ;
/// rien des dossiers, des finances ni des autres cours.
/// </summary>
public sealed class TeacherHomePage : PageViewModel
{
    private string subtitle = string.Empty;
    private string rules = string.Empty;
    private string blocked = string.Empty;

    public TeacherHomePage(ShellViewModel shell) : base(shell)
    {
        Action("Encoder mes cotes", MaterialIconKind.Pen, () => Shell.Open(() => new MarksPage(Shell)), primary: true);
        Action("Choisir mon code d'accès", MaterialIconKind.ShieldLockOutline, () => { Shell.AskForOwnCode(); return Task.CompletedTask; });
        ActionRefresh();
    }

    public override string Title => "Mon espace";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.AccountSchool;

    public string Greeting => $"Bonjour, {Shell.Session?.DisplayName ?? "professeur"}";
    public string Role => Shell.Can(Perm.JuryEncode) ? "Enseignant · membre du jury" : "Enseignant";

    public ObservableCollection<KpiVm> Stats { get; } = [];
    public ObservableCollection<TeacherCourseCardVm> Courses { get; } = [];
    public ObservableCollection<TeacherNoticeVm> Notices { get; } = [];
    public ObservableCollection<TeacherRuleRowVm> RuleItems { get; } = [];

    /// <summary>Règles de l'encodage décidées par le secrétariat (« Paramètres profs »).</summary>
    public string Rules { get => rules; private set => Set(ref rules, value); }

    /// <summary>Raison pour laquelle les cours ne s'ouvrent pas encore (code provisoire à remplacer).</summary>
    public string Blocked { get => blocked; private set { if (Set(ref blocked, value)) { Raise(nameof(IsBlocked)); Raise(nameof(NoCourses)); } } }
    public bool IsBlocked => blocked.Length > 0;
    public bool NoCourses => !IsBlocked && Courses.Count == 0;
    public bool HasNotices => Notices.Count > 0;
    public AsyncCommand ChooseCode => new(() => { Shell.AskForOwnCode(); return Task.CompletedTask; });
    public AsyncCommand EncodeByCode => new(() => Shell.Run(() => Shell.Open(() => new MarksPage(Shell))));

    public override async Task LoadAsync()
    {
        Raise(nameof(Greeting));
        Raise(nameof(Role));
        Courses.Clear();
        Notices.Clear();
        Stats.Clear();
        RuleItems.Clear();
        var settings = await Shell.Api.GetAsync<DeliberationSettingsDto>("api/deliberation/reglages");
        Rules = string.Join(Environment.NewLine,
            $"• Encodage du S1 : {(settings.EncodingS1Open ? "ouvert" : "fermé")} — du S2 : {(settings.EncodingS2Open ? "ouvert" : "fermé")}.",
            $"• Moyennes d'appui (sur 10) : {(settings.SupportEnabled ? "actives" : "désactivées")}.",
            $"• Cotes déjà enregistrées : {(settings.TeacherUpdates ? "vous pouvez les remplacer tant que la feuille n'est pas remise" : "le secrétariat les corrige")}.",
            $"• Téléchargement des relevés de vos cours : {(settings.TeacherDownloads ? "autorisé" : "fermé")}.",
            $"• Seuil de réussite : {settings.PassPercent:0.##} % ; échec sous la moitié du maxima d'un cours.",
            "• Vos feuilles montrent les codes anonymes des copies, jamais les noms.");
        // Le mobile reprend la liste de réglages partagée avec l'accueil
        // administrateur : une ligne courte, une icône du thème, sans nouvelles couleurs.
        RuleItems.Add(new TeacherRuleRowVm(MaterialIconKind.CalendarCheck, ThemeBrush.Get("ToneBlue"),
            $"S1 : {(settings.EncodingS1Open ? "ouvert" : "fermé")} · S2 : {(settings.EncodingS2Open ? "ouvert" : "fermé")}"));
        RuleItems.Add(new TeacherRuleRowVm(MaterialIconKind.ChartBar, ThemeBrush.Get("TonePurple"),
            $"Moyennes d'appui (sur 10) : {(settings.SupportEnabled ? "actives" : "désactivées")}"));
        RuleItems.Add(new TeacherRuleRowVm(MaterialIconKind.Pen, ThemeBrush.Get("ToneOrange"),
            settings.TeacherUpdates ? "Cotes enregistrées : modification autorisée avant remise" : "Cotes enregistrées : correction par le secrétariat"));
        RuleItems.Add(new TeacherRuleRowVm(MaterialIconKind.FileChart, ThemeBrush.Get("ToneGreen"),
            $"Téléchargement des relevés : {(settings.TeacherDownloads ? "autorisé" : "fermé")}"));
        RuleItems.Add(new TeacherRuleRowVm(MaterialIconKind.ShieldCheckOutline, ThemeBrush.Get("ToneTeal"),
            $"Seuil de réussite : {settings.PassPercent:0.##} % · échec sous la moitié du maxima"));
        RuleItems.Add(new TeacherRuleRowVm(MaterialIconKind.Incognito, ThemeBrush.Get("ToneIndigo"),
            "Feuilles anonymes : codes des copies, jamais les noms"));
        List<TeacherCourseDto> mine;
        try
        {
            mine = await Shell.Api.GetAsync<List<TeacherCourseDto>>("api/deliberation/mes-cours");
            Blocked = string.Empty;
        }
        catch (ApiException ex) when (ex.Status == 403)
        {
            Blocked = ex.Message;
            subtitle = "Code d'accès provisoire";
            Raise(nameof(Subtitle));
            return;
        }
        foreach (var notice in (await Shell.Api.GetAsync<List<AnnouncementDto>>("api/deliberation/annonces")).Take(6))
            Notices.Add(new TeacherNoticeVm(notice.AtUtc.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.CurrentCulture), notice.Title, notice.Text));
        Raise(nameof(HasNotices));

        int expected = 0, graded = 0, sheets = 0, submitted = 0;
        foreach (var m in mine)
        {
            var c = m.Course;
            var (done, total, sheetCount, handed) = c.Semester switch
            {
                "JuryEpreuve" or "JuryBloc" => (c.GradedJury, c.Students, 0, 0),
                "S1" => (c.GradedS1, c.Students, 1, c.SubmittedS1 ? 1 : 0),
                "S2" => (c.GradedS2, c.Students, 1, c.SubmittedS2 ? 1 : 0),
                _ => (c.GradedS1 + c.GradedS2, 2 * c.Students, 2, (c.SubmittedS1 ? 1 : 0) + (c.SubmittedS2 ? 1 : 0)),
            };
            expected += total; graded += done; sheets += sheetCount; submitted += handed;
            var percent = total == 0 ? 0 : 100.0 * done / total;
            var jury = c.Semester is "JuryEpreuve" or "JuryBloc";
            var (state, tone) = c.LockedS1 || c.LockedS2 ? ("Délibéré", "ToneIndigo")
                : sheetCount > 0 && handed == sheetCount ? ("Remis", "ToneGreen")
                : total > 0 && done == total ? (jury ? "Complet" : "À remettre", jury ? "ToneGreen" : "ToneTeal")
                : done == 0 ? ("À encoder", "ToneOrange")
                : ("En cours", "ToneBlue");
            var promotionId = m.PromotionId;
            var courseId = c.Id;
            Courses.Add(new TeacherCourseCardVm(m.Promotion, $"{c.Number}. {c.Name}",
                $"{Labels.Semester(c.Semester)} · sur {c.MaxScore:0.##} · {Format.Count(c.Students, "copie", "copies")}",
                $"{done}/{total} cotes · {percent:0} %", percent, state, ThemeBrush.Get(tone),
                new AsyncCommand(() => Shell.Run(() => Shell.Open(() => new MarksPage(Shell, promotionId, courseId))))));
        }
        Raise(nameof(NoCourses));
        var share = expected == 0 ? 0 : 100.0 * graded / expected;
        Stats.Add(new KpiVm("Mes cours", mine.Count.ToString("N0", CultureInfo.CurrentCulture),
            Format.Count(mine.Select(m => m.PromotionId).Distinct().Count(), "promotion", "promotions"), ThemeBrush.Get("TonePurple"), MaterialIconKind.BookOpenVariant));
        Stats.Add(new KpiVm("Cotes encodées", $"{share:0} %", $"{graded.ToString("N0", CultureInfo.CurrentCulture)} sur {expected.ToString("N0", CultureInfo.CurrentCulture)}",
            ThemeBrush.Get("ToneBlue"), MaterialIconKind.Pen));
        Stats.Add(new KpiVm("Feuilles remises", $"{submitted}/{sheets}", sheets == submitted && sheets > 0 ? "tout est remis" : "remettez chaque feuille complète",
            ThemeBrush.Get("ToneGreen"), MaterialIconKind.SendOutline));
        Stats.Add(new KpiVm("Encodage", (settings.EncodingS1Open, settings.EncodingS2Open) switch
        {
            (true, true) => "S1 et S2",
            (true, false) => "S1 ouvert",
            (false, true) => "S2 ouvert",
            _ => "Fermé",
        }, "décidé par le secrétariat", ThemeBrush.Get("ToneOrange"), MaterialIconKind.LockOpenVariantOutline));
        subtitle = mine.Count == 0 ? "Aucun cours ne vous est attribué cette année" : $"{Format.Count(mine.Count, "cours", "cours")} · {share:0} % encodé";
        Raise(nameof(Subtitle));
    }
}
