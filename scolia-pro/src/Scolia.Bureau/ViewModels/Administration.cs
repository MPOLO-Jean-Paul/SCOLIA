using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;
using Scolia.Domain.School;

namespace Scolia.Bureau.ViewModels;

/// <summary>Membre du personnel, mis en forme pour la liste.</summary>
public sealed record StaffRowVm(Guid Id, string Number, string Name, string Function, string Phone, string Hired, string Salary, string Status, IBrush StatusBrush, StaffDto Source);

/// <summary>Bulletin de paie d'un membre pour une période.</summary>
public sealed record PayrollRowVm(Guid Id, string Number, string Name, string Function, string Base, string Allowances, string Deductions, string Advances, string Net, string Status, IBrush StatusBrush, string PaidOn, PayrollDto Source);

/// <summary>Personnel et paie : dossiers des membres, bulletins mensuels et paiement des salaires.</summary>
public sealed class StaffPage : PageViewModel
{
    private string period = DateTime.Today.ToString("yyyy-MM");
    private ChoiceItem? periodChoice;
    private bool showPayroll;
    private StaffRowVm? selectedStaff;
    private PayrollRowVm? selectedPayroll;
    private string subtitle = string.Empty;

    public StaffPage(ShellViewModel shell) : base(shell)
    {
        Action("Nouveau membre", MaterialIconKind.AccountPlus, () => NewMemberAsync(Shell), primary: true);
        Action("Modifier le membre", MaterialIconKind.AccountEdit, EditMemberAsync, open: true);
        Action("Préparer la paie", MaterialIconKind.CalendarPlus, PreparePayrollAsync);
        Action("Pointage et bilan", MaterialIconKind.ClockCheck, () => Shell.Open(() => new StaffAttendancePage(Shell)));
        Action("Modifier le bulletin", MaterialIconKind.Pencil, EditPayslipAsync, row: true);
        Action("Payer le salaire", MaterialIconKind.CashCheck, PayAsync, row: true);
        periodChoice = Periods.FirstOrDefault(p => p.Value == period);
        ActionRefresh();
    }

    public override string Title => "Personnel et paie";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.BriefcaseOutline;

    public ObservableCollection<StaffRowVm> Staff { get; } = [];
    public ObservableCollection<PayrollRowVm> Payroll { get; } = [];

    /// <summary>Vrai pour afficher la paie, faux pour les dossiers du personnel.</summary>
    public bool ShowPayroll
    {
        get => showPayroll;
        set
        {
            if (!Set(ref showPayroll, value)) return;
            RaiseEmptyStates();
            _ = Shell.Run(RefreshAsync);
        }
    }

    /// <summary>Mois de paie au format aaaa-mm.</summary>
    public string Period
    {
        get => period;
        set
        {
            if (!Set(ref period, value.Trim())) return;
            periodChoice = Periods.FirstOrDefault(p => p.Value == period);
            Raise(nameof(PeriodChoice));
            if (ShowPayroll) _ = Shell.Run(RefreshAsync);
        }
    }

    /// <summary>Mois de paie choisi dans la liste : les douze derniers mois et le mois suivant.</summary>
    public IReadOnlyList<ChoiceItem> Periods { get; } = PayrollMonths();

    public ChoiceItem? PeriodChoice
    {
        get => periodChoice;
        set { if (value is not null) Period = value.Value; }
    }

    public bool StaffEmpty => !ShowPayroll && Staff.Count == 0;
    public bool PayrollEmpty => ShowPayroll && Payroll.Count == 0;

    private void RaiseEmptyStates()
    {
        Raise(nameof(StaffEmpty));
        Raise(nameof(PayrollEmpty));
    }

    private static IReadOnlyList<ChoiceItem> PayrollMonths()
    {
        var first = DateOnly.FromDateTime(DateTime.Today).AddMonths(-11);
        var list = new List<ChoiceItem>();
        for (var i = 0; i < 13; i++)
        {
            var month = first.AddMonths(i);
            list.Add(new ChoiceItem(month.ToString("yyyy-MM", CultureInfo.InvariantCulture), MonthLabel(month.ToString("yyyy-MM", CultureInfo.InvariantCulture))));
        }
        return list;
    }

    /// <summary>« 2026-10 » devient « octobre 2026 ».</summary>
    public static string MonthLabel(string key)
    {
        var date = DateOnly.ParseExact(key + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var name = CultureInfo.GetCultureInfo("fr-FR").DateTimeFormat.GetMonthName(date.Month);
        return $"{char.ToUpperInvariant(name[0])}{name[1..]} {date.Year}";
    }

    public StaffRowVm? SelectedStaff { get => selectedStaff; set => Set(ref selectedStaff, value); }
    public PayrollRowVm? SelectedPayroll { get => selectedPayroll; set => Set(ref selectedPayroll, value); }

    public override async Task LoadAsync()
    {
        if (ShowPayroll)
        {
            if (!TryPeriod(out var key))
            {
                Shell.Notify("Le mois doit être écrit ainsi : 2026-10.");
                return;
            }
            var rows = await Shell.Api.GetAsync<List<PayrollDto>>($"api/personnel/paie/{key}");
            Payroll.Clear();
            foreach (var p in rows)
                Payroll.Add(new PayrollRowVm(p.Id, p.Number, p.FullName, p.Function, Money(p.BaseSalary), Money(p.Allowances), Money(p.Deductions),
                    Money(p.Advances), Money(p.Net), Labels.Status(p.Status), ThemeBrush.Get(p.Status == "Paye" ? "ToneGreen" : "ToneOrange"),
                    p.PaidOn is { } d ? Format.Date(d) : "—", p));
            var month = MonthLabel(key).ToLowerInvariant();
            // « Paie d'avril », « Paie d'août », « Paie d'octobre », mais « Paie de novembre ».
            var of = month.Length > 0 && "aeiouyéèâàô".Contains(month[0]) ? "d'" : "de ";
            subtitle = $"Paie {of}{month} · {Format.Count(rows.Count, "bulletin", "bulletins")} · net {Money(rows.Sum(r => r.Net))}";
        }
        else
        {
            var rows = await Shell.Api.GetAsync<List<StaffDto>>("api/personnel");
            Staff.Clear();
            foreach (var s in rows)
                Staff.Add(new StaffRowVm(s.Id, s.Number, Format.FullName(s.LastName, s.MiddleName, s.FirstName), s.Function, s.Phone ?? "—",
                    Format.Date(s.HireDate), Money(s.BaseSalary), s.IsActive ? "Actif" : "Inactif", ThemeBrush.Get(s.IsActive ? "ToneGreen" : "ToneNeutral"), s));
            subtitle = $"{Format.Count(rows.Count(r => r.IsActive), "membre actif", "membres actifs")} sur {rows.Count}";
        }
        RaiseEmptyStates();
        Raise(nameof(Subtitle));
    }

    private bool TryPeriod(out string key)
    {
        key = Period;
        return DateOnly.TryParseExact(Period + "-01", "yyyy-MM-dd", out _);
    }

    /// <summary>Formulaire d'un nouveau membre du personnel.</summary>
    public static async Task NewMemberAsync(ShellViewModel shell)
    {
        var form = new FormViewModel("Nouveau membre", "Le numéro de dossier est attribué automatiquement.", async f =>
        {
            await shell.Api.SendAsync(HttpMethod.Post, "api/personnel", StaffBody(f));
            shell.Notify("Membre ajouté au personnel.");
            return true;
        });
        AddStaffFields(form, null);
        if (await shell.Ask(form) && shell.Current is StaffPage page) await page.RefreshAsync();
    }

    private async Task EditMemberAsync()
    {
        if (SelectedStaff is null)
        {
            Shell.Notify("Sélectionnez un membre du personnel.");
            return;
        }
        var s = SelectedStaff.Source;
        var form = new FormViewModel("Modifier le membre", $"Dossier {s.Number}", async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Put, $"api/personnel/{s.Id}", StaffBody(f));
            Shell.Notify("Dossier enregistré.");
            return true;
        });
        AddStaffFields(form, s);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task PreparePayrollAsync()
    {
        if (!TryPeriod(out var key))
        {
            Shell.Notify("Le mois doit être écrit ainsi : 2026-10.");
            return;
        }
        var result = await Shell.Api.SendAsync<Dictionary<string, int>>(HttpMethod.Post, $"api/personnel/paie/{key}/preparation");
        Shell.Notify($"{result.GetValueOrDefault("crees")} bulletin(s) préparé(s) pour {key}.");
        await RefreshAsync();
    }

