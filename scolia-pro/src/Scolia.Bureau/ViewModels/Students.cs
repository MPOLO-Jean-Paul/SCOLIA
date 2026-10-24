using System.Collections.ObjectModel;
using System.Net.Http;
using Avalonia.Media;
using Avalonia.Threading;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>Ligne de la liste des apprenants, déjà mise en forme.</summary>
public sealed record StudentRow(Guid Id, string Matricule, string Name, string Gender, string Promotion, string Status, IBrush StatusBrush, string Phone, string Balance, string Overdue, IBrush OverdueBrush);

/// <summary>Liste des apprenants : recherche, filtres par promotion et statut, pagination.</summary>
public sealed class StudentsPage : PageViewModel
{
    private const int PageSize = 50;
    private string search = string.Empty;
    private ChoiceItem? promotion;
    private ChoiceItem? status;
    private StudentRow? selected;
    private int page = 1;
    private int pageCount = 1;
    private int total;
    private bool ready;
    private string subtitle = string.Empty;
    private DispatcherTimer? debounce;
    private IReadOnlyList<ChoiceItem> promotions = [];

    public StudentsPage(ShellViewModel shell) : base(shell)
    {
        if (Shell.Can(Perm.StudentsWrite)) Action("Nouvelle inscription", MaterialIconKind.AccountPlusOutline, () => StudentForms.CreateAsync(Shell), primary: true);
        Action("Ouvrir la fiche", MaterialIconKind.AccountOutline, OpenSelectedAsync);
        ActionRefresh();
        PreviousPage = new AsyncCommand(async () => { page--; await RefreshAsync(); });
        NextPage = new AsyncCommand(async () => { page++; await RefreshAsync(); });
    }

    public override string Title => "Apprenants";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.AccountGroupOutline;

    public ObservableCollection<StudentRow> Items { get; } = [];
    public IReadOnlyList<ChoiceItem> Promotions { get => promotions; private set => Set(ref promotions, value); }
    public IReadOnlyList<ChoiceItem> StatusChoices { get; } = Labels.Statuses.Prepend(new ChoiceItem(string.Empty, "Tous les statuts")).ToList();

    public string Search
    {
        get => search;
        set { if (Set(ref search, value)) ScheduleSearch(); }
    }

    public ChoiceItem? Promotion
    {
        get => promotion;
        set { if (Set(ref promotion, value) && ready) ApplyFilter(); }
    }

    public ChoiceItem? Status
    {
        get => status;
        set { if (Set(ref status, value) && ready) ApplyFilter(); }
    }

    public StudentRow? Selected { get => selected; set => Set(ref selected, value); }
    public string PageText => $"Page {page} sur {pageCount}";
    public bool CanPrevious => page > 1;
    public bool CanNext => page < pageCount;

    public AsyncCommand PreviousPage { get; }
    public AsyncCommand NextPage { get; }

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            var choices = await PromotionChoicesAsync(withAll: true);
            Promotions = choices;
            promotion = choices[0];
            status = StatusChoices[0];
            Raise(nameof(Promotion));
            Raise(nameof(Status));
            ready = true;
        }

        var query = ScoliaClient.Query(
            ("q", Search.Trim()), ("promotionId", Promotion?.Value), ("statut", Status?.Value), ("page", page), ("taille", PageSize));
        var result = await Shell.Api.GetAsync<PagedResult<StudentListItem>>("api/apprenants" + query);

        total = result.Total;
        pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        if (page > pageCount) page = pageCount;

        Items.Clear();
        foreach (var s in result.Items)
        {
            var late = s.Overdue > 0;
            Items.Add(new StudentRow(
                s.Id, s.Matricule, Format.FullName(s.LastName, s.MiddleName, s.FirstName), Labels.Gender(s.Gender), s.PromotionCode ?? "—",
                Labels.Status(s.Status ?? string.Empty), ThemeBrush.Get(Labels.Tone(s.Status ?? string.Empty) switch { "green" => "ToneGreen", "red" => "ToneRed", "orange" => "ToneOrange", _ => "ToneNeutral" }),
                s.GuardianPhone ?? "—", Money(s.Balance), late ? Money(s.Overdue) : "À jour", ThemeBrush.Get(late ? "ToneRed" : "ToneGreen")));
        }

        subtitle = Format.Count(total, "apprenant", "apprenants") + (Promotion is { Value.Length: > 0 } ? $" · {Promotion.Label}" : string.Empty);
        Raise(nameof(Subtitle));
        Raise(nameof(PageText));
        Raise(nameof(CanPrevious));
        Raise(nameof(CanNext));
    }

    /// <summary>Attend une pause de frappe avant de chercher, pour ne pas envoyer une requête par lettre.</summary>
    private void ScheduleSearch()
    {
        debounce?.Stop();
        debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        debounce.Tick += async (_, _) =>
        {
            debounce?.Stop();
            page = 1;
            await RefreshAsync();
        };
        debounce.Start();
    }

    private void ApplyFilter()
    {
        page = 1;
        _ = RefreshAsync();
    }

    /// <summary>Ouvre la fiche de l'apprenant sélectionné : double-clic ou bouton.</summary>
    public async Task OpenSelectedAsync()
    {
        if (Selected is null)
        {
            Shell.Notify("Sélectionnez un apprenant dans la liste.");
            return;
        }
        var id = Selected.Id;
        await Shell.Open(() => new StudentDetailPage(Shell, id));
    }
}

