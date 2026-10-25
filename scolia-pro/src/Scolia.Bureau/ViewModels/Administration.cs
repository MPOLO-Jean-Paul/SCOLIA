using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

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
        Action("Nouveau membre", MaterialIconKind.AccountPlusOutline, () => NewMemberAsync(Shell), primary: true);
        Action("Modifier le membre", MaterialIconKind.AccountEditOutline, EditMemberAsync, open: true);
        Action("Préparer la paie", MaterialIconKind.CalendarPlusOutline, PreparePayrollAsync);
        Action("Pointage et bilan", MaterialIconKind.ClockCheckOutline, () => Shell.Open(() => new StaffAttendancePage(Shell)));
        Action("Modifier le bulletin", MaterialIconKind.PencilOutline, EditPayslipAsync, row: true);
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
            subtitle = $"Paie d'{MonthLabel(key).ToLowerInvariant()} · {Format.Count(rows.Count, "bulletin", "bulletins")} · net {Money(rows.Sum(r => r.Net))}";
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
public sealed record YearRowVm(Guid Id, string Label, string Period, string Status, bool IsCurrent, string Months, IBrush StatusBrush);

/// <summary>Section d'enseignement dans la liste des paramètres.</summary>
public sealed record SectionRowVm(Guid Id, string Code, string Name, string Active, SectionDto Source);

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
    private string subtitle = string.Empty;
    private FormViewModel? schoolForm;

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
            ]
            : [];
        PosteSections =
        [
            new BackstageItemVm("apparence", "Thème et couleurs", MaterialIconKind.Palette, "TonePurple"),
            new BackstageItemVm("texte", "Texte et polices", MaterialIconKind.FormatFont, "ToneBlue"),
            new BackstageItemVm("fenetres", "Fenêtres et affichage", MaterialIconKind.WindowMaximize, "ToneTeal"),
            new BackstageItemVm("animations", "Animations et fluidité", MaterialIconKind.AnimationPlay, "ToneOrange"),
            new BackstageItemVm("sons", "Sons", MaterialIconKind.VolumeHigh, "TonePink"),
        ];
        // Sur le mobile, l'application occupe tout l'écran : la taille de fenêtre, le volet et le ruban n'existent pas.
        if (Preferences.MobileLayout) PosteSections = [.. PosteSections.Where(s => s.Value != "fenetres")];
        var all = SchoolSections.Concat(PosteSections).ToList();
        section = all.FirstOrDefault(s => s.Value == open) ?? all[0];
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
    public RelayCommand<string> TestSound { get; }
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
        : "Inter, Carlito, Liberation et DejaVu sont fournies avec SCOLIA. Les autres viennent de Windows quand elles y sont installées.";
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
                Action("Nouvelle année", MaterialIconKind.CalendarPlusOutline, () => NewYearAsync(Shell), primary: true);
                Action("Définir comme courante", MaterialIconKind.CalendarCheckOutline, SetCurrentYearAsync, row: true);
                Action("Clôturer l'année", MaterialIconKind.CalendarLockOutline, CloseYearAsync, row: true);
                break;
            case "sections":
                Action("Nouvelle section", MaterialIconKind.ViewGridPlusOutline, NewSectionAsync, primary: true);
                Action("Modifier", MaterialIconKind.PencilOutline, EditSelectedAsync, open: true);
                break;
            case "promotions":
                Action("Nouvelle promotion", MaterialIconKind.FolderPlusOutline, () => NewPromotionAsync(Shell), primary: true);
                Action("Modifier", MaterialIconKind.PencilOutline, EditSelectedAsync, open: true);
                Action("Supprimer", MaterialIconKind.DeleteOutline, DeletePromotionAsync, row: true);
                break;
            case "etablissement":
                Action("Changer le logo", MaterialIconKind.ImageOutline, ChangeLogoAsync);
                break;
            default:
                Action("Rétablir les réglages", MaterialIconKind.Restore, () => { ResetPrefs.Execute(null); return Task.CompletedTask; });
                return;
        }
        ActionRefresh();
    }

    public override string Title => "Paramètres";
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
            foreach (var name in new[] { nameof(IsSchool), nameof(IsYears), nameof(IsSections), nameof(IsPromotions), nameof(IsAppearance), nameof(IsText),
                         nameof(IsWindows), nameof(IsAnimations), nameof(IsSounds), nameof(IsPoste), nameof(SchoolSection), nameof(PosteSection), nameof(SectionTitle), nameof(SectionHint) })
                Raise(name);
            RebuildActions();
            if (value is not null) _ = Shell.Run(RefreshAsync);
        }
    }

    public override object? SelectedRow => section?.Value switch
    {
        "annees" => SelectedYear,
        "sections" => SelectedSection,
        "promotions" => SelectedPromotion,
        _ => null,
    };

    protected override bool HasRows => section?.Value is "annees" or "sections" or "promotions";

    public bool IsSchool => section?.Value == "etablissement";
    public bool IsYears => section?.Value == "annees";
    public bool IsSections => section?.Value == "sections";
    public bool IsPromotions => section?.Value == "promotions";
    public bool IsAppearance => section?.Value == "apparence";
    public bool IsText => section?.Value == "texte";
    public bool IsWindows => section?.Value == "fenetres";
    public bool IsAnimations => section?.Value == "animations";
    public bool IsSounds => section?.Value == "sons";
    public bool IsPoste => PosteSections.Contains(section!);
    public string SectionTitle => section?.Label ?? string.Empty;

    /// <summary>Phrase d'explication sous le titre de chaque rubrique.</summary>
    public string SectionHint => section?.Value switch
    {
        "etablissement" => "Identité, coordonnées, monnaie et en-tête des cartes d'accès. Ces informations figurent sur les reçus et les documents.",
        "annees" => "Les années scolaires, leurs mois de paiement et l'année courante.",
        "sections" => "Les filières de l'établissement, qui regroupent les promotions.",
        "promotions" => "Les classes, leur code de 4 caractères, leur mensualité et leurs frais.",
        "apparence" => Platform.IsMobile ? "Clair, sombre ou selon l'appareil, et la couleur des boutons et des sélections." : "Clair, sombre ou selon Windows, et la couleur des boutons et des sélections.",
        "texte" => "La police et la taille du texte de toute l'interface.",
        "fenetres" => "La taille de la fenêtre au lancement et les éléments affichés autour des pages.",
        "animations" => "Les mouvements de l'interface : fondus, transitions entre pages et écran d'ouverture.",
        "sons" => "Un son discret quand une action réussit, échoue ou quand un message s'affiche.",
        _ => string.Empty,
    };

    public FormViewModel? SchoolForm { get => schoolForm; private set => Set(ref schoolForm, value); }
    public ObservableCollection<YearRowVm> Years { get; } = [];
    public ObservableCollection<SectionRowVm> SectionRows { get; } = [];
    public ObservableCollection<PromotionRowVm> Promotions { get; } = [];
    public YearRowVm? SelectedYear { get => selectedYear; set => Set(ref selectedYear, value); }
    public SectionRowVm? SelectedSection { get => selectedSection; set => Set(ref selectedSection, value); }
    public PromotionRowVm? SelectedPromotion { get => selectedPromotion; set => Set(ref selectedPromotion, value); }

    public override async Task LoadAsync()
    {
        if (IsPoste)
        {
            subtitle = "Réglages de ce poste, enregistrés automatiquement";
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
        Years.Clear();
        foreach (var y in years)
        {
            var status = Labels.Status(y.Status);
            Years.Add(new YearRowVm(y.Id, y.Label, $"{Format.Date(y.StartDate)} → {Format.Date(y.EndDate)}", status, y.IsCurrent,
                string.Join(" · ", y.Periods.Select(p => p.Name[..Math.Min(3, p.Name.Length)])), ThemeBrush.Get(y.Status == "Closed" ? "ToneNeutral" : "ToneGreen")));
        }
        subtitle = $"{Format.Count(years.Count, "année scolaire", "années scolaires")}";
        Raise(nameof(Subtitle));
    }

    private async Task LoadSectionsAsync()
    {
        var sections = await Shell.Api.GetAsync<List<SectionDto>>("api/etablissement/sections");
        SectionRows.Clear();
        foreach (var s in sections)
            SectionRows.Add(new SectionRowVm(s.Id, s.Code, s.Name, s.IsActive ? "Oui" : "Non", s));
        subtitle = Format.Count(sections.Count, "section", "sections");
        Raise(nameof(Subtitle));
    }

    private async Task LoadPromotionsAsync()
    {
        var promotions = await Shell.Api.GetAsync<List<PromotionDto>>("api/etablissement/promotions");
        Promotions.Clear();
        foreach (var p in promotions)
            Promotions.Add(new PromotionRowVm(p.Id, p.Code, p.Name, p.SectionName ?? "—", $"{p.Level}", $"{p.StudentCount}",
                Money(p.MonthlyFee), Money(p.AnnualTotal), p.IsActive ? "Oui" : "Non", p));
        subtitle = Format.Count(promotions.Count, "promotion", "promotions");
        Raise(nameof(Subtitle));
    }

    private async Task EditSelectedAsync()
    {
        switch (section?.Value)
        {
            case "promotions" when SelectedPromotion is not null:
                await PromotionFormAsync(Shell, SelectedPromotion.Source);
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
        var form = new FormViewModel("Modifier la section", $"Code {s.Code}", async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Put, $"api/etablissement/sections/{s.Id}", new { code = f.Text("code"), name = f.Text("nom"), isActive = f.Flag("actif") });
            Shell.Notify("Section enregistrée.");
            return true;
        });
        form.Add("code", "Code", value: s.Code, required: true);
        form.Add("nom", "Nom", value: s.Name, required: true);
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
        var picked = await Shell.PickFileHandler([".png", ".jpg", ".jpeg"]);
        if (picked is null) return;
        await Shell.Api.UploadAsync<object>(HttpMethod.Put, "api/etablissement/logo", [new UploadedFile("fichier", picked.Name, picked.Data)]);
        await Shell.RefreshLogoAsync();
        Shell.Notify("Logo mis à jour. Il figure désormais sur l'écran de connexion, les reçus, les listes et les cartes.");
    }

    private async Task NewSectionAsync()
    {
        var form = new FormViewModel("Nouvelle section", "Une section regroupe des promotions.", async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Post, "api/etablissement/sections", new { code = f.Text("code"), name = f.Text("nom"), isActive = f.Flag("actif") });
            Shell.Notify("Section créée.");
            return true;
        });
        form.Add("code", "Code", required: true);
        form.Add("nom", "Nom", required: true);
        form.AddFlag("actif", "Section active", true);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    /// <summary>Crée l'année suivante, avec proposition de libellé et copie des promotions.</summary>
    public static async Task NewYearAsync(ShellViewModel shell)
    {
        var proposal = await shell.Api.GetAsync<Dictionary<string, string>>("api/installation/annee-proposee");
        var form = new FormViewModel("Nouvelle année scolaire", "L'année peut être rendue courante dès sa création.", async f =>
        {
            await shell.Api.SendAsync(HttpMethod.Post, "api/etablissement/annees", new
            {
                label = f.Text("libelle"),
                startMonth = (int)f.Amount("rentree"),
                monthCount = (int)f.Amount("mois"),
                dueDay = (int)f.Amount("echeance"),
                copyPromotionsFromCurrent = f.Flag("copier"),
                makeCurrent = f.Flag("courante"),
            });
            shell.Notify("Année scolaire créée.");
            return true;
        });
        form.Add("libelle", "Libellé", value: proposal.GetValueOrDefault("label"), required: true);
        form.Add("rentree", "Mois de rentrée (1-12)", FieldKind.Amount, "9", required: true);
        form.Add("mois", "Mois facturés", FieldKind.Amount, "10", required: true);
        form.Add("echeance", "Jour d'échéance (mois suivant)", FieldKind.Amount, "5", required: true);
        form.AddFlag("copier", "Copier les promotions de l'année courante", true);
        form.AddFlag("courante", "Définir comme année courante", true);
        if (await shell.Ask(form) && shell.Current is SettingsPage page) await page.RefreshAsync();
    }

    /// <summary>Crée une promotion avec ses frais mensuels et ses frais ponctuels.</summary>
    public static async Task NewPromotionAsync(ShellViewModel shell) => await PromotionFormAsync(shell, null);

    /// <summary>Boîte de création ou de modification d'une promotion.</summary>
    public static async Task PromotionFormAsync(ShellViewModel shell, PromotionDto? existing)
    {
        var sections = await shell.Api.GetAsync<List<SectionDto>>("api/etablissement/sections");
        var sectionChoices = sections.Select(s => new ChoiceItem(s.Id.ToString(), $"{s.Code} · {s.Name}")).ToList();
        var inscription = existing?.Fees.FirstOrDefault(f => f.Kind == "Inscription");
        var examen = existing?.Fees.FirstOrDefault(f => f.Kind == "Examen");

        var form = new FormViewModel(existing is null ? "Nouvelle promotion" : "Modifier la promotion",
            "Les frais ponctuels sont facultatifs : indiquez un montant pour les créer.", async f =>
        {
            var fees = new List<object>();
            foreach (var (key, kind, label, dayKey) in new[] { ("inscription", "Inscription", "Frais d'inscription", "inscriptionEcheance"), ("examen", "Examen", "Frais d'examen", "examenEcheance") })
            {
                var amount = f.Amount(key);
                if (amount <= 0) continue;
                var due = f.Day(dayKey);
                if (due is null)
                {
                    f.Find(dayKey).Error = "Indiquez l'échéance de ces frais.";
                    return false;
                }
                fees.Add(new { kind, label, amount, dueDate = due });
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
                monthlyFee = f.Amount("mensualite"),
                oneTimeFees = fees,
            };
            if (existing is null) await shell.Api.SendAsync(HttpMethod.Post, "api/etablissement/promotions", body);
            else await shell.Api.SendAsync(HttpMethod.Put, $"api/etablissement/promotions/{existing.Id}", body);
            shell.Notify(existing is null ? "Promotion créée." : "Promotion enregistrée.");
            return true;
        });
        form.Add("code", "Code (4 caractères)", value: existing?.Code, required: true);
        form.Add("nom", "Désignation", value: existing?.Name, required: true);
        form.AddChoice("section", "Section", sectionChoices, existing?.SectionId?.ToString());
        form.Add("niveau", "Niveau", FieldKind.Amount, existing?.Level.ToString() ?? "1", required: true);
        form.Add("capacite", "Capacité", FieldKind.Amount, existing?.Capacity?.ToString());
        form.Add("suivante", "Promotion suivante", value: existing?.NextPromotionCode, hint: "Code de la promotion de l'année suivante");
        form.Add("mensualite", "Mensualité", FieldKind.Amount, existing?.MonthlyFee.ToString("0"), required: true);
        form.AddFlag("actif", "Promotion active", existing?.IsActive ?? true);
        form.Add("inscription", "Frais d'inscription (montant)", FieldKind.Amount, inscription?.Amount.ToString("0"));
        form.AddDay("inscriptionEcheance", "Échéance des frais d'inscription", inscription?.DueDate);
        form.Add("examen", "Frais d'examen (montant)", FieldKind.Amount, examen?.Amount.ToString("0"));
        form.AddDay("examenEcheance", "Échéance des frais d'examen", examen?.DueDate);

        if (await shell.Ask(form) && shell.Current is SettingsPage page) await page.RefreshAsync();
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
        Action("Nouvel utilisateur", MaterialIconKind.AccountPlusOutline, () => NewUserAsync(Shell), primary: true);
        Action("Modifier", MaterialIconKind.AccountEditOutline, EditAsync, open: true);
        Action("Réinitialiser le mot de passe", MaterialIconKind.KeyChange, ResetPasswordAsync, row: true);
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

    /// <summary>Formulaire de création d'un compte nominatif.</summary>
    public static async Task NewUserAsync(ShellViewModel shell)
    {
        var form = new FormViewModel("Nouvel utilisateur", "Secrétariat : administre SCOLIA, paramétrage compris. Comptabilité : encaissements, dépenses et caisse. Le mot de passe provisoire devra être changé à la première connexion.", async f =>
        {
            await shell.Api.SendAsync(HttpMethod.Post, "api/utilisateurs", new
            {
                userName = f.Text("identifiant"),
                displayName = f.Text("nom"),
                role = f.ChoiceValue("role"),
                password = f.Text("motDePasse"),
            });
            shell.Notify("Compte créé.");
            return true;
        });
        form.Add("identifiant", "Identifiant", required: true);
        form.Add("nom", "Nom complet", required: true);
        form.AddChoice("role", "Rôle", Labels.Roles, "Secretariat", required: true);
        form.Add("motDePasse", "Mot de passe provisoire", FieldKind.Secret, required: true);
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
            });
            Shell.Notify("Compte enregistré.");
            return true;
        });
        form.Add("nom", "Nom complet", value: u.DisplayName, required: true);
        form.AddChoice("role", "Rôle", Labels.Roles, u.Role, required: true);
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
        var form = new FormViewModel("Réinitialiser le mot de passe", $"Compte {u.UserName}", async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Post, $"api/utilisateurs/{u.Id}/mot-de-passe", new { newPassword = f.Text("motDePasse") });
            Shell.Notify("Mot de passe provisoire défini. Il sera à changer à la prochaine connexion.");
            return true;
        });
        form.Add("motDePasse", "Nouveau mot de passe provisoire", FieldKind.Secret, required: true);
        if (await Shell.Ask(form)) await RefreshAsync();
    }
}