    private async Task EditPayslipAsync()
    {
        if (SelectedPayroll is null)
        {
            Shell.Notify("Sélectionnez un bulletin.");
            return;
        }
        var p = SelectedPayroll.Source;
        if (p.Status == "Paye")
        {
            Shell.Notify("Un bulletin payé ne se modifie plus.");
            return;
        }
        var form = new FormViewModel("Modifier le bulletin", $"{p.FullName} · salaire de base {Money(p.BaseSalary)}", async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Put, $"api/personnel/paie/bulletins/{p.Id}", new
            {
                allowances = f.Amount("primes"),
                deductions = f.Amount("retenues"),
                advances = f.Amount("avances"),
                note = f.OptionalText("note"),
            });
            Shell.Notify("Bulletin mis à jour.");
            return true;
        });
        form.Add("primes", "Primes et indemnités", FieldKind.Amount, p.Allowances.ToString("0"));
        form.Add("retenues", "Retenues", FieldKind.Amount, p.Deductions.ToString("0"));
        form.Add("avances", "Avances déjà versées", FieldKind.Amount, p.Advances.ToString("0"));
        form.Add("note", "Observation", FieldKind.Multiline, p.Note);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task PayAsync()
    {
        if (SelectedPayroll is null)
        {
            Shell.Notify("Sélectionnez un bulletin à payer.");
            return;
        }
        var p = SelectedPayroll.Source;
        if (p.Status == "Paye")
        {
            Shell.Notify("Ce salaire est déjà payé.");
            return;
        }
        var form = new FormViewModel("Payer le salaire", $"{p.FullName} · net à payer {Money(p.Net)}", async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Post, $"api/personnel/paie/bulletins/{p.Id}/paiement" + ScoliaClient.Query(("mode", f.ChoiceValue("mode"))));
            Shell.Notify($"Salaire de {p.FullName} payé.");
            return true;
        })
        { SubmitText = "Payer" };
        form.AddChoice("mode", "Mode de paiement", Labels.PayrollModes, "Especes", required: true);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private static void AddStaffFields(FormViewModel form, StaffDto? s)
    {
        form.Add("nom", "Nom", value: s?.LastName, required: true);
        form.Add("postNom", "Post-nom", value: s?.MiddleName);
        form.Add("prenom", "Prénom", value: s?.FirstName, required: true);
        form.AddChoice("sexe", "Sexe", Labels.Genders, s?.Gender ?? "M", required: true);
        form.Add("fonction", "Fonction", value: s?.Function, required: true);
        form.Add("telephone", "Téléphone", FieldKind.Phone, value: s?.Phone, hint: "10 chiffres, ex. 0812345678");
        form.Add("email", "E-mail", value: s?.Email);
        form.AddDay("embauche", "Date d'embauche", s?.HireDate ?? DateOnly.FromDateTime(DateTime.Today), required: true);
        form.AddDay("sortie", "Fin d'activité (facultatif)", s?.ExitDate);
        form.Add("salaire", "Salaire de base", FieldKind.Amount, s is null ? string.Empty : s.BaseSalary.ToString("0"), required: true);
        form.AddFlag("actif", "Membre actif", s?.IsActive ?? true);
    }

    private static object StaffBody(FormViewModel f) => new
    {
        lastName = f.Text("nom"),
        middleName = f.Text("postNom"),
        firstName = f.Text("prenom"),
        gender = f.ChoiceValue("sexe") ?? "M",
        function = f.Text("fonction"),
        phone = f.OptionalText("telephone"),
        email = f.OptionalText("email"),
        hireDate = f.Day("embauche"),
        exitDate = f.Day("sortie"),
        baseSalary = f.Amount("salaire"),
        isActive = f.Flag("actif"),
    };
}

/// <summary>Année scolaire dans la liste des paramètres.</summary>
public sealed record YearRowVm(Guid Id, string Label, string Period, string Status, bool IsCurrent, string Months,
    IBrush StatusBrush, string Deadline, AcademicYearDto Source);

/// <summary>Section d'enseignement dans la liste des paramètres.</summary>
public sealed record SectionRowVm(Guid Id, string Code, string Name, string Active, SectionDto Source, string Promotions = "", string Students = "")
{
    public string StatusLabel => Source.IsActive ? "Active" : "Inactive";
    public IBrush StatusBrush => ThemeBrush.Get(Source.IsActive ? "ToneGreen" : "ToneOrange");
}

/// <summary>Catégorie de frais du catalogue : nom, montant, échéance proposée et promotions qui l'appliquent.</summary>
public sealed record FeeCategoryRowVm(Guid Id, string Name, string Amount, string DueDate, string Promotions, string Active, FeeCategoryDto Source);

/// <summary>Promotion dans la liste des paramètres, avec ses frais.</summary>
public sealed record PromotionRowVm(Guid Id, string Code, string Name, string Section, string Level, string Students, string Monthly, string Annual, string Active, PromotionDto Source);

/// <summary>Paramètres de l'établissement : identité, années scolaires, sections et promotions.</summary>
/// <summary>Pastille d'une couleur d'accent : un clic l'applique, une coche marque la couleur choisie.</summary>
public sealed class AccentSwatchVm(string key, string label, IBrush brush, RelayCommand choose, Func<bool> isSelected) : Observable
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    public IBrush Brush { get; } = brush;
    public RelayCommand Choose { get; } = choose;
    public bool IsSelected => isSelected();
    public void Refresh() => Raise(nameof(IsSelected));
}

public sealed class SettingsPage : PageViewModel
{
    private BackstageItemVm? section;
    private YearRowVm? selectedYear;
    private SectionRowVm? selectedSection;
    private PromotionRowVm? selectedPromotion;
    private FeeCategoryRowVm? selectedCategory;
    private string subtitle = string.Empty;
    private FormViewModel? schoolForm;
    private bool mobileDetailsShown;

    /// <summary>Android : une première liste de rubriques, puis une page de réglages.</summary>
    public bool MobileCategoriesVisible => Phone && !mobileDetailsShown;
    public bool SettingsDetailVisible => !Phone || mobileDetailsShown;
    public RelayCommand<BackstageItemVm> OpenMobileSection { get; }
    public RelayCommand BackToMobileSections { get; }
    /// <summary>Actions directes, accessibles sans le menu « … » sur Android.</summary>
    public AsyncCommand CreateSection { get; }
    public RelayCommand<SectionRowVm> EditSectionOnPhone { get; }
    public AsyncCommand CreatePromotion { get; }
    public RelayCommand<PromotionRowVm> EditPromotionOnPhone { get; }

    /// <summary>Le bouton retour système Android revient d'abord aux familles de réglages.</summary>
    public bool TryBackToMobileSections()
    {
        if (!Phone || !mobileDetailsShown) return false;
        mobileDetailsShown = false;
        RefreshMobileSectionVisibility();
        return true;
    }

    private void RefreshMobileSectionVisibility()
    {
        Raise(nameof(MobileCategoriesVisible));
        Raise(nameof(SettingsDetailVisible));
    }