/// <summary>Ligne d'inscription de la fiche.</summary>
public sealed record EnrollmentRow(string Year, string Promotion, string EnrolledOn, string Status, IBrush StatusBrush, string Exit);

/// <summary>Ligne du compte : une échéance, son montant et ce qui reste à payer.</summary>
public sealed record AccountRow(string Label, string DueDate, string Amount, string Covered, string Paid, string Remaining, string Status, IBrush StatusBrush);

/// <summary>Prise en charge affichée sur la fiche.</summary>
public sealed record SponsorshipRow(Guid Id, string Sponsor, string Reason, string Since, string Covered, string Status, bool Active);

/// <summary>Fiche d'un apprenant : identité, inscriptions, compte, reçus et prises en charge.</summary>
public sealed class StudentDetailPage : PageViewModel
{
    private readonly Guid studentId;
    private string title = "Fiche apprenant";
    private string subtitle = string.Empty;
    private StudentDto? student;
    private EnrollmentDto? enrollment;
    private SponsorshipRow? selectedSponsorship;

    public StudentDetailPage(ShellViewModel shell, Guid studentId) : base(shell)
    {
        this.studentId = studentId;
        EndSponsorship = new AsyncCommand(() => Shell.Run(EndSelectedSponsorshipAsync));
        if (Shell.Can(Perm.PaymentsCreate)) Action("Encaisser", MaterialIconKind.HandCoin, () => Shell.Open(() => new CashierPage(Shell, studentId)), primary: true);
        if (Shell.Can(Perm.StudentsWrite)) Action("Modifier", MaterialIconKind.PencilOutline, EditAsync);
        if (Shell.Can(Perm.PaymentsRead)) Action("Relevé de compte", MaterialIconKind.FileDocumentOutline, StatementAsync);
        if (Shell.Can(Perm.StudentsWrite)) Action("Changer le statut", MaterialIconKind.AccountSwitchOutline, ChangeStatusAsync);
        if (Shell.Can(Perm.StudentsWrite)) Action("Changer de promotion", MaterialIconKind.SwapHorizontal, ChangePromotionAsync);
        if (Shell.Can(Perm.PaymentsCreate)) Action("Prise en charge", MaterialIconKind.ShieldAccountOutline, NewSponsorshipAsync);
        ActionRefresh();
    }

    public Guid StudentId => studentId;
    public override string Title => title;
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.AccountOutline;

    public ObservableCollection<InfoLine> Details { get; } = [];
    public ObservableCollection<KpiVm> Totals { get; } = [];
    public ObservableCollection<EnrollmentRow> Enrollments { get; } = [];
    public ObservableCollection<AccountRow> Lines { get; } = [];
    public ObservableCollection<ReceiptLineVm> Receipts { get; } = [];
    public ObservableCollection<SponsorshipRow> Sponsorships { get; } = [];

    public SponsorshipRow? SelectedSponsorship { get => selectedSponsorship; set => Set(ref selectedSponsorship, value); }

    /// <summary>Ferme une prise en charge en gardant son historique.</summary>
    public AsyncCommand EndSponsorship { get; private set; } = null!;

