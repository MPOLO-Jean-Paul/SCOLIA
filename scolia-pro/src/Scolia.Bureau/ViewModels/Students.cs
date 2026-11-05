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
        if (Shell.Can(Perm.StudentsWrite)) Action("Nouvelle inscription", MaterialIconKind.AccountPlus, () => StudentForms.CreateAsync(Shell), primary: true);
        Action("Ouvrir la fiche", MaterialIconKind.Account, OpenSelectedAsync, open: true);
        if (Shell.Can(Perm.ReportsRead)) Action("Suivi par mois", MaterialIconKind.CalendarCheck, () => Shell.Open(() => new MonthlyPage(Shell)));
        Action("Préparer hors ligne", MaterialIconKind.Download, PrepareOfflineAsync);
        ActionRefresh();
        MenuAction("Aperçu rapide", MaterialIconKind.Eye, () => WithSelected(id => QuickView.StudentAsync(Shell, id)));
        if (Shell.Can(Perm.PaymentsCreate)) MenuAction("Encaisser", MaterialIconKind.HandCoin, () => WithSelected(id => Shell.Open(() => new CashierPage(Shell, id))));
        if (Shell.Can(Perm.StudentsWrite)) MenuAction("Renommer", MaterialIconKind.Rename, () => WithSelected(async id =>
        {
            if (await StudentForms.RenameAsync(Shell, id)) await RefreshAsync();
        }));
        if (CanDelete(Shell)) MenuAction("Supprimer le dossier", MaterialIconKind.Delete, () => WithSelected(async id =>
        {
            if (await StudentForms.DeleteAsync(Shell, id)) await RefreshAsync();
        }), danger: true);
        ClearFilters = new AsyncCommand(async () =>
        {
            debounce?.Stop();
            search = string.Empty;
            promotion = Promotions.Count > 0 ? Promotions[0] : null;
            status = StatusChoices[0];
            page = 1;
            Selected = null;
            Raise(nameof(Search));
            Raise(nameof(Promotion));
            Raise(nameof(Status));
            await RefreshAsync();
        });
        PreviousPage = new AsyncCommand(async () => { page--; await RefreshAsync(); });
        NextPage = new AsyncCommand(async () => { page++; await RefreshAsync(); });
    }

    /// <summary>
    /// Consentement explicite avant le téléchargement des fiches nominatives sur l'appareil.
    /// La copie reste isolée dans le cache chiffré du compte, sans exposition publique.
    /// </summary>
    private async Task PrepareOfflineAsync()
    {
        if (!Shell.Api.IsOnline)
        {
            Shell.Notify("Connectez-vous au serveur de l'établissement avant de préparer cet appareil.");
            return;
        }
        if (Shell.Api.Offline?.User is not { Length: > 0 })
        {
            Shell.Notify("Connectez-vous à votre compte avant de préparer les données locales.");
            return;
        }

        var form = new FormViewModel("Préparer cet appareil hors ligne",
            "SCOLIA téléchargera les listes et fiches auxquelles votre compte a accès. Les copies seront chiffrées sur cet appareil.",
            async f =>
            {
                var finance = Shell.Can(Perm.PaymentsRead) && f.Flag("comptes");
                f.Message = "Téléchargement des données autorisées…";
                var result = await OfflinePreparation.RunAsync(Shell.Api, finance, p =>
                {
                    if (p.Completed % 10 == 0 || p.Completed == p.Expected || p.Completed <= 5)
                        f.Message = $"{p.Stage} · {p.Completed}/{p.Expected} éléments traités.";
                });

                var summary = $"{result.Details}/{result.Expected} fiches préparées"
                    + (finance ? $" · {result.Accounts} comptes de paiement" : string.Empty)
                    + (result.Errors > 0 ? $" · {result.Errors} élément(s) indisponible(s)." : ".");
                Shell.Notify(result.Complete ? "Préparation hors ligne terminée : " + summary
                    : "Préparation partielle : " + summary + " Vérifiez la connexion et relancez la préparation.");
                return true;
            })
        {
            SubmitText = "Préparer maintenant",
            Message = "Réalisez cette préparation avant une période sans réseau. Les écritures restent soumises à la synchronisation et à la validation du serveur."
        };
        if (Shell.Can(Perm.PaymentsRead))
            form.AddFlag("comptes", "Inclure les comptes scolaires autorisés (montants et mensualités)", false);
        await Shell.Ask(form);
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
    public string EmptyTitle { get; private set; } = "Aucun apprenant trouvé";
    public string EmptyHint { get; private set; } = string.Empty;
    public string PageText => $"Page {page} sur {pageCount}";
    public bool CanPrevious => page > 1;
    public bool CanNext => page < pageCount;

    public AsyncCommand PreviousPage { get; }
    public AsyncCommand NextPage { get; }
    public AsyncCommand ClearFilters { get; }

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            IReadOnlyList<ChoiceItem> choices;
            try { choices = await PromotionChoicesAsync(withAll: true); }
            catch (OfflineException) when (!Shell.Api.IsOnline)
            {
                // Même si les promotions n'ont jamais été consultées, la liste
                // locale des apprenants peut encore être ouverte sans attendre.
                choices = [new ChoiceItem(string.Empty, "Toutes les promotions")];
            }
            Promotions = choices;
            promotion = choices[0];
            status = StatusChoices[0];
            Raise(nameof(Promotion));
            Raise(nameof(Status));
            ready = true;
        }

        var query = ScoliaClient.Query(
            ("q", Search.Trim()), ("promotionId", Promotion?.Value), ("statut", Status?.Value), ("page", page), ("taille", PageSize));
        OfflineStudentSearch.Result? local = null;
        if (!Shell.Api.IsOnline && Shell.Api.Offline is { } store)
        {
            var promotionCode = Promotion?.Value is { Length: > 0 } selectedPromotion
                ? Promotions.FirstOrDefault(x => x.Value == selectedPromotion)?.Label.Split('·')[0].Trim()
                : null;
            local = OfflineStudentSearch.Search(store, Search.Trim(), promotionCode, Status?.Value, page, PageSize);
        }
        var result = local?.Page
            ?? await Shell.Api.GetAsync<PagedResult<StudentListItem>>("api/apprenants" + query);
        if (local is not null) page = local.Page.Page;

        total = result.Total;
        pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        if (page > pageCount) page = pageCount;

        CollectionSync.Apply(Items, result.Items.Select(s =>
        {
            var late = s.Overdue > 0;
            return new StudentRow(
                s.Id, s.Matricule, Format.FullName(s.LastName, s.MiddleName, s.FirstName), Labels.Gender(s.Gender), s.PromotionCode ?? "—",
                Labels.Status(s.Status ?? string.Empty), ThemeBrush.Get(Labels.Tone(s.Status ?? string.Empty) switch { "green" => "ToneGreen", "red" => "ToneRed", "orange" => "ToneOrange", _ => "ToneNeutral" }),
                s.GuardianPhone ?? "—", Money(s.Balance), late ? Money(s.Overdue) : "À jour", ThemeBrush.Get(late ? "ToneRed" : "ToneGreen"));
        }));

        subtitle = Format.Count(total, "apprenant", "apprenants") + (Promotion is { Value.Length: > 0 } ? $" · {Promotion.Label}" : string.Empty);
        if (!Shell.Api.IsOnline)
            subtitle += local is { Complete: false }
                ? $" · recherche locale ({local.Loaded}/{local.Registered} dossiers disponibles)"
                : " · copie locale, non actualisée";
        // Liste vide : sans filtre, l'établissement n'a encore aucun apprenant ; avec un filtre, il faut l'élargir.
        var filtered = Search.Trim().Length > 0 || Promotion is { Value.Length: > 0 } || Status is { Value.Length: > 0 };
        EmptyTitle = filtered ? "Aucun apprenant trouvé" : "Aucun apprenant inscrit";
        EmptyHint = local is { Complete: false }
            ? "Recherche limitée aux dossiers téléchargés sur cet appareil. Reconnectez-vous pour voir le registre complet."
            : filtered ? "Modifiez la recherche, ou choisissez « Toutes les promotions » et « Tous les statuts »."
            : "Inscrivez le premier avec « Nouvelle inscription ».";
        Raise(nameof(EmptyTitle));
        Raise(nameof(EmptyHint));
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

    /// <summary>Supprimer un dossier : le secrétariat, qui inscrit les apprenants et gère les sauvegardes.</summary>
    public static bool CanDelete(ShellViewModel shell) => shell.Can(Perm.StudentsWrite) && shell.Can(Perm.BackupManage);

    private Task WithSelected(Func<Guid, Task> work)
    {
        if (Selected is null)
        {
            Shell.Notify("Sélectionnez un apprenant dans la liste.");
            return Task.CompletedTask;
        }
        return work(Selected.Id);
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
public sealed record SponsorshipRow(Guid Id, string Sponsor, string Reason, string Since, string Covered, string Status, bool Active, string Type = "Historique");

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
        if (Shell.Can(Perm.StudentsWrite)) Action("Modifier", MaterialIconKind.Pencil, EditAsync);
        if (Shell.Can(Perm.StudentsWrite)) Action("Renommer", MaterialIconKind.Rename, RenameAsync);
        if (Shell.Can(Perm.PaymentsRead)) Action("Relevé de compte", MaterialIconKind.FileDocument, StatementAsync);
        if (Shell.Can(Perm.StudentsWrite)) Action("Changer le statut", MaterialIconKind.AccountSwitch, ChangeStatusAsync);
        if (Shell.Can(Perm.StudentsWrite)) Action("Changer de promotion", MaterialIconKind.SwapHorizontal, ChangePromotionAsync);
        if (Shell.Can(Perm.StudentsWrite)) Action("Prise en charge", MaterialIconKind.ShieldAccount, NewSponsorshipAsync);
        if (Shell.Can(Perm.PaymentsRead)) Action("Relancer par WhatsApp", MaterialIconKind.Whatsapp, RemindAsync);
        if (StudentsPage.CanDelete(Shell)) Action("Supprimer le dossier", MaterialIconKind.Delete, DeleteAsync, danger: true);
        ActionRefresh();
    }

    private async Task RenameAsync()
    {
        if (await StudentForms.RenameAsync(Shell, studentId)) await RefreshAsync();
    }

    /// <summary>Dossier supprimé : la fiche n'existe plus, on revient à la liste des apprenants.</summary>
    private async Task DeleteAsync()
    {
        if (!await StudentForms.DeleteAsync(Shell, studentId)) return;
        await Shell.LeaveDeletedAsync(this, () => new StudentsPage(Shell));
    }

    private string? whatsAppLink;

    /// <summary>Relance du parent depuis la fiche : le message cite le reste exigible et les mois en retard.</summary>
    private Task RemindAsync()
    {
        if (whatsAppLink is { Length: > 0 } link) Files.Open(link);
        else Shell.Notify(student?.GuardianPhone is null && student?.GuardianPhone2 is null
            ? "Aucun contact parent sur la fiche : ajoutez-le avec « Modifier »."
            : "Rien à relancer : aucun mois en retard.");
        return Task.CompletedTask;
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
        Details.Add(new InfoLine("Adresse", student.Address ?? "—"));
        Details.Add(new InfoLine("Parent ou tuteur", string.IsNullOrWhiteSpace(student.GuardianName) ? "—" : student.GuardianName));
        Details.Add(new InfoLine("Contact parent", student.GuardianPhone ?? student.GuardianPhone2 ?? "—"));

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
        whatsAppLink = account.WhatsAppLink;
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
                s.IsActive ? "Active" : "Terminée", s.IsActive, s.Type));
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
        var months = promotion.Fees.Where(fee =>
                fee.Kind.Equals("Mensualite", StringComparison.OrdinalIgnoreCase))
            .OrderBy(fee => fee.DueDate).ThenBy(fee => fee.Order).ToList();
        var options = new List<ChoiceItem> { new("Complete", "Complète — tous les frais et tous les mois") };
        if (months.Count > 0)
            options.Add(new("PartielleMensuelle", "Partielle — montant à préciser pour chaque mois"));

        var form = new FormViewModel("Nouvelle prise en charge",
            "La couverture complète couvre tous les frais sans saisie mensuelle. En partielle, indiquez les montants des mois concernés.", f =>
        {
            var type = f.ChoiceValue("type");
            if (type is not ("Complete" or "PartielleMensuelle"))
            {
                f.Find("type").Error = "Choisissez le type de prise en charge.";
                return Task.FromResult(false);
            }
            var lines = new List<object>();
            if (type == "PartielleMensuelle")
            {
                var amounts = months.Select(month =>
                    (Month: month, Amount: f.Amount($"fee-{month.Id}"))).ToList();
                foreach (var (month, amount) in amounts)
                {
                    if (amount < 0 || amount > month.Amount)
                    {
                        f.Find($"fee-{month.Id}").Error =
                            $"Le montant doit être compris entre 0 et {Money(month.Amount)}.";
                        return Task.FromResult(false);
                    }
                }
                var selected = amounts.Where(x => x.Amount > 0).ToList();
                if (selected.Count == 0)
                {
                    f.Find($"fee-{months[0].Id}").Error =
                        "Indiquez le montant pris en charge pour au moins un mois.";
                    return Task.FromResult(false);
                }
                lines.AddRange(selected.Select(x => (object)new { feeItemId = x.Month.Id, amount = x.Amount }));
            }
            return SaveAsync(HttpMethod.Post, $"api/inscriptions/{enrollment.Id}/prises-en-charge",
                new { sponsorName = f.Text("sponsor"), reason = f.Text("motif"),
                    effectiveFrom = f.Day("date"), type, lines },
                "Prise en charge enregistrée.");
        });
        form.AddChoice("type", "Type de prise en charge", options, required: false);
        form.Add("sponsor", "Garant ou organisme", required: true);
        form.Add("motif", "Motif", required: true);
        form.AddDay("date", "Date d'effet", DateOnly.FromDateTime(DateTime.Today), required: true);

        // Ne demander ni montant ni pourcentage quand l'utilisateur choisit Complète.
        // En partielle, chaque mois a son propre montant ; les mois à 0 restent à charge.
        form.Changed = field =>
        {
            if (field.Key != "type") return;
            var partial = form.ChoiceValue("type") == "PartielleMensuelle";
            foreach (var extra in form.Fields.Where(f => f.Key.StartsWith("fee-", StringComparison.Ordinal)
                || f.Key == "monthly-header").ToList())
                form.Fields.Remove(extra);
            if (!partial) return;
            form.Add("monthly-header", "Montants par mois",
                FieldKind.Header, hint: "Remplissez seulement les mois pris en charge. Les autres restent à 0.");
            foreach (var month in months)
                form.Add($"fee-{month.Id}", $"{month.Label} — montant pris en charge",
                    FieldKind.Amount, "0", hint: $"Mensualité prévue : {Money(month.Amount)}.");
        };
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
        var promotions = (await shell.Api.GetAsync<List<PromotionDto>>("api/etablissement/promotions")).Where(p => p.IsActive).ToList();
        // Pas encore de promotion : on la crée d'abord, l'apprenant doit en recevoir une à l'inscription.
        if (promotions.Count == 0)
        {
            if (!shell.Can(Perm.SettingsWrite))
            {
                await shell.Confirm("Aucune promotion", "Aucune promotion n'est ouverte. Demandez au secrétariat d'en créer une dans Paramètres, Promotions et frais.", "Fermer");
                return;
            }
            if (!await shell.Confirm("Aucune promotion", "L'apprenant doit être inscrit dans une promotion, et aucune n'existe encore. Créer une promotion maintenant ?", "Créer une promotion"))
                return;
            await SettingsPage.NewPromotionAsync(shell);
            promotions = (await shell.Api.GetAsync<List<PromotionDto>>("api/etablissement/promotions")).Where(p => p.IsActive).ToList();
            if (promotions.Count == 0) return;
        }
        string? confirmedIdentity = null;
        var form = new FormViewModel("Nouvelle inscription", "Le matricule est attribué automatiquement à l'enregistrement.", async f =>
        {
            // La confirmation d'un homonyme ne vaut que pour la saisie vérifiée.
            // Changer le nom, la promotion ou les coordonnées impose une nouvelle vérification.
            var identityKey = string.Join("|", f.ChoiceValue("promotion"), f.Text("nom").ToUpperInvariant(),
                f.Text("postNom").ToUpperInvariant(), f.Text("prenom").ToUpperInvariant(),
                f.ChoiceValue("sexe"), f.Day("naissance"), f.Text("lieu"), f.Text("contact1"), f.Day("inscription"));
            List<DuplicateCandidateDto> doubles;
            try
            {
                doubles = await shell.Api.GetAsync<List<DuplicateCandidateDto>>("api/apprenants/doublons" + ScoliaClient.Query(
                    ("nom", f.Text("nom")), ("postNom", f.Text("postNom")), ("prenom", f.Text("prenom"))));
            }
            catch (OfflineException) when (!shell.Api.IsOnline)
            {
                // Le serveur revérifiera les doublons au rejeu. En attendant,
                // vérifier les dossiers effectivement répliqués sur cet appareil.
                doubles = LocalDuplicates(shell.Api.Offline, f.Text("nom"), f.Text("postNom"), f.Text("prenom"));
            }
            if (doubles.Count > 0 && confirmedIdentity != identityKey)
            {
                confirmedIdentity = identityKey;
                f.Fail($"Un dossier possède déjà cette identité : {string.Join(", ", doubles.Select(d => $"{d.FullName} ({d.Matricule})"))}. Vérifiez les informations, puis cliquez à nouveau sur Inscrire uniquement s'il s'agit d'une autre personne.");
                return false;
            }
            if (doubles.Count == 0) confirmedIdentity = null;
            try
            {
                await shell.Api.SendAsync(HttpMethod.Post, "api/apprenants", new
                {
                    identity = Identity(f, null),
                    promotionId = Guid.Parse(f.ChoiceValue("promotion")!),
                    enrolledOn = f.Day("inscription"),
                    confirmDuplicate = doubles.Count > 0 && confirmedIdentity == identityKey,
                });
                shell.Notify("Apprenant inscrit.");
            }
            catch (OfflineQueuedException)
            {
                shell.Notify("Inscription enregistrée sur cet appareil. Matricule provisoire LOCAL ; confirmation automatique au retour du serveur.");
            }
            return true;
        })
        { SubmitText = "Inscrire" };

        // La promotion d'abord, comme dans la version 79 : elle donne le début du matricule.
        form.AddChoice("promotion", "Promotion", promotions.Select(Choices.Promotion).ToList(), null, required: true);
        // Pour une nouvelle inscription, ne pas affecter implicitement la première promotion :
        // l'utilisateur choisit le niveau scolaire. S'il n'en existe qu'une, il n'y a rien à choisir.
        if (promotions.Count > 1) form.Find("promotion").Choice = null;
        AddIdentityFields(form, null);
        form.AddDay("inscription", "Date d'inscription", DateOnly.FromDateTime(DateTime.Today));

        if (await shell.Ask(form) && shell.Current is StudentsPage page) await page.RefreshAsync();
    }

    private static List<DuplicateCandidateDto> LocalDuplicates(OfflineStore? store,
        string last, string middle, string first)
    {
        if (store is null) return [];
        var result = new Dictionary<Guid, DuplicateCandidateDto>();
        for (var pageNumber = 1; pageNumber <= 20; pageNumber++)
        {
            var page = OfflineStudentSearch.Search(store, last, null, null, pageNumber);
            if (page is null) break;
            foreach (var student in page.Page.Items)
            {
                if (!string.Equals(last, student.LastName, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(middle, student.MiddleName, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(first, student.FirstName, StringComparison.OrdinalIgnoreCase)) continue;
                result[student.Id] = new DuplicateCandidateDto(student.Id, student.Matricule,
                    Format.FullName(student.LastName, student.MiddleName, student.FirstName), student.PromotionCode);
            }
            if (page.Page.Items.Count < page.Page.PageSize) break;
        }
        return result.Values.ToList();
    }

    public static async Task<bool> EditAsync(ShellViewModel shell, StudentDto s)
    {
        var form = new FormViewModel("Modifier l'apprenant", $"Matricule {s.Matricule}", async f =>
        {
            try
            {
                await shell.Api.SendAsync(HttpMethod.Put, $"api/apprenants/{s.Id}", Identity(f, s));
                shell.Notify("Dossier enregistré.");
            }
            catch (OfflineQueuedException)
            {
                shell.Notify("Dossier modifié localement ; l'envoi au serveur se fera automatiquement.");
            }
            return true;
        });
        AddIdentityFields(form, s);
        // Un seul poste modifie un dossier à la fois : les autres voient qui le fait.
        return await shell.Sync.WithLockAsync($"dossier:{s.Id}", $"Modification du dossier {s.Matricule}", () => shell.Ask(form));
    }

    /// <summary>
    /// Renomme le dossier : nom, post-nom et prénom seulement. Le matricule, les paiements, les notes et
    /// l'historique restent attachés au dossier ; un homonyme demande une seconde confirmation.
    /// </summary>
    public static async Task<bool> RenameAsync(ShellViewModel shell, Guid id)
    {
        var s = await shell.Api.GetAsync<StudentDto>($"api/apprenants/{id}");
        var before = Format.FullName(s.LastName, s.MiddleName, s.FirstName);
        string? confirmedIdentity = null;
        var form = new FormViewModel("Renommer l'apprenant", $"{before} · matricule {s.Matricule}", async f =>
        {
            var (last, middle, first) = (f.Text("nom"), f.Text("postNom"), f.Text("prenom"));
            var after = Format.FullName(last, middle, first);
            if (string.Equals(after, before, StringComparison.Ordinal))
            {
                shell.Notify("Le nom n'a pas changé.");
                return true;
            }
            var doubles = await shell.Api.GetAsync<List<DuplicateCandidateDto>>("api/apprenants/doublons" + ScoliaClient.Query(
                ("nom", last), ("postNom", middle), ("prenom", first), ("exclure", id)));
            var identityKey = string.Join("|", last.ToUpperInvariant(), middle.ToUpperInvariant(), first.ToUpperInvariant());
            if (doubles.Count > 0 && confirmedIdentity != identityKey)
            {
                confirmedIdentity = identityKey;
                f.Fail($"Un autre dossier possède déjà ce nom : {string.Join(", ", doubles.Select(d => $"{d.FullName} ({d.Matricule})"))}. Vérifiez l'identité et cliquez à nouveau sur Renommer uniquement s'il s'agit d'une autre personne.");
                return false;
            }
            if (doubles.Count == 0) confirmedIdentity = null;
            await shell.Api.SendAsync(HttpMethod.Put, $"api/apprenants/{id}", new
            {
                lastName = last, middleName = middle, firstName = first,
                gender = s.Gender, birthDate = s.BirthDate, birthPlace = s.BirthPlace, nationality = s.Nationality, address = s.Address,
                phone = s.Phone, email = s.Email, guardianName = s.GuardianName, guardianRelation = s.GuardianRelation,
                guardianPhone = s.GuardianPhone, guardianPhone2 = s.GuardianPhone2, notes = s.Notes, version = s.Version,
            });
            shell.Notify($"Dossier renommé : {after}.", SoundKind.Success);
            return true;
        })
        {
            SubmitText = "Renommer",
            Message = "Le matricule, les reçus, les notes et l'historique restent sur le dossier.",
        };
        form.Add("nom", "Nom", value: s.LastName, required: true);
        form.Add("postNom", "Post-nom", value: s.MiddleName, required: true);
        form.Add("prenom", "Prénom", value: s.FirstName, required: true);
        return await shell.Sync.WithLockAsync($"dossier:{s.Id}", $"Modification du dossier {s.Matricule}", () => shell.Ask(form));
    }

    /// <summary>
    /// Supprime le dossier, même s'il a déjà payé : inscriptions, reçus, notes, présences, discipline et prises
    /// en charge partent avec lui. Le mot de passe du compte connecté confirme ; le serveur fait une sauvegarde
    /// juste avant, d'où le dossier se récupère, et garde la trace au journal.
    /// </summary>
    public static async Task<bool> DeleteAsync(ShellViewModel shell, Guid id)
    {
        var s = await shell.Api.GetAsync<StudentDto>($"api/apprenants/{id}");
        var name = Format.FullName(s.LastName, s.MiddleName, s.FirstName);
        var current = s.Enrollments.FirstOrDefault(e => e.Status == "Actif") ?? (s.Enrollments.Count > 0 ? s.Enrollments[0] : null);
        var paid = string.Empty;
        if (current is not null && shell.Can(Perm.PaymentsRead))
        {
            var account = await shell.Api.GetAsync<AccountDto>($"api/inscriptions/{current.Id}/compte");
            var valid = account.Receipts.Where(r => r.Status == "Valide").ToList();
            if (valid.Count > 0)
                paid = $" Il a déjà payé {Format.Money(valid.Sum(r => r.Amount), shell.Session?.CurrencySymbol ?? string.Empty)} en {Format.Count(valid.Count, "reçu", "reçus")} cette année : ces reçus seront effacés aussi, et la caisse ne les comptera plus.";
        }
        var form = new FormViewModel("Supprimer le dossier", $"{name} · matricule {s.Matricule}", async f =>
        {
            await shell.Api.SendAsync<ErasureSummaryDto>(HttpMethod.Post, "api/effacement/apprenants",
                new { scope = ErasureScope.Selection, studentIds = new[] { id }, password = f.Text("motDePasse") });
            shell.Notify($"Dossier de {name} supprimé. Une sauvegarde a été faite juste avant.", SoundKind.Success);
            return true;
        })
        {
            SubmitText = "Supprimer le dossier",
            Danger = true,
            Message = $"Le dossier, ses inscriptions, ses notes, ses présences et sa discipline seront effacés.{paid} Une sauvegarde est faite juste avant : le dossier se récupère depuis Sauvegardes.",
        };
        form.Add("motDePasse", "Votre mot de passe", FieldKind.Secret, required: true, hint: "Celui du compte connecté, pour confirmer");
        return await shell.Sync.WithLockAsync($"dossier:{s.Id}", $"Suppression du dossier {s.Matricule}", () => shell.Ask(form));
    }

    /// <summary>
    /// Champs de la fiche : identité, naissance, parent, son contact et l'adresse. Un seul numéro est saisi,
    /// celui du parent : c'est lui que visent les relances WhatsApp et SMS.
    /// </summary>
    private static void AddIdentityFields(FormViewModel form, StudentDto? s)
    {
        form.AddHeader("Identité de l'apprenant", "Renseignez les noms tels qu'ils apparaissent sur le dossier scolaire.");
        form.Add("nom", "Nom", value: s?.LastName, required: true);
        form.Add("postNom", "Post-nom", value: s?.MiddleName, required: true);
        form.Add("prenom", "Prénom", value: s?.FirstName, required: true);
        form.AddChoice("sexe", "Sexe", Labels.Genders, s?.Gender, required: true);
        if (s is null) form.Find("sexe").Choice = null;
        form.AddDay("naissance", "Date de naissance", s?.BirthDate, required: true);
        form.Add("lieu", "Lieu de naissance", value: s?.BirthPlace, required: true);
        form.AddHeader("Contact et adresse", "Le contact du parent servira aux communications et aux rappels de paiement.");
        form.Add("tuteur", "Parent ou tuteur", value: s?.GuardianName);
        form.Add("adresse", "Adresse", value: s?.Address, hint: "Quartier, avenue et numéro");
        form.Add("contact1", "Contact parent (relances WhatsApp)", FieldKind.Phone, value: s?.GuardianPhone, required: true,
            hint: "10 chiffres, ex. 0812345678");
    }

    /// <summary>Les données d'un ancien dossier qui ne sont plus saisies, nationalité, téléphones, e-mail, lien et observations, sont gardées telles quelles.</summary>
    private static object Identity(FormViewModel f, StudentDto? s) => new
    {
        lastName = f.Text("nom"),
        middleName = f.Text("postNom"),
        firstName = f.Text("prenom"),
        gender = f.ChoiceValue("sexe") ?? "M",
        birthDate = f.Day("naissance"),
        birthPlace = f.OptionalText("lieu"),
        nationality = s?.Nationality,
        address = f.OptionalText("adresse"),
        phone = s?.Phone,
        email = s?.Email,
        guardianName = f.OptionalText("tuteur"),
        guardianRelation = s?.GuardianRelation,
        guardianPhone = f.OptionalText("contact1"),
        guardianPhone2 = s?.GuardianPhone2,
        notes = s?.Notes,
        version = s?.Version,
    };
}