    public SettingsPage(ShellViewModel shell, string? open = null) : base(shell)
    {
        // Rubriques avec leur icône, comme la liste des Réglages de macOS : l'établissement pour le secrétariat,
        // puis les réglages de ce poste, ouverts à tous les comptes.
        SchoolSections = Shell.Can(Perm.SettingsWrite)
            ?
            [
                new BackstageItemVm("etablissement", "Établissement", MaterialIconKind.OfficeBuilding, "ToneNeutral"),
                new BackstageItemVm("annees", "Années scolaires", MaterialIconKind.Calendar, "ToneRed"),
                new BackstageItemVm("sections", "Sections", MaterialIconKind.ViewGrid, "ToneIndigo"),
                new BackstageItemVm("promotions", "Promotions et frais", MaterialIconKind.School, "ToneBlue"),
                new BackstageItemVm("frais", "Catégories de frais", MaterialIconKind.TagMultipleOutline, "ToneGreen"),
            ]
            : [];
        if (Shell.IsServerComputer && Shell.Can(Perm.SettingsWrite))
            SchoolSections = [.. SchoolSections,
                new BackstageItemVm("licence", "Licence SCOLIA Pro",
                    MaterialIconKind.ShieldLockOutline, "ToneBlue")];
        PosteSections =
        [
            new BackstageItemVm("apparence", "Thème et couleurs", MaterialIconKind.Palette, "TonePurple"),
            new BackstageItemVm("texte", "Texte et polices", MaterialIconKind.FormatFont, "ToneBlue"),
            new BackstageItemVm("fenetres", "Fenêtres et affichage", MaterialIconKind.WindowMaximize, "ToneTeal"),
            new BackstageItemVm("animations", "Animations et fluidité", MaterialIconKind.AnimationPlay, "ToneOrange"),
            new BackstageItemVm("sons", "Sons", MaterialIconKind.VolumeHigh, "TonePink"),
        ];
        // Sur Windows, la sécurité et les informations du logiciel appartiennent
        // explicitement aux Paramètres (pas au menu Compte).
        if (!Preferences.MobileLayout)
            PosteSections =
            [
                .. PosteSections,
                new BackstageItemVm("securite", "Sécurité du compte", MaterialIconKind.ShieldLockOutline, "ToneOrange"),
                new BackstageItemVm("apropos", "À propos et mises à jour", MaterialIconKind.InformationOutline, "ToneIndigo"),
            ];
        else
        {
            PosteSections = [.. PosteSections.Where(s => s.Value != "fenetres")];
            // Conserver le parcours historique Android (mot de passe et biométrie).
            if (open == "securite")
            {
                open = null;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Shell.OpenBackstage();
                    Shell.Backstage.Section = Shell.Backstage.Items.First(i => i.Value == "mot-de-passe");
                });
            }
        }
        var all = SchoolSections.Concat(PosteSections).ToList();
        section = all.FirstOrDefault(s => s.Value == open) ?? all[0];
        // Ouverture ciblée depuis une autre fonctionnalité : afficher directement la rubrique.
        mobileDetailsShown = open is not null || !Phone;
        OpenMobileSection = new RelayCommand<BackstageItemVm>(item =>
        {
            if (item is null) return;
            mobileDetailsShown = true;
            Section = item;
            RefreshMobileSectionVisibility();
        });
        BackToMobileSections = new RelayCommand(() => TryBackToMobileSections());
        CreateSection = new AsyncCommand(() => Shell.Run(NewSectionAsync));
        EditSectionOnPhone = new RelayCommand<SectionRowVm>(row =>
        {
            if (row is null) return;
            SelectedSection = row;
            _ = Shell.Run(() => EditSectionAsync(row.Source));
        });
        CreatePromotion = new AsyncCommand(() => Shell.Run(() => NewPromotionAsync(Shell)));
        EditPromotionOnPhone = new RelayCommand<PromotionRowVm>(row =>
        {
            if (row is null) return;
            SelectedPromotion = row;
            _ = Shell.Run(() => PromotionFormAsync(Shell, row.Source));
        });
        OpenAccountSecurity = new RelayCommand(() =>
        {
            if (!Preferences.MobileLayout)
                Section = PosteSections.First(item => item.Value == "securite");
            else
            {
                Shell.OpenBackstage();
                Shell.Backstage.Section = Shell.Backstage.Items.First(item => item.Value == "mot-de-passe");
            }
        });
        AboutModel = new AboutViewModel(shell);
        if (section?.Value == "securite" && !Preferences.MobileLayout) ActivateSecurity();
        Swatches = Preferences.AccentSwatches.Select(a => new AccentSwatchVm(a.Key, a.Label, new SolidColorBrush(a.Color),
            new RelayCommand(() => { Prefs.Accent = a.Key; RaiseSwatches(); }), () => Prefs.Accent == a.Key)).ToList();
        ResetPrefs = new RelayCommand(() => { Prefs.Reset(); RaisePrefs(); Shell.Notify("Réglages de ce poste rétablis."); });
        TestSound = new RelayCommand<string>(kind => Sounds.Play(Prefs, Enum.TryParse<SoundKind>(kind, out var k) ? k : SoundKind.Notification, force: true));
        RebuildActions();
    }

    // ---------- Réglages de ce poste : chaque changement s'applique tout de suite et reste enregistré ----------
    public Preferences Prefs => Shell.Prefs;
    public IReadOnlyList<AccentSwatchVm> Swatches { get; }
    public RelayCommand ResetPrefs { get; }
    public RelayCommand OpenAccountSecurity { get; }
    public BackstageViewModel SecurityModel => Shell.Backstage;
    public AboutViewModel AboutModel { get; }
    public SchoolLicenseViewModel License => Shell.License;
    public bool CanCheckWindowsUpdates => Shell.Backstage.CanCheckWindowsUpdates;
    public AsyncCommand CheckWindowsUpdates => Shell.Backstage.CheckWindowsUpdates;
    private void ActivateSecurity() =>
        Shell.Backstage.Section = new BackstageItemVm("mot-de-passe", "Sécurité du compte",
            MaterialIconKind.ShieldLockOutline, "ToneOrange");
    public RelayCommand<string> TestSound { get; }

    /// <summary>Montre une bannière d'exemple : la personne voit ce qu'elle recevra d'un autre poste.</summary>
    public RelayCommand TestAlert => testAlert ??= new RelayCommand(() => Shell.ShowAlert(new AppAlert(AlertKind.Payment,
        "Nouvel encaissement", "Exemple : reçu R-000123 de 50 000 FC pour ACC101 (Espèces).", "Secrétariat · PC-ACCUEIL")));
    private RelayCommand? testAlert;
    public IReadOnlyList<ChoiceItem> Themes => Preferences.Themes;
    public IReadOnlyList<ChoiceItem> Fonts => Preferences.Fonts;
    public IReadOnlyList<ChoiceItem> TextSizes => Preferences.TextSizes;
    public IReadOnlyList<ChoiceItem> StartupModes => Preferences.StartupModes;
    public IReadOnlyList<ChoiceItem> Speeds => Preferences.Speeds;
    public IReadOnlyList<ChoiceItem> OpeningDurations => Preferences.OpeningDurations;
    /// <summary>Sur un téléphone, il n'y a pas de sons de Windows : seuls ceux de SCOLIA sont proposés.</summary>
    public IReadOnlyList<ChoiceItem> SoundStyles => Platform.IsMobile ? Preferences.SoundStyles.Where(s => s.Value != "systeme").ToList() : Preferences.SoundStyles;

    // Textes qui nomment le système : Windows sur l'ordinateur, l'appareil sur un téléphone.
    public string SystemThemeLabel => Platform.IsMobile ? "Selon l'appareil" : "Selon Windows";
    public string FontHint => Platform.IsMobile
        ? "Toutes les polices sont fournies avec SCOLIA."
        : "Celles de Windows, ou celles fournies avec SCOLIA.";
    public string VolumeHint => Platform.IsMobile ? "Sans effet sur le volume du téléphone." : "Sans effet sur le volume de Windows.";
    public string SoundStyleHint => Platform.IsMobile ? "Doux ou clochette : deux jeux de sons de SCOLIA." : "Doux et clochette sont des sons de SCOLIA ; ou les sons habituels de Windows.";

    /// <summary>Nom de l'établissement dans l'aperçu du texte.</summary>
    public string PreviewSchool => Shell.SchoolLine.Split(" · ")[0] is { Length: > 0 } name ? name : "Votre établissement";

    private static ChoiceItem? Pick(IReadOnlyList<ChoiceItem> list, string value) => list.FirstOrDefault(c => c.Value == value);

    public ChoiceItem? Theme { get => Pick(Themes, Prefs.Theme); set { if (value is not null) { Prefs.Theme = value.Value; RaiseThemes(); } } }
    public bool ThemeSystem { get => Prefs.Theme == "system"; set { if (value) { Prefs.Theme = "system"; RaiseThemes(); } } }
    public bool ThemeLight { get => Prefs.Theme == "light"; set { if (value) { Prefs.Theme = "light"; RaiseThemes(); } } }
    public bool ThemeDark { get => Prefs.Theme == "dark"; set { if (value) { Prefs.Theme = "dark"; RaiseThemes(); } } }
    public ChoiceItem? Font { get => Pick(Fonts, Prefs.Font); set { if (value is not null) Prefs.Font = value.Value; } }
    public ChoiceItem? Startup { get => Pick(StartupModes, Prefs.Startup); set { if (value is not null) Prefs.Startup = value.Value; } }
    public ChoiceItem? Speed { get => Pick(Speeds, Prefs.Speed); set { if (value is not null) Prefs.Speed = value.Value; } }
    public ChoiceItem? OpeningDuration
    {
        get => Pick(OpeningDurations, Prefs.OpeningSeconds.ToString(CultureInfo.InvariantCulture));
        set { if (value is not null) Prefs.OpeningSeconds = int.Parse(value.Value, CultureInfo.InvariantCulture); }
    }
    public ChoiceItem? SoundStyle { get => Pick(SoundStyles, Prefs.SoundStyle); set { if (value is not null) Prefs.SoundStyle = value.Value; } }
    /// <summary>Taille du texte de 80 à 160 %, réglée au curseur.</summary>
    public double TextScale { get => Prefs.Scale; set { Prefs.Scale = value; Raise(nameof(TextScaleLabel)); } }
    public string TextScaleLabel => Prefs.ZoomLabel;
    public double Volume { get => Prefs.SoundVolume; set { Prefs.SoundVolume = (int)Math.Round(value); Raise(nameof(VolumeLabel)); } }
    public string VolumeLabel => Prefs.VolumeLabel;

    private void RaiseThemes()
    {
        Raise(nameof(Theme)); Raise(nameof(ThemeSystem)); Raise(nameof(ThemeLight)); Raise(nameof(ThemeDark));
    }

    private void RaiseSwatches()
    {
        foreach (var s in Swatches) s.Refresh();
    }

    private void RaisePrefs()
    {
        RaiseThemes();
        RaiseSwatches();
        foreach (var name in new[] { nameof(Font), nameof(Startup), nameof(Speed), nameof(OpeningDuration), nameof(SoundStyle), nameof(TextScale), nameof(TextScaleLabel), nameof(Volume), nameof(VolumeLabel), nameof(Prefs) })
            Raise(name);
    }

    /// <summary>Affiche dans l'en-tête les seules actions de la rubrique ouverte.</summary>
    private void RebuildActions()
    {
        Actions.Clear();
        switch (section?.Value)
        {
            case "annees":
                Action("Nouvelle année", MaterialIconKind.CalendarPlus, () => NewYearAsync(Shell), primary: true);
                Action("Configurer les mois et le retard", MaterialIconKind.CalendarEdit, EditYearScheduleAsync, row: true);
                Action("Définir comme courante", MaterialIconKind.CalendarCheck, SetCurrentYearAsync, row: true);
                Action("Clôturer l'année", MaterialIconKind.CalendarLock, CloseYearAsync, row: true);
                break;
            case "sections":
                Action("Nouvelle section", MaterialIconKind.ViewGridPlus, NewSectionAsync, primary: true);
                Action("Modifier", MaterialIconKind.Pencil, EditSelectedAsync, open: true);
                Action("Supprimer", MaterialIconKind.Delete, DeleteSectionAsync, row: true);
                break;
            case "promotions":
                Action("Nouvelle promotion", MaterialIconKind.FolderPlus, () => NewPromotionAsync(Shell), primary: true);
                Action("Modifier", MaterialIconKind.Pencil, EditSelectedAsync, open: true);
                Action("Supprimer", MaterialIconKind.Delete, DeletePromotionAsync, row: true);
                break;
            case "frais":
                Action("Nouvelle catégorie", MaterialIconKind.TagPlusOutline, () => CategoryFormAsync(Shell, null), primary: true);
                Action("Modifier", MaterialIconKind.Pencil, EditSelectedAsync, open: true);
                Action("Supprimer", MaterialIconKind.Delete, DeleteCategoryAsync, row: true);
                break;
            case "licence":
                return; // Les actions de licence restent dans sa propre fiche.
            case "etablissement":
                Action("Changer le logo", MaterialIconKind.Image, ChangeLogoAsync);
                break;
            default:
                Action("Rétablir les réglages", MaterialIconKind.Restore, () => { ResetPrefs.Execute(null); return Task.CompletedTask; });
                return;
        }
        ActionRefresh();
    }

    public override string Title => "Configuration";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.CogOutline;

    public IReadOnlyList<BackstageItemVm> SchoolSections { get; }
    public IReadOnlyList<BackstageItemVm> PosteSections { get; }
    public bool HasSchoolSections => SchoolSections.Count > 0;

    /// <summary>Toutes les rubriques, pour la liste déroulante du téléphone.</summary>
    public IReadOnlyList<BackstageItemVm> AllSections => [.. SchoolSections, .. PosteSections];

    /// <summary>Deux listes à gauche : choisir dans l'une efface la sélection de l'autre.</summary>
    public BackstageItemVm? SchoolSection { get => SchoolSections.Contains(section!) ? section : null; set { if (value is not null) Section = value; } }
    public BackstageItemVm? PosteSection { get => PosteSections.Contains(section!) ? section : null; set { if (value is not null) Section = value; } }

    public BackstageItemVm? Section
    {
        get => section;
        set
        {
            if (!Set(ref section, value)) return;
            foreach (var name in new[] { nameof(IsSchool), nameof(IsYears), nameof(IsSections), nameof(IsPromotions), nameof(IsFees), nameof(IsAppearance), nameof(IsText),
                         nameof(IsWindows), nameof(IsAnimations), nameof(IsSounds), nameof(IsSecurity), nameof(IsAbout), nameof(IsLicense), nameof(IsPoste), nameof(ShowResetLink), nameof(SchoolSection), nameof(PosteSection), nameof(SectionTitle), nameof(SectionHint) })
                Raise(name);
            if (value?.Value == "securite" && !Preferences.MobileLayout) ActivateSecurity();
            if (value?.Value == "licence") _ = License.RefreshAsync();
            RebuildActions();
            if (value is not null) _ = Shell.Run(RefreshAsync);
        }
    }

    public override object? SelectedRow => section?.Value switch
    {
        "annees" => SelectedYear,
        "sections" => SelectedSection,
        "promotions" => SelectedPromotion,
        "frais" => SelectedCategory,
        _ => null,
    };

    protected override bool HasRows => section?.Value is "annees" or "sections" or "promotions" or "frais";

    public bool IsSchool => section?.Value == "etablissement";
    public bool IsLicense => section?.Value == "licence";
    public bool IsYears => section?.Value == "annees";
    public bool IsSections => section?.Value == "sections";
    public bool IsPromotions => section?.Value == "promotions";
    public bool IsFees => section?.Value == "frais";
    public bool IsAppearance => section?.Value == "apparence";
    public bool IsText => section?.Value == "texte";
    public bool IsWindows => section?.Value == "fenetres";
    public bool IsAnimations => section?.Value == "animations";
    public bool IsSounds => section?.Value == "sons";
    public bool IsSecurity => section?.Value == "securite";
    public bool IsAbout => section?.Value == "apropos";
    public bool IsPoste => PosteSections.Contains(section!);
    /// <summary>Lien « Rétablir » en bas de page sur le téléphone ; sur l'ordinateur, le bouton de l'en-tête suffit.</summary>
    public bool ShowResetLink => IsPoste && Platform.IsMobile;
    public string SectionTitle => section?.Label ?? string.Empty;

    /// <summary>Phrase d'explication sous le titre de chaque rubrique.</summary>
    public string SectionHint => section?.Value switch
    {
        "etablissement" => "Ce qui figure sur les reçus, les listes et les cartes d'accès.",
        "licence" => "Échéance, jours restants et statut du serveur SCOLIA. Aucun dossier scolaire n'est envoyé à JP-Services.",
        "annees" => "Les années scolaires, leurs mois de paiement et l'année courante.",
        "sections" => "Les filières de l'établissement, qui regroupent les promotions.",
        "promotions" => "Les classes, leur code de 4 caractères, leur mensualité et leurs frais.",
        "frais" => "Les frais autres que la mensualité (inscription, examen, tenue…) et leur montant. Seules les catégories créées ici existent.",
        "apparence" => Platform.IsMobile ? "Clair, sombre ou selon l'appareil, et la couleur des boutons et des sélections." : "Clair, sombre ou selon Windows, et la couleur des boutons et des sélections.",
        "texte" => "La police et la taille du texte de toute l'interface.",
        "fenetres" => "La fenêtre au lancement et ce qui entoure les pages.",
        "animations" => "Fondus, transitions entre pages et écran d'ouverture.",
        "sons" => "Un son discret à chaque action, et les bannières des autres postes.",
        "securite" => "Mot de passe, code d'accès et biométrie du compte.",
        "apropos" => "Informations du logiciel, éditeur, assistance et recherche des mises à jour officielles.",
        _ => string.Empty,
    };

    public FormViewModel? SchoolForm { get => schoolForm; private set => Set(ref schoolForm, value); }
    public ObservableCollection<YearRowVm> Years { get; } = [];
    public AsyncCommand EditSelectedCalendar => editSelectedCalendar ??= new AsyncCommand(() => Shell.Run(EditYearScheduleAsync));
    private AsyncCommand? editSelectedCalendar;
    public ObservableCollection<SectionRowVm> SectionRows { get; } = [];
    public bool HasSectionRows => SectionRows.Count > 0;
    public bool HasNoSections => SectionRows.Count == 0;
    public ObservableCollection<PromotionRowVm> Promotions { get; } = [];
    public bool HasPromotionRows => Promotions.Count > 0;
    public bool HasNoPromotions => Promotions.Count == 0;
    public YearRowVm? SelectedYear
    {
        get => selectedYear;
        set { if (Set(ref selectedYear, value)) { Raise(nameof(HasSelectedYear)); Raise(nameof(SelectedYearTitle)); } }
    }
    public bool HasSelectedYear => selectedYear is not null;
    public string SelectedYearTitle => selectedYear is null ? string.Empty
        : $"Année {selectedYear.Label}{(selectedYear.IsCurrent ? " · courante" : string.Empty)}";
    public SectionRowVm? SelectedSection { get => selectedSection; set => Set(ref selectedSection, value); }
    public PromotionRowVm? SelectedPromotion { get => selectedPromotion; set => Set(ref selectedPromotion, value); }
    public ObservableCollection<FeeCategoryRowVm> Categories { get; } = [];
    public FeeCategoryRowVm? SelectedCategory { get => selectedCategory; set => Set(ref selectedCategory, value); }

    public override async Task LoadAsync()
    {
        if (IsPoste)
        {
            subtitle = "Réglages de ce poste";
            Raise(nameof(Subtitle));
            RaisePrefs();
            return;
        }
        switch (section?.Value)
        {
            case "annees":
                await LoadYearsAsync();
                break;
            case "sections":
                await LoadSectionsAsync();
                break;
            case "promotions":
                await LoadPromotionsAsync();
                break;
            case "frais":
                await LoadCategoriesAsync();
                break;
            default:
                await LoadSchoolAsync();
                break;
        }
    }

    private async Task LoadSchoolAsync()
    {
        var s = await Shell.Api.GetAsync<SchoolDto>("api/etablissement");
        var form = new FormViewModel("Établissement", string.Empty, async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Put, "api/etablissement", new
            {
                name = f.Text("nom"),
                shortName = f.Text("sigle"),
                motto = f.OptionalText("slogan"),
                address = f.OptionalText("adresse"),
                city = f.OptionalText("ville"),
                country = f.OptionalText("pays"),
                phone = f.OptionalText("telephone"),
                email = f.OptionalText("email"),
                website = f.OptionalText("site"),
                currencyCode = f.Text("devise"),
                currencySymbol = f.Text("symbole"),
                wholeAmountsOnly = f.Flag("entiers"),
                receiptFooter = f.OptionalText("pied"),
                phoneCountryCode = f.Text("indicatif"),
                cardMinistry = f.OptionalText("ministere"),
                cardDirection = f.OptionalText("direction"),
                cardDivision = f.OptionalText("division"),
                cardZone = f.OptionalText("zone"),
                cardInstitute = f.OptionalText("institut"),
                cardAddress = f.OptionalText("adresse-institut"),
            });
            Shell.Notify("Établissement enregistré. Les reçus suivants utilisent ces informations.");
            return true;
        })
        { SubmitText = "Enregistrer", ShowCancel = false, Embedded = true };
        form.AddHeader("Identité", "Nom et sigle figurent en tête des reçus, des listes et des cartes d'accès.");
        form.Add("nom", "Nom de l'établissement", value: s.Name, required: true);
        form.Add("sigle", "Sigle", value: s.ShortName, required: true);
        form.Add("slogan", "Devise ou slogan", value: s.Motto);
        form.AddHeader("Coordonnées");
        form.Add("adresse", "Adresse", value: s.Address);
        form.Add("ville", "Ville", value: s.City);
        form.Add("pays", "Pays", value: s.Country);
        form.Add("telephone", "Téléphone", FieldKind.Phone, value: s.Phone, hint: "10 chiffres, ex. 0812345678");
        form.Add("email", "E-mail", value: s.Email);
        form.Add("site", "Site web", value: s.Website);
        form.AddHeader("Monnaie et reçus");
        form.Add("devise", "Monnaie (code ISO)", value: s.CurrencyCode, required: true, hint: "CDF");
        form.Add("symbole", "Symbole affiché", value: s.CurrencySymbol, required: true, hint: "FC");
        form.Add("indicatif", "Indicatif téléphonique", value: s.PhoneCountryCode, required: true, hint: "243 pour la RDC, utilisé par les relances WhatsApp");
        form.AddFlag("entiers", "Montants en unités entières", s.WholeAmountsOnly);
        form.Add("pied", "Mention en bas des reçus", FieldKind.Multiline, s.ReceiptFooter);
        form.AddHeader("Cartes d'accès", "En-tête officiel imprimé sur les cartes des apprenants.");
        form.Add("ministere", "Ministère", value: s.CardMinistry);
        form.Add("direction", "Direction", value: s.CardDirection);
        form.Add("division", "Division provinciale", value: s.CardDivision);
        form.Add("zone", "Zone de santé", value: s.CardZone);
        form.Add("institut", "Institut", value: s.CardInstitute);
        form.Add("adresse-institut", "Adresse de l'institut", value: s.CardAddress);
        SchoolForm = form;
        subtitle = $"{s.Name} · {s.CurrencyCode}";
        Raise(nameof(Subtitle));
    }

    private async Task LoadYearsAsync()
    {
        var years = await Shell.Api.GetAsync<List<AcademicYearDto>>("api/etablissement/annees");
        var keep = SelectedYear?.Id;
        Years.Clear();
        foreach (var y in years)
        {
            var status = Labels.Status(y.Status);
            var periods = y.Periods.OrderBy(p => p.Order).ToArray();
            var days = periods.Select(p => p.DueDate.AddDays(-1).Day).Distinct().ToArray();
            var deadline = periods.Length == 0 ? "À configurer"
                : days.Length == 1
                    ? $"Limite : le {days[0]} du mois suivant · retard dès le lendemain"
                    : "Limites variables : échéances déjà comptabilisées conservées";
            Years.Add(new YearRowVm(y.Id, y.Label, $"{Format.Date(y.StartDate)} → {Format.Date(y.EndDate)}", status, y.IsCurrent,
                periods.Length == 0 ? "Aucun mois" : $"{periods[0].Name} {periods[0].CalendarYear} → {periods[^1].Name} {periods[^1].CalendarYear} ({periods.Length} mois)",
                ThemeBrush.Get(y.Status == "Closed" ? "ToneNeutral" : "ToneGreen"), deadline, y));
        }
        // L'année courante est choisie d'office : le bouton « Configurer » agit tout de suite sur elle.
        SelectedYear = Years.FirstOrDefault(y => y.Id == keep) ?? Years.FirstOrDefault(y => y.IsCurrent) ?? Years.FirstOrDefault();
        subtitle = Format.Count(years.Count, "année scolaire", "années scolaires");
        Raise(nameof(Subtitle));
    }

    private async Task LoadSectionsAsync()
    {
        var sections = await Shell.Api.GetAsync<List<SectionDto>>("api/etablissement/sections");
        var previouslySelectedId = SelectedSection?.Id;
        SectionRows.Clear();
        foreach (var s in sections)
            SectionRows.Add(new SectionRowVm(s.Id, s.Code, s.Name, s.IsActive ? "Oui" : "Non", s,
                Format.Count(s.PromotionCount, "promotion", "promotions"), Format.Count(s.StudentCount, "apprenant", "apprenants")));
        SelectedSection = SectionRows.FirstOrDefault(s => s.Id == previouslySelectedId);
        Raise(nameof(HasSectionRows));
        Raise(nameof(HasNoSections));
        subtitle = Format.Count(sections.Count, "section", "sections");
        Raise(nameof(Subtitle));
    }

    private async Task LoadPromotionsAsync()
    {
        var promotions = await Shell.Api.GetAsync<List<PromotionDto>>("api/etablissement/promotions");
        var previouslySelectedId = SelectedPromotion?.Id;
        Promotions.Clear();
        foreach (var p in promotions)
            Promotions.Add(new PromotionRowVm(p.Id, p.Code, p.Name, p.SectionName ?? "—", $"{p.Level}", $"{p.StudentCount}",
                Money(p.MonthlyFee), Money(p.AnnualTotal), p.IsActive ? "Oui" : "Non", p));
        SelectedPromotion = Promotions.FirstOrDefault(p => p.Id == previouslySelectedId);
        Raise(nameof(HasPromotionRows));
        Raise(nameof(HasNoPromotions));
        subtitle = Format.Count(promotions.Count, "promotion", "promotions");
        Raise(nameof(Subtitle));
    }

    private async Task LoadCategoriesAsync()
    {
        var categories = await Shell.Api.GetAsync<List<FeeCategoryDto>>("api/etablissement/categories-frais");
        Categories.Clear();
        foreach (var c in categories)
            Categories.Add(new FeeCategoryRowVm(c.Id, c.Name, Money(c.Amount), c.DueDate is { } d ? Format.Date(d) : "À la rentrée",
                Format.Count(c.PromotionCount, "promotion", "promotions"), c.IsActive ? "Oui" : "Non", c));
        subtitle = categories.Count == 0
            ? "Aucune catégorie : seules les mensualités sont facturées"
            : Format.Count(categories.Count, "catégorie de frais", "catégories de frais");
        Raise(nameof(Subtitle));
    }

    /// <summary>Création ou modification d'une catégorie de frais, appliquée si besoin à toutes les promotions de l'année.</summary>
    public static async Task CategoryFormAsync(ShellViewModel shell, FeeCategoryDto? existing)
    {
        var form = new FormViewModel(existing is null ? "Nouvelle catégorie de frais" : "Modifier la catégorie",
            "Par exemple : frais d'inscription, frais d'examen, tenue, carte d'élève. La mensualité se règle dans chaque promotion.", async f =>
        {
            var body = new
            {
                name = f.Text("nom"),
                amount = f.Amount("montant"),
                dueDate = f.Day("echeance"),
                isActive = f.Flag("actif"),
                applyToPromotions = f.Flag("appliquer"),
            };
            if (existing is null) await shell.Api.SendAsync(HttpMethod.Post, "api/etablissement/categories-frais", body);
            else await shell.Api.SendAsync(HttpMethod.Put, $"api/etablissement/categories-frais/{existing.Id}", body);
            shell.Notify(f.Flag("appliquer") ? "Catégorie enregistrée et appliquée aux promotions de l'année." : "Catégorie enregistrée.");
            return true;
        });
        form.Add("nom", "Nom de la catégorie", value: existing?.Name, required: true);
        form.Add("montant", "Montant", FieldKind.Amount, existing?.Amount.ToString("0", CultureInfo.InvariantCulture), required: true);
        form.AddDay("echeance", "Échéance (facultative)", existing?.DueDate);
        form.AddFlag("actif", "Catégorie active", existing?.IsActive ?? true);
        form.AddFlag("appliquer", existing is null ? "Appliquer à toutes les promotions de l'année courante" : "Mettre à jour le montant dans toutes les promotions de l'année courante",
            existing is null);
        if (await shell.Ask(form) && shell.Current is SettingsPage page) await page.RefreshAsync();
    }

    private async Task DeleteCategoryAsync()
    {
        if (section?.Value != "frais" || SelectedCategory is null)
        {
            Shell.Notify("Sélectionnez une catégorie de frais.");
            return;
        }
        var c = SelectedCategory;
        if (!await Shell.Confirm("Supprimer la catégorie",
                $"« {c.Name} » sera retirée de toutes les promotions. Une catégorie déjà encaissée ne peut pas être supprimée : désactivez-la.",
                "Supprimer", danger: true))
            return;
        await Shell.Api.SendAsync(HttpMethod.Delete, $"api/etablissement/categories-frais/{c.Id}");
        Shell.Notify("Catégorie supprimée.");
        await RefreshAsync();
    }

    private async Task EditSelectedAsync()
    {
        switch (section?.Value)
        {
            case "promotions" when SelectedPromotion is not null:
                await PromotionFormAsync(Shell, SelectedPromotion.Source);
                break;
            case "frais" when SelectedCategory is not null:
                await CategoryFormAsync(Shell, SelectedCategory.Source);
                break;
            case "sections" when SelectedSection is not null:
                await EditSectionAsync(SelectedSection.Source);
                break;
            default:
                Shell.Notify("Sélectionnez une ligne à modifier.");
                break;
        }
    }

    private async Task EditSectionAsync(SectionDto s)
    {
        var form = new FormViewModel("Modifier la section",
            "Les promotions existantes restent liées à cette section, même si elle est désactivée.", async f =>
        {
            if (!ValidateSectionForm(f)) return false;
            await Shell.Api.SendAsync(HttpMethod.Put, $"api/etablissement/sections/{s.Id}",
                new { code = f.Text("code"), name = f.Text("nom"), isActive = f.Flag("actif") });
            Shell.Notify("Section enregistrée.");
            return true;
        });
        form.Add("code", "Code de la section", value: s.Code, required: true, hint: "12 caractères maximum, par exemple INF ou ACC.");
        form.Add("nom", "Désignation", value: s.Name, required: true, hint: "Nom complet de la filière.");
        form.AddFlag("actif", "Section active", s.IsActive);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task SetCurrentYearAsync()
    {
        if (SelectedYear is null)
        {
            Shell.Notify("Sélectionnez une année scolaire.");
            return;
        }
        var y = SelectedYear;
        var confirmed = await Shell.Confirm("Définir l'année courante",
            $"L'année {y.Label} devient l'année de travail : encaissements, notes et présences s'y rattachent.", "Définir");
        if (!confirmed) return;
        await Shell.Api.SendAsync(HttpMethod.Post, $"api/etablissement/annees/{y.Id}/courante");
        Shell.Notify($"Année {y.Label} définie comme courante.");
        await RefreshAsync();
    }

    private async Task CloseYearAsync()
    {
        if (SelectedYear is null)
        {
            Shell.Notify("Sélectionnez une année scolaire à clôturer.");
            return;
        }
        var y = SelectedYear;
        var confirmed = await Shell.Confirm("Clôturer l'année",
            $"L'année {y.Label} sera verrouillée. Les écritures ne pourront plus y être modifiées.", "Clôturer", danger: true);
        if (!confirmed) return;
        await Shell.Api.SendAsync(HttpMethod.Post, $"api/etablissement/annees/{y.Id}/cloture");
        Shell.Notify($"Année {y.Label} clôturée.");
        await RefreshAsync();
    }

    private async Task DeletePromotionAsync()
    {
        if (section?.Value != "promotions" || SelectedPromotion is null)
        {
            Shell.Notify("Sélectionnez une promotion à supprimer.");
            return;
        }
        var p = SelectedPromotion;
        var confirmed = await Shell.Confirm("Supprimer la promotion",
            $"La promotion {p.Code} sera retirée. Elle n'est possible que si elle ne compte aucun apprenant ni aucun reçu.", "Supprimer", danger: true);
        if (!confirmed) return;
        await Shell.Api.SendAsync(HttpMethod.Delete, $"api/etablissement/promotions/{p.Id}");
        Shell.Notify($"Promotion {p.Code} supprimée.");
        await RefreshAsync();
    }

    private async Task ChangeLogoAsync()
    {
        if (Shell.PickFileHandler is null) return;
        var picked = await Shell.PickFileHandler(LogoImage.Extensions);
        if (picked is null) return;
        // Toute image courante est acceptée : SCOLIA la convertit en PNG net, redressé, de 512 points au plus.
        byte[] png;
        try { png = await Task.Run(() => LogoImage.ToPng(picked.Data, picked.Name)); }
        catch (LogoImageException ex)
        {
            Shell.Notify(ex.Message);
            return;
        }
        await Shell.Api.UploadAsync<object>(HttpMethod.Put, "api/etablissement/logo",
            [new UploadedFile("fichier", Path.ChangeExtension(picked.Name, ".png"), png)]);
        await Shell.RefreshLogoAsync();
        Shell.Notify("Logo mis à jour. Il figure désormais sur l'écran de connexion, les reçus, les listes et les cartes.");
    }

    private async Task DeleteSectionAsync()
    {
        if (section?.Value != "sections" || SelectedSection is null)
        {
            Shell.Notify("Sélectionnez une section à supprimer.");
            return;
        }
        var s = SelectedSection;
        if (!await Shell.Confirm("Supprimer la section",
                $"La section {s.Name} sera supprimée. Une section qui regroupe des promotions, même d'années passées, ne peut pas l'être : désactivez-la.",
                "Supprimer", danger: true))
            return;
        await Shell.Api.SendAsync(HttpMethod.Delete, $"api/etablissement/sections/{s.Id}");
        Shell.Notify($"Section {s.Name} supprimée.");
        await RefreshAsync();
    }

    /// <summary>Contrôles lisibles sur mobile avant l'appel à l'API (qui vérifie aussi unicité et droits).</summary>
    public static bool ValidateSectionForm(FormViewModel form)
    {
        if (form.Text("code").Length is < 1 or > 12)
        {
            form.Find("code").Error = "Le code doit contenir entre 1 et 12 caractères.";
            form.Fail("Vérifiez le code de la section.");
            return false;
        }
        if (form.Text("nom").Length is < 1 or > 120)
        {
            form.Find("nom").Error = "La désignation doit contenir entre 1 et 120 caractères.";
            form.Fail("Vérifiez la désignation de la section.");
            return false;
        }
        return true;
    }

    private async Task NewSectionAsync()
    {
        var form = new FormViewModel("Nouvelle section",
            "Créez une filière, puis associez-y les promotions dans « Promotions et frais ».", async f =>
        {
            if (!ValidateSectionForm(f)) return false;
            await Shell.Api.SendAsync(HttpMethod.Post, "api/etablissement/sections",
                new { code = f.Text("code"), name = f.Text("nom"), isActive = f.Flag("actif") });
            Shell.Notify("Section créée. Elle est disponible pour les promotions.");
            return true;
        });
        form.Add("code", "Code de la section", required: true, hint: "12 caractères maximum, par exemple INF ou ACC.");
        form.Add("nom", "Désignation", required: true, hint: "Exemple : Sciences infirmières.");
        form.AddFlag("actif", "Section active", true);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    /// <summary>Mois scolaires affichés par leur nom, au lieu d'exiger des numéros 1-12.</summary>
    public static IReadOnlyList<ChoiceItem> CalendarMonths { get; } =
        Enumerable.Range(1, 12).Select(i => new ChoiceItem(i.ToString(CultureInfo.InvariantCulture),
            CultureInfo.GetCultureInfo("fr-FR").DateTimeFormat.GetMonthName(i) is { } month
                ? char.ToUpperInvariant(month[0]) + month[1..] : i.ToString(CultureInfo.InvariantCulture))).ToArray();

    /// <summary>
    /// Mois prédits : le dernier mois suit le premier (dix mois d'école) tant qu'il n'a pas été choisi à la main,
    /// et le résumé se recalcule à chaque choix : nombre de mois, période, exemple de retard.
    /// </summary>
    public static void PredictMonths(FormViewModel form, FormField summary, bool endFollows = true)
    {
        var start = form.Find("debut");
        var end = form.Find("fin");
        var limit = form.Find("limite");
        var endChosen = !endFollows;
        var following = false;
        void Describe()
        {
            if (start.Choice is null || end.Choice is null || limit.Choice is null) return;
            var first = int.Parse(start.Choice.Value, CultureInfo.InvariantCulture);
            var last = int.Parse(end.Choice.Value, CultureInfo.InvariantCulture);
            var day = int.Parse(limit.Choice.Value, CultureInfo.InvariantCulture);
            var count = (last - first + 12) % 12 + 1;
            var following1 = CalendarMonths[SetupViewModel.MonthsAhead(first, 1) - 1].Label.ToLowerInvariant();
            summary.Hint = $"{Format.Count(count, "mois", "mois")} : de {start.Choice.Label.ToLowerInvariant()} à {end.Choice.Label.ToLowerInvariant()}. " +
                $"Pour {start.Choice.Label.ToLowerInvariant()}, limite le {day} {following1} : retard dès le {day + 1} {following1}.";
        }
        var previous = form.Changed;
        form.Changed = field =>
        {
            previous?.Invoke(field);
            if (field == end && !following) endChosen = true;
            if (field == start && !endChosen && start.Choice is { } chosen)
            {
                var target = SetupViewModel.MonthsAhead(int.Parse(chosen.Value, CultureInfo.InvariantCulture), 9).ToString(CultureInfo.InvariantCulture);
                following = true;
                try { end.Choice = end.Options.First(o => o.Value == target); }
                finally { following = false; }
            }
            if (field == start || field == end || field == limit) Describe();
        };
        Describe();
    }

    /// <summary>Libellés d'année proposés : « 2026 », « 26 » ou « 2026- » donnent « 2026-2027 ».</summary>
    public static IReadOnlyList<string> YearLabels(string typed)
    {
        var digits = new string(typed.TakeWhile(c => char.IsDigit(c)).ToArray());
        var rest = typed[digits.Length..].Trim();
        if (rest.Length > 1 || (rest.Length == 1 && rest != "-") || digits.Length is not (2 or 4)) return [];
        var year = digits.Length == 2 ? 2000 + int.Parse(digits, CultureInfo.InvariantCulture) : int.Parse(digits, CultureInfo.InvariantCulture);
        return year is < 2000 or > 2099 ? [] : [$"{year}-{year + 1}"];
    }

    public static IReadOnlyList<ChoiceItem> PaymentDeadlineDays { get; } =
        Enumerable.Range(1, 28).Select(i => new ChoiceItem(i.ToString(CultureInfo.InvariantCulture),
            $"{i} du mois suivant")).ToArray();

    /// <summary>Calendrier complet à la création : début, fin et dernier jour sans retard.</summary>
    public static async Task NewYearAsync(ShellViewModel shell)
    {
        var proposal = await shell.Api.GetAsync<Dictionary<string, string>>("api/installation/annee-proposee");
        var form = new FormViewModel("Nouvelle année scolaire",
            "Définissez les mois à encaisser et la limite de paiement. Le retard commencera le jour suivant.", async f =>
        {
            var start = int.Parse(f.ChoiceValue("debut")!, CultureInfo.InvariantCulture);
            var end = int.Parse(f.ChoiceValue("fin")!, CultureInfo.InvariantCulture);
            var cutoff = int.Parse(f.ChoiceValue("limite")!, CultureInfo.InvariantCulture);
            await shell.Api.SendAsync(HttpMethod.Post, "api/etablissement/annees", new
            {
                label = f.Text("libelle"),
                startMonth = start,
                endMonth = end,
                monthCount = (end - start + 12) % 12 + 1,
                lastOnTimeDay = cutoff,
                dueDay = cutoff + 1,
                copyPromotionsFromCurrent = f.Flag("copier"),
                makeCurrent = f.Flag("courante"),
            });
            shell.Notify("Année créée : les mois à encaisser et les dates de retard sont configurés.");
            return true;
        });
        form.Add("libelle", "Année scolaire", value: proposal.GetValueOrDefault("label"), required: true,
            hint: "Exemple : 2026-2027");
        form.AddHeader("Mois de facturation", "Les deux mois sont inclus, même si la période traverse décembre.");
        form.AddChoice("debut", "Premier mois à encaisser", CalendarMonths, "9", required: true);
        form.AddChoice("fin", "Dernier mois à encaisser", CalendarMonths, "6", required: true);
        form.AddChoice("limite", "Dernier jour pour payer sans retard", PaymentDeadlineDays, "5", required: true);
        var example = form.AddHeader("Exemple", "Pour octobre, limite le 5 novembre : déclaration de retard dès le 6 novembre.");
        PredictMonths(form, example);
        // « 2026 » propose « 2026-2027 », comme une saisie semi-automatique.
        form.Find("libelle").Suggest = f => YearLabels(f.Text);
        form.RefreshSuggestions();
        form.AddFlag("copier", "Copier les promotions et leurs frais de l'année courante", true);
        form.AddFlag("courante", "Utiliser cette nouvelle année pour les inscriptions et paiements", true);
        if (await shell.Ask(form) && shell.Current is SettingsPage page) await page.RefreshAsync();
    }

    /// <summary>Modifier les périodes d'une année existante, en conservant les identifiants des mensualités.</summary>
    private async Task EditYearScheduleAsync()
    {
        if (SelectedYear is not { } chosen)
        {
            Shell.Notify("Sélectionnez d'abord une année scolaire dans la liste.");
            return;
        }
        if (chosen.Source.Status == "Closed")
        {
            Shell.Notify("Cette année est clôturée : son calendrier ne peut plus être modifié.");
            return;
        }
        var periods = chosen.Source.Periods.OrderBy(p => p.Order).ToArray();
        var first = periods.FirstOrDefault()?.Month ?? 9;
        var last = periods.LastOrDefault()?.Month ?? 6;
        // Le calendrier des mois à venir prime sur les échéances historiques déjà payées.
        var cutoff = periods.Length == 0 ? 5 : periods[^1].DueDate.AddDays(-1).Day;
        var form = new FormViewModel($"Mois et échéance · {chosen.Label}",
            "Modifiez le début, la fin et le dernier jour sans retard. Les mois de paiement seront mis à jour dans toutes les promotions de cette année.", async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Put, $"api/etablissement/annees/{chosen.Id}/calendrier", new
            {
                startMonth = int.Parse(f.ChoiceValue("debut")!, CultureInfo.InvariantCulture),
                endMonth = int.Parse(f.ChoiceValue("fin")!, CultureInfo.InvariantCulture),
                lastOnTimeDay = int.Parse(f.ChoiceValue("limite")!, CultureInfo.InvariantCulture),
            });
            Shell.Notify("Calendrier mis à jour : les mois apparaîtront dans Encaisser, pour les promotions avec une mensualité.");
            return true;
        });
        form.AddHeader("Période de l'année", "Choisissez les mois par leur nom. La période peut traverser décembre.");
        form.AddChoice("debut", "Premier mois de paiement", CalendarMonths, first.ToString(CultureInfo.InvariantCulture), required: true);
        form.AddChoice("fin", "Dernier mois de paiement", CalendarMonths, last.ToString(CultureInfo.InvariantCulture), required: true);
        form.AddChoice("limite", "Dernier jour sans retard (mois suivant)", PaymentDeadlineDays, cutoff.ToString(CultureInfo.InvariantCulture), required: true);
        PredictMonths(form, form.AddHeader("Résumé"), endFollows: false);
        form.AddHeader("Protection des reçus", "Les mois déjà encaissés ou pris en charge ne peuvent pas être renommés ni supprimés ; leur échéance historique reste inchangée. Les mois non réglés et les nouveaux mois peuvent être configurés. Synchronisez les appareils avant de modifier.");
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    /// <summary>Crée une promotion avec ses frais mensuels et ses frais ponctuels.</summary>
    public static async Task NewPromotionAsync(ShellViewModel shell) => await PromotionFormAsync(shell, null);

    /// <summary>Boîte de création ou de modification d'une promotion.</summary>
    public static async Task PromotionFormAsync(ShellViewModel shell, PromotionDto? existing)
    {
        var sections = await shell.Api.GetAsync<List<SectionDto>>("api/etablissement/sections");
        // Une section désactivée n'est plus proposée, sauf si la promotion y est déjà rattachée.
        var sectionChoices = sections.Where(s => s.IsActive || s.Id == existing?.SectionId)
            .Select(s => new ChoiceItem(s.Id.ToString(), s.IsActive ? $"{s.Code} · {s.Name}" : $"{s.Code} · {s.Name} (désactivée)")).ToList();
        // Les autres frais viennent du catalogue : une ligne par catégorie créée, avec son montant pour cette promotion.
        List<FeeCategoryDto> categories;
        try { categories = await shell.Api.GetAsync<List<FeeCategoryDto>>("api/etablissement/categories-frais"); }
        catch (ApiException) { categories = []; }
        var applied = existing?.Fees.Where(f => f.CategoryId is not null).ToDictionary(f => f.CategoryId!.Value) ?? [];
        var shown = categories.Where(c => c.IsActive || applied.ContainsKey(c.Id)).ToList();
        // Un complexe réunit plusieurs niveaux : il faut sélectionner le cycle de
        // chaque classe, sans le déduire du code ou de la section.
        var plan = await shell.Api.GetAsync<SchoolCycleConfigurationDto>("api/etablissement/cycles");
        var schoolUsesCycles = InstitutionProfiles.UsesSchoolCycles(plan.InstitutionType);
        var schoolIsPublic = plan.Governance == "Public";
        var allowsZeroMonthly = schoolUsesCycles || schoolIsPublic
            || plan.InstitutionType == InstitutionProfiles.SuperieurLmd;
        if (schoolIsPublic) shown.Clear();
        var activeCycles = plan.Cycles.Where(c => c.IsActive).ToList();
        if (schoolUsesCycles && activeCycles.Count == 0)
        {
            shell.Notify("Configurez d'abord les cycles dans Paramètres > Cycles scolaires avant de créer une classe.");
            return;
        }
        var cycleChoices = activeCycles.Select(c =>
            new ChoiceItem(c.Id.ToString(), c.Name)).ToList();

        var form = new FormViewModel(existing is null ? "Nouvelle promotion" : "Modifier la promotion",
            schoolIsPublic
                ? "Public : gestion de la classe sans frais exigibles. Une décision réglementaire sera nécessaire pour activer la perception."
                : allowsZeroMonthly
                    ? "Associez la classe au bon cycle ; vous pouvez laisser la mensualité à zéro si elle ne s'applique pas."
                    : "La mensualité est obligatoire. Pour chaque catégorie, laissez le montant vide si elle ne concerne pas cette classe.", async f =>
        {
            var monthly = f.Amount("mensualite");
            if (schoolIsPublic ? monthly != 0 : allowsZeroMonthly ? monthly < 0 : monthly <= 0)
            {
                f.Find("mensualite").Error = schoolIsPublic
                    ? "Aucune mensualité scolaire publique sans autorisation : indiquez zéro."
                    : allowsZeroMonthly ? "La mensualité ne peut pas être négative." : "La mensualité est obligatoire.";
                return false;
            }
            var fees = new List<object>();
            foreach (var category in shown)
            {
                var amount = f.Amount($"categorie-{category.Id:N}");
                if (amount > 0) fees.Add(new { categoryId = category.Id, amount });
            }
            int? capacity = Format.TryAmount(f.Text("capacite"), out var cap) ? (int?)(int)cap : null;
            var body = new
            {
                code = f.Text("code"),
                name = f.Text("nom"),
                sectionId = f.ChoiceValue("section") is { } s ? Guid.Parse(s) : (Guid?)null,
                level = (int)f.Amount("niveau"),
                capacity,
                isActive = f.Flag("actif"),
                nextPromotionCode = f.OptionalText("suivante"),
                schoolCycleId = schoolUsesCycles && f.ChoiceValue("cycle") is { } selected
                    ? Guid.Parse(selected) : (Guid?)null,
                monthlyFee = monthly,
                oneTimeFees = fees,
                managesCategories = true,
            };
            if (existing is null) await shell.Api.SendAsync(HttpMethod.Post, "api/etablissement/promotions", body);
            else await shell.Api.SendAsync(HttpMethod.Put, $"api/etablissement/promotions/{existing.Id}", body);
            shell.Notify(existing is null ? "Promotion créée." : "Promotion enregistrée.");
            return true;
        });
        form.Add("code", "Code (4 caractères)", value: existing?.Code, required: true);
        form.Add("nom", "Désignation", value: existing?.Name, required: true);
        if (schoolUsesCycles)
            form.AddChoice("cycle", "Cycle scolaire", cycleChoices,
                existing?.SchoolCycleId?.ToString() ?? (cycleChoices.Count == 1 ? cycleChoices[0].Value : null),
                required: true);
        form.AddChoice("section", "Section", sectionChoices, existing?.SectionId?.ToString());
        form.Add("niveau", "Niveau", FieldKind.Amount, existing?.Level.ToString() ?? "1", required: true);
        form.Add("capacite", "Capacité", FieldKind.Amount, existing?.Capacity?.ToString());
        form.Add("suivante", "Promotion suivante", value: existing?.NextPromotionCode, hint: "Code de la promotion de l'année suivante");
        form.Add("mensualite", schoolIsPublic ? "Mensualité (public : zéro)"
            : allowsZeroMonthly ? "Mensualité (0 si non appliquée)" : "Mensualité (obligatoire)",
            FieldKind.Amount,
            existing is { MonthlyFee: > 0 } ? existing.MonthlyFee.ToString("0")
                : allowsZeroMonthly ? "0" : null, required: true);
        form.AddFlag("actif", "Promotion active", existing?.IsActive ?? true);
        form.AddHeader("Autres frais", shown.Count == 0
            ? "Aucune catégorie de frais n'est créée. Créez les frais d'inscription ou d'examen dans Configuration, Catégories de frais si nécessaires."
            : "Montant de chaque catégorie pour cette promotion. Vide : la catégorie ne s'applique pas.");
        foreach (var category in shown)
        {
            var value = applied.TryGetValue(category.Id, out var item) ? item.Amount
                : existing is null && category.IsActive ? category.Amount : (decimal?)null;
            form.Add($"categorie-{category.Id:N}", category.Name + (category.IsActive ? string.Empty : " (désactivée)"), FieldKind.Amount,
                value?.ToString("0", CultureInfo.InvariantCulture), hint: $"Montant du catalogue : {Format.Money(category.Amount, shell.Session?.CurrencySymbol ?? string.Empty)}");
        }
        await PredictPromotionAsync(shell, form, sections, existing);

        if (await shell.Ask(form) && shell.Current is SettingsPage page) await page.RefreshAsync();
    }

    /// <summary>
    /// Prédiction dans la fiche promotion : « INF1 » propose « Première Infirmière » puis les autres niveaux ;
    /// une désignation choisie remplit le code, le niveau, la promotion suivante et la section.
    /// </summary>
    private static async Task PredictPromotionAsync(ShellViewModel shell, FormViewModel form, IReadOnlyList<SectionDto> sections, PromotionDto? existing)
    {
        List<PromotionDto> others;
        try { others = (await shell.Api.GetAsync<List<PromotionDto>>("api/etablissement/promotions")).Where(p => p.Id != existing?.Id).ToList(); }
        catch (Exception ex) when (ex is HttpRequestException or ApiException) { others = []; }
        var context = new PromotionNames.Context(
            sections.Select(s => (s.Code, s.Name)).ToList(),
            others.Select(p => (p.Code, p.Name)).ToList());
        var usedCodes = others.Select(p => p.Code.ToUpperInvariant()).ToHashSet();
        var usedNames = others.Select(p => PromotionNames.Normalize(p.Name)).ToHashSet();
        var code = form.Find("code");
        var name = form.Find("nom");
        var level = form.Find("niveau");
        var next = form.Find("suivante");
        var section = form.Find("section");
        var levelTouched = existing is not null;
        var auto = false;

        string Prefix() => PromotionNames.ParseCode(code.Text).Prefix is { Length: >= 2 } p ? p
            : PromotionNames.TradeOf(name.Text) is { } trade && PromotionNames.PrefixFor(trade, context) is { } q ? q : string.Empty;

        name.Suggest = f => PromotionNames.Suggest(code.Text, f.Text, context)
            .Where(n => !usedNames.Contains(PromotionNames.Normalize(n))).Take(6).ToList();
        code.Suggest = f =>
        {
            var (prefix, typedLevel) = PromotionNames.ParseCode(f.Text);
            if (typedLevel is not null && f.Text.Trim().Length > 0) return [];
            var target = PromotionNames.LevelOf(name.Text);
            if (prefix.Length < 2) prefix = Prefix();
            if (prefix.Length < 2) return [];
            var levels = target is { } t ? [t] : Enumerable.Range(1, PromotionNames.DefaultLevels);
            return levels.Select(l => PromotionNames.Code(prefix, l)).Where(c => !usedCodes.Contains(c) && c != f.Text.Trim().ToUpperInvariant()).ToList();
        };
        name.Picked = (_, chosen) =>
        {
            if (PromotionNames.LevelOf(chosen) is not { } l) return;
            var prefix = Prefix();
            auto = true;
            try { if (prefix.Length >= 2 && PromotionNames.ParseCode(code.Text).Level != l) code.Text = PromotionNames.Code(prefix, l); }
            finally { auto = false; }
            Fill(l);
        };
        code.Picked = (_, chosen) => { if (PromotionNames.ParseCode(chosen).Level is { } l) Fill(l); };
        form.Changed = changed =>
        {
            if (changed == level && !auto) levelTouched = true;
            // Code complet tapé à la main : le niveau suit, tant que la personne ne l'a pas changé elle-même.
            if (changed == code && PromotionNames.ParseCode(code.Text).Level is { } l && !levelTouched)
            {
                auto = true;
                try { level.Text = l.ToString(CultureInfo.InvariantCulture); }
                finally { auto = false; }
            }
        };

        void Fill(int l)
        {
            auto = true;
            try { FillLinked(l); }
            finally { auto = false; }
        }

        void FillLinked(int l)
        {
            if (!levelTouched) level.Text = l.ToString(CultureInfo.InvariantCulture);
            var prefix = Prefix();
            if (string.IsNullOrWhiteSpace(next.Text) && prefix.Length >= 2 && l < PromotionNames.DefaultLevels)
                next.Text = PromotionNames.Code(prefix, l + 1);
            if (section.Choice is null && PromotionNames.TradeOf(name.Text) is { } trade)
            {
                var match = sections.FirstOrDefault(s => s.Code.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                    ?? sections.FirstOrDefault(s => PromotionNames.Normalize(s.Name) == PromotionNames.Normalize(trade));
                if (match is not null) section.Choice = section.Options.FirstOrDefault(o => o.Value == match.Id.ToString());
            }
        }
        form.RefreshSuggestions();
    }
}

/// <summary>Compte utilisateur dans la liste des utilisateurs.</summary>
public sealed record UserRowVm(Guid Id, string UserName, string DisplayName, string Role, string Active, IBrush ActiveBrush, string LastLogin, string Locked, UserDto Source);

/// <summary>Utilisateurs : comptes nominatifs, rôles, activation et mots de passe provisoires.</summary>
public sealed class UsersPage : PageViewModel
{
    private UserRowVm? selected;
    private string subtitle = string.Empty;

    public UsersPage(ShellViewModel shell) : base(shell)
    {
        Action("Nouvel utilisateur", MaterialIconKind.AccountPlus, () => NewUserAsync(Shell), primary: true);
        Action("Modifier", MaterialIconKind.AccountEdit, EditAsync, open: true);
        Action("Réinitialiser le mot de passe", MaterialIconKind.KeyChange, ResetPasswordAsync, row: true);
        if (Shell.IsServerComputer && Shell.Can(Perm.UsersManage))
            Action("Retirer l'accès au serveur", MaterialIconKind.AccountCancel, RevokeServerAccountAsync, row: true);
        ActionRefresh();
    }

    public override string Title => "Utilisateurs";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.AccountCogOutline;

    public ObservableCollection<UserRowVm> Items { get; } = [];
    public UserRowVm? Selected { get => selected; set => Set(ref selected, value); }

    public override async Task LoadAsync()
    {
        var users = await Shell.Api.GetAsync<List<UserDto>>("api/utilisateurs");
        Items.Clear();
        foreach (var u in users)
            Items.Add(new UserRowVm(u.Id, u.UserName, u.DisplayName, u.RoleLabel, u.IsActive ? "Actif" : "Désactivé",
                ThemeBrush.Get(u.IsActive ? "ToneGreen" : "ToneNeutral"),
                u.LastLoginUtc is { } l ? Format.DateTime(l) : "jamais",
                u.IsLocked ? "Verrouillé" : "—", u));
        subtitle = $"{Format.Count(users.Count(u => u.IsActive), "compte actif", "comptes actifs")} sur {users.Count}";
        Raise(nameof(Subtitle));
    }

    /// <summary>
    /// Retrait du droit de connexion côté serveur : désactivation réversible et auditée,
    /// sans supprimer l'utilisateur, ses cotes, ses opérations ni les autres comptes.
    /// </summary>
    private async Task RevokeServerAccountAsync()
    {
        if (!Shell.IsServerComputer || !Shell.Can(Perm.UsersManage))
        {
            Shell.Notify("Cette opération est réservée à l'administrateur sur la machine principale.");
            return;
        }
        if (Selected?.Source is not { } user) { Shell.Notify("Choisissez un compte utilisateur."); return; }
        if (!user.IsActive) { Shell.Notify("Ce compte n'a déjà plus accès au serveur."); return; }
        if (string.Equals(Shell.Session?.UserName, user.UserName, StringComparison.OrdinalIgnoreCase))
        {
            Shell.Notify("Vous ne pouvez pas retirer votre propre accès au serveur.");
            return;
        }
        if (!await Shell.Confirm("Retirer l'accès au serveur ?",
            $"Le compte « {user.UserName} » sera désactivé sur le serveur principal. "
            + "Sa prochaine authentification sera refusée et ses sessions existantes seront invalidées. "
            + "Les données scolaires, notes, paiements et traces d'audit resteront conservés. "
            + "Un administrateur pourra réactiver le compte depuis « Modifier ».",
            "Désactiver l'accès", danger: true)) return;
        await Shell.Api.SendAsync(HttpMethod.Put, $"api/utilisateurs/{user.Id}", new
        {
            displayName = user.DisplayName,
            role = user.Role,
            isActive = false,
            staffMemberId = user.StaffMemberId,
            juryAccess = false,
        });
        Shell.Notify($"Accès au serveur retiré pour « {user.UserName} ».");
        await RefreshAsync();
    }

    /// <summary>Fiches des enseignants en activité, pour relier un compte enseignant à ses cours attribués.</summary>
    private static async Task<IReadOnlyList<ChoiceItem>> TeacherChoicesAsync(ShellViewModel shell) =>
        (await shell.Api.GetAsync<List<TeacherDto>>("api/enseignants")).Select(t => new ChoiceItem(t.Id.ToString(), $"{t.Nom} · {t.Function}")).ToList();

    private static Guid? TeacherLink(FormViewModel f) =>
        f.ChoiceValue("role") == "Enseignant" && f.ChoiceValue("enseignant") is { Length: > 0 } id ? Guid.Parse(id) : null;

    /// <summary>Formulaire de création d'un compte nominatif.</summary>
    public static async Task NewUserAsync(ShellViewModel shell)
    {
        var teachers = await TeacherChoicesAsync(shell);
        var form = new FormViewModel("Nouvel utilisateur", "Secrétariat : administration. Comptabilité : encaissements et caisse. Promoteur : consultation d'indicateurs agrégés, sans modification. Enseignant : encodage et remise des cotes de ses seuls cours, par code anonyme. Le mot de passe provisoire devra être changé à la première connexion.", async f =>
        {
            await shell.Api.SendAsync(HttpMethod.Post, "api/utilisateurs", new
            {
                userName = f.Text("identifiant"),
                displayName = f.Text("nom"),
                role = f.ChoiceValue("role"),
                password = f.Text("motDePasse"),
                staffMemberId = TeacherLink(f),
                juryAccess = f.ChoiceValue("role") == "Enseignant" && f.Flag("jury"),
            });
            shell.Notify("Compte créé.");
            return true;
        });
        form.Add("identifiant", "Identifiant", required: true);
        form.Add("nom", "Nom complet", required: true);
        form.AddChoice("role", "Rôle", Labels.Roles, "Secretariat", required: true);
        form.AddChoice("enseignant", "Fiche enseignant (rôle Enseignant seulement)", teachers);
        form.AddFlag("jury", "Membre du jury : encode aussi les épreuves du jury (rôle Enseignant)", false);
        form.Add("motDePasse", "Code provisoire", FieldKind.Secret, required: true,
            hint: AccessCode.Hint + " La personne choisira le sien à sa première connexion.");
        if (await shell.Ask(form) && shell.Current is UsersPage page) await page.RefreshAsync();
    }

    private async Task EditAsync()
    {
        if (Selected is null)
        {
            Shell.Notify("Sélectionnez un compte.");
            return;
        }
        var u = Selected.Source;
        var form = new FormViewModel("Modifier le compte", $"Identifiant {u.UserName}", async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Put, $"api/utilisateurs/{u.Id}", new
            {
                displayName = f.Text("nom"),
                role = f.ChoiceValue("role"),
                isActive = f.Flag("actif"),
                staffMemberId = TeacherLink(f),
                juryAccess = f.ChoiceValue("role") == "Enseignant" && f.Flag("jury"),
            });
            Shell.Notify("Compte enregistré.");
            return true;
        });
        var teachers = await TeacherChoicesAsync(Shell);
        form.Add("nom", "Nom complet", value: u.DisplayName, required: true);
        form.AddChoice("role", "Rôle", Labels.Roles, u.Role, required: true);
        form.AddChoice("enseignant", "Fiche enseignant (rôle Enseignant seulement)", teachers, u.StaffMemberId?.ToString());
        // Privilège, pas un compte : le retirer laisse en place les cotes du jury déjà encodées.
        form.AddFlag("jury", "Membre du jury : encode aussi les épreuves du jury (rôle Enseignant)", u.JuryAccess);
        form.AddFlag("actif", "Compte actif", u.IsActive);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task ResetPasswordAsync()
    {
        if (Selected is null)
        {
            Shell.Notify("Sélectionnez un compte.");
            return;
        }
        var u = Selected.Source;
        var form = new FormViewModel("Réinitialiser le mot de passe",
            $"Compte {u.UserName} · L'accès actuel sera remplacé. Les sessions existantes seront invalidées.", async f =>
        {
            var temporary = f.Text("motDePasse");
            if (AccessCode.Problem(temporary) is { } weakness)
            {
                f.Find("motDePasse").Error = weakness;
                f.Fail("Choisissez un code conforme à la politique de sécurité.");
                return false;
            }
            if (temporary != f.Text("confirmation"))
            {
                f.Find("confirmation").Error = "Les deux codes ne correspondent pas.";
                f.Fail("Confirmez le code provisoire avant de continuer.");
                return false;
            }
            await Shell.Api.SendAsync(HttpMethod.Post, $"api/utilisateurs/{u.Id}/mot-de-passe", new { newPassword = temporary });
            // Le serveur invalide le SecurityStamp et impose le changement au prochain accès.
            f.Find("motDePasse").Text = string.Empty;
            f.Find("confirmation").Text = string.Empty;
            Shell.Notify("Nouveau code provisoire enregistré. Le compte devra le remplacer à sa prochaine connexion.");
            return true;
        });
        form.Add("motDePasse", "Nouveau code provisoire", FieldKind.Secret, required: true,
            hint: AccessCode.Hint + " À remettre uniquement au titulaire du compte.");
        form.Add("confirmation", "Confirmer le nouveau code", FieldKind.Secret, required: true,
            hint: "Ressaisissez exactement le même code pour éviter les erreurs.");
        if (await Shell.Ask(form)) await RefreshAsync();
    }
}