    public override async Task LoadAsync()
    {
        student = await Shell.Api.GetAsync<StudentDto>($"api/apprenants/{studentId}");
        enrollment = student.Enrollments.FirstOrDefault(e => e.Status == "Actif") ?? (student.Enrollments.Count > 0 ? student.Enrollments[0] : null);
        title = Format.FullName(student.LastName, student.MiddleName, student.FirstName);
        subtitle = $"Matricule {student.Matricule} · {enrollment?.PromotionCode ?? "sans promotion"} · {Labels.Status(enrollment?.Status ?? "")}";
        Raise(nameof(Title));
        Raise(nameof(Subtitle));

        Details.Clear();
        Details.Add(new InfoLine("Matricule", student.Matricule));
        Details.Add(new InfoLine("Sexe", Labels.Gender(student.Gender)));
        Details.Add(new InfoLine("Date de naissance", student.BirthDate is { } b ? $"{Format.Date(b)}{(student.BirthPlace is { } p ? $" à {p}" : string.Empty)}" : "—"));
        Details.Add(new InfoLine("Nationalité", student.Nationality ?? "—"));
        Details.Add(new InfoLine("Adresse", student.Address ?? "—"));
        Details.Add(new InfoLine("Téléphone", student.Phone ?? "—"));
        Details.Add(new InfoLine("E-mail", student.Email ?? "—"));
        Details.Add(new InfoLine("Parent ou tuteur", string.IsNullOrWhiteSpace(student.GuardianName) ? "—" : $"{student.GuardianName} ({student.GuardianRelation ?? "lien non précisé"})"));
        Details.Add(new InfoLine("Contacts parents", string.Join(" · ", new[] { student.GuardianPhone, student.GuardianPhone2 }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } c ? c : "—"));
        Details.Add(new InfoLine("Observations", student.Notes ?? "—"));

        Enrollments.Clear();
        foreach (var e in student.Enrollments)
            Enrollments.Add(new EnrollmentRow(e.AcademicYear, $"{e.PromotionCode} · {e.PromotionName}", Format.Date(e.EnrolledOn), Labels.Status(e.Status),
                Tone(e.Status), e.ExitDate is { } x ? $"{Format.Date(x)}{(e.ExitReason is null ? string.Empty : $" · {e.ExitReason}")}" : "—"));

        Lines.Clear();
        Receipts.Clear();
        Sponsorships.Clear();
        Totals.Clear();
        if (enrollment is null)
        {
            Totals.Add(new KpiVm("Compte", "Sans inscription", "Inscrivez l'apprenant pour ouvrir son compte.", ThemeBrush.Get("ToneNeutral"), MaterialIconKind.AccountOffOutline));
            return;
        }

        var account = await Shell.Api.GetAsync<AccountDto>($"api/inscriptions/{enrollment.Id}/compte");
        Totals.Add(new KpiVm("Solde à payer", Money(account.Balance), $"Total net de l'année {Money(account.TotalNet)}", ThemeBrush.Get(account.Balance > 0 ? "ToneOrange" : "ToneGreen"), MaterialIconKind.WalletOutline));
        Totals.Add(new KpiVm("Reste exigible", Money(account.Overdue), $"{account.OverdueCount} échéance(s) en retard", ThemeBrush.Get(account.Overdue > 0 ? "ToneRed" : "ToneGreen"), MaterialIconKind.AlertCircleOutline));
        Totals.Add(new KpiVm("Déjà payé", Money(account.TotalPaid), $"Pris en charge : {Money(account.TotalCovered)}", ThemeBrush.Get("ToneGreen"), MaterialIconKind.CheckCircleOutline));
        Totals.Add(new KpiVm("Ancienneté", account.AgingBucket, $"Année {account.AcademicYear}", ThemeBrush.Get("ToneBlue"), MaterialIconKind.ProgressClock));

        foreach (var l in account.Lines)
            Lines.Add(new AccountRow(l.Label, Format.Date(l.DueDate), Money(l.Amount), Money(l.Covered), Money(l.Paid), Money(l.Remaining),
                Labels.Status(l.Status), Tone(l.Status)));

        foreach (var r in account.Receipts)
            Receipts.Add(new ReceiptLineVm(r.Id, r.Number, Format.Date(r.Date), Money(r.Amount), Labels.Method(r.Method)));

        var sponsorships = await Shell.Api.GetAsync<List<SponsorshipDto>>($"api/inscriptions/{enrollment.Id}/prises-en-charge");
        foreach (var s in sponsorships)
            Sponsorships.Add(new SponsorshipRow(s.Id, s.SponsorName, s.Reason, Format.Date(s.EffectiveFrom), Money(s.CoveredTotal),
                s.IsActive ? "Active" : "Terminée", s.IsActive));
    }

    public override async Task PrintAsync() => await StatementAsync();

    public async Task EditAsync()
    {
        if (student is null) return;
        var saved = await StudentForms.EditAsync(Shell, student);
        if (saved) await RefreshAsync();
    }

    public async Task StatementAsync()
    {
        if (enrollment is null)
        {
            Shell.Notify("Cet apprenant n'a pas d'inscription : aucun relevé à produire.");
            return;
        }
        await Shell.Download($"api/inscriptions/{enrollment.Id}/releve.pdf", $"releve-{student?.Matricule ?? "apprenant"}.pdf");
    }

    private async Task ChangeStatusAsync()
    {
        if (enrollment is null) return;
        var form = new FormViewModel("Changer le statut", $"Inscription {enrollment.PromotionCode} · {enrollment.AcademicYear}", f =>
        {
            var status = f.ChoiceValue("statut")!;
            var reason = f.OptionalText("motif");
            if (status != "Actif" && reason is null)
            {
                f.Find("motif").Error = "Le motif est obligatoire pour sortir l'apprenant de la promotion.";
                return Task.FromResult(false);
            }
            return SaveAsync(HttpMethod.Put, $"api/inscriptions/{enrollment.Id}/statut",
                new { status, date = f.Day("date"), reason }, "Statut mis à jour.");
        });
        form.AddChoice("statut", "Nouveau statut", Labels.Statuses, enrollment.Status, required: true);
        form.AddDay("date", "Date d'effet", DateOnly.FromDateTime(DateTime.Today));
        form.Add("motif", "Motif", hint: "Obligatoire si l'apprenant quitte la promotion");
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task ChangePromotionAsync()
    {
        if (enrollment is null) return;
        var promotions = await PromotionChoicesAsync(withAll: false);
        var form = new FormViewModel("Changer de promotion", $"Actuellement : {enrollment.PromotionCode}", f =>
            SaveAsync(HttpMethod.Put, $"api/inscriptions/{enrollment.Id}/promotion",
                new { promotionId = Guid.Parse(f.ChoiceValue("promotion")!), enrolledOn = f.Day("date") }, "Promotion changée."));
        form.AddChoice("promotion", "Nouvelle promotion", promotions, enrollment.PromotionId.ToString(), required: true);
        form.AddDay("date", "Date d'effet", DateOnly.FromDateTime(DateTime.Today));
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task NewSponsorshipAsync()
    {
        if (enrollment is null) return;
        var promotion = (await PromotionsAsync()).FirstOrDefault(p => p.Id == enrollment.PromotionId);
        if (promotion is null || promotion.Fees.Count == 0)
        {
            Shell.Notify("Cette promotion n'a pas de frais à prendre en charge.");
            return;
        }
        var fees = promotion.Fees.ToList();
        var form = new FormViewModel("Nouvelle prise en charge", "Pourcentage couvert pour chaque frais, de 0 à 100.", f =>
        {
            var lines = fees
                .Select(fee => new { feeItemId = fee.Id, percent = f.Amount($"fee-{fee.Id}") })
                .Where(l => l.percent > 0)
                .ToList();
            if (lines.Count == 0)
            {
                f.Find($"fee-{fees[0].Id}").Error = "Indiquez au moins un pourcentage.";
                return Task.FromResult(false);
            }
            return SaveAsync(HttpMethod.Post, $"api/inscriptions/{enrollment.Id}/prises-en-charge",
                new { sponsorName = f.Text("sponsor"), reason = f.Text("motif"), effectiveFrom = f.Day("date"), lines }, "Prise en charge enregistrée.");
        });
        form.Add("sponsor", "Garant ou organisme", required: true);
        form.Add("motif", "Motif", required: true);
        form.AddDay("date", "Date d'effet", DateOnly.FromDateTime(DateTime.Today), required: true);
        foreach (var fee in fees) form.Add($"fee-{fee.Id}", $"{fee.Label} (%)", FieldKind.Amount, "0");
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task EndSelectedSponsorshipAsync()
    {
        if (enrollment is null || SelectedSponsorship is null)
        {
            Shell.Notify("Sélectionnez une prise en charge à terminer.");
            return;
        }
        var id = SelectedSponsorship.Id;
        var form = new FormViewModel("Terminer la prise en charge", SelectedSponsorship.Sponsor, f =>
            SaveAsync(HttpMethod.Post, $"api/inscriptions/{enrollment.Id}/prises-en-charge/{id}/fin", new { reason = f.Text("motif") }, "Prise en charge terminée."))
        { SubmitText = "Terminer", Danger = true };
        form.Add("motif", "Motif de fin", required: true);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task<bool> SaveAsync(HttpMethod method, string path, object body, string message)
    {
        await Shell.Api.SendAsync(method, path, body);
        Shell.Notify(message);
        return true;
    }

    private static IBrush Tone(string code) => ThemeBrush.Get(Labels.Tone(code) switch { "green" => "ToneGreen", "red" => "ToneRed", "orange" => "ToneOrange", _ => "ToneNeutral" });
}

/// <summary>Formulaires d'inscription et de modification d'un apprenant.</summary>
public static class StudentForms
{
    public static async Task CreateAsync(ShellViewModel shell)
    {
        var promotions = await shell.Api.GetAsync<List<PromotionDto>>("api/etablissement/promotions");
        var needsConfirmation = false;
        var form = new FormViewModel("Nouvelle inscription", "Le matricule est attribué automatiquement à l'enregistrement.", async f =>
        {
            var doubles = await shell.Api.GetAsync<List<DuplicateCandidateDto>>("api/apprenants/doublons" + ScoliaClient.Query(
                ("nom", f.Text("nom")), ("postNom", f.Text("postNom")), ("prenom", f.Text("prenom"))));
            if (doubles.Count > 0 && !needsConfirmation)
            {
                needsConfirmation = true;
                f.Fail($"Un apprenant porte déjà ce nom : {string.Join(", ", doubles.Select(d => $"{d.FullName} ({d.Matricule})"))}. Cliquez à nouveau sur Inscrire pour confirmer.");
                return false;
            }
            await shell.Api.SendAsync(HttpMethod.Post, "api/apprenants", new
            {
                identity = Identity(f, null),
                promotionId = Guid.Parse(f.ChoiceValue("promotion")!),
                enrolledOn = f.Day("inscription"),
                confirmDuplicate = needsConfirmation,
            });
            shell.Notify("Apprenant inscrit.");
            return true;
        })
        { SubmitText = "Inscrire" };

        AddIdentityFields(form, null);
        form.AddChoice("promotion", "Promotion", promotions.Select(Choices.Promotion).ToList(), null, required: true);
        form.AddDay("inscription", "Date d'inscription", DateOnly.FromDateTime(DateTime.Today));

        if (await shell.Ask(form) && shell.Current is StudentsPage page) await page.RefreshAsync();
    }

    public static async Task<bool> EditAsync(ShellViewModel shell, StudentDto s)
    {
        var form = new FormViewModel("Modifier l'apprenant", $"Matricule {s.Matricule}", async f =>
        {
            await shell.Api.SendAsync(HttpMethod.Put, $"api/apprenants/{s.Id}", Identity(f, s.Version));
            shell.Notify("Dossier enregistré.");
            return true;
        });
        AddIdentityFields(form, s);
        return await shell.Ask(form);
    }

    private static void AddIdentityFields(FormViewModel form, StudentDto? s)
    {
        form.Add("nom", "Nom", value: s?.LastName, required: true);
        form.Add("postNom", "Post-nom", value: s?.MiddleName, required: true);
        form.Add("prenom", "Prénom", value: s?.FirstName, required: true);
        form.AddChoice("sexe", "Sexe", Labels.Genders, s?.Gender ?? "M", required: true);
        form.AddDay("naissance", "Date de naissance", s?.BirthDate);
        form.Add("lieu", "Lieu de naissance", value: s?.BirthPlace);
        form.Add("nationalite", "Nationalité", value: s?.Nationality);
        form.Add("telephone", "Téléphone de l'apprenant", value: s?.Phone);
        form.Add("email", "E-mail", value: s?.Email);
        form.Add("adresse", "Adresse de résidence", value: s?.Address);
        form.Add("tuteur", "Parent ou tuteur", value: s?.GuardianName);
        form.Add("lien", "Lien", value: s?.GuardianRelation);
        form.Add("contact1", "Contact parent 1", value: s?.GuardianPhone);
        form.Add("contact2", "Contact parent 2", value: s?.GuardianPhone2);
        form.Add("notes", "Observations", FieldKind.Multiline, s?.Notes);
    }

    private static object Identity(FormViewModel f, long? version) => new
    {
        lastName = f.Text("nom"),
        middleName = f.Text("postNom"),
        firstName = f.Text("prenom"),
        gender = f.ChoiceValue("sexe") ?? "M",
        birthDate = f.Day("naissance"),
        birthPlace = f.OptionalText("lieu"),
        nationality = f.OptionalText("nationalite"),
        address = f.OptionalText("adresse"),
        phone = f.OptionalText("telephone"),
        email = f.OptionalText("email"),
        guardianName = f.OptionalText("tuteur"),
        guardianRelation = f.OptionalText("lien"),
        guardianPhone = f.OptionalText("contact1"),
        guardianPhone2 = f.OptionalText("contact2"),
        notes = f.OptionalText("notes"),
        version,
    };
}
