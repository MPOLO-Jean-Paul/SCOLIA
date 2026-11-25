using Avalonia.Threading;
using Scolia.Bureau.ViewModels;
using Scolia.Bureau.Ribbon;
using Scolia.Domain.School;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Adaptateur du contrat Flutter v1. Aucune ouverture HTTP, ni nouveau stockage de secrets.
/// L'hôte Android devra enregistrer le MethodChannel et afficher l'interface native lorsque
/// Stage vaut nativeScreen (verrouillage, dialogue ou écran métier non migré).
/// </summary>
public sealed class FlutterCommandAdapter(ShellViewModel shell)
{
    public sealed record Snapshot(int Protocol, string Stage, string School, string User,
        string Year, IReadOnlyList<string> Modules, string Notice, StudentListing? Students = null, MonthlyListing? Monthly = null, IReadOnlyList<ModuleEntry>? Catalog = null, WorkspaceHeader? Header = null, StudentProfile? Profile = null, EnrollmentForm? Enrollment = null, CashierScreen? Cashier = null, ReceiptJournal? Journal = null, SchoolRegister? Register = null);

    public sealed record RegisterRow(string Id, string Name, string Code, string Status, string Note,
        IReadOnlyList<ProfileValue> Details);
    public sealed record SchoolRegister(string Kind, string Title, string Subtitle, string Promotion, string Date,
        string Measure, bool Summary, bool CanWrite, bool Pending, bool Dirty, string Token,
        IReadOnlyList<FilterChoice> Promotions, IReadOnlyList<FilterChoice> Options, IReadOnlyList<RegisterRow> Rows, MarkInput? Marks = null, string Overview = "", string Context = "");
    private StaffPage? staffDraft;
    private StaffAttendancePage? pointageDraft;
    private TeachingPage? teachingDraft;
    private AnonymousCodesPage? codesDraft;
    private MarksPage? marksDraft;
    private DeliberationPage? deliberationDraft;
    private EncodingProgressPage? progressDraft;
    private LmdPlanningPage? lmdDraft;
    private SettingsPage? settingsDraft;
    private UsersPage? usersDraft;
    private SchoolRegister DescribeSettings(SettingsPage p)
    {
        var selected = p.Section?.Value ?? "";
        var rows = selected switch
        {
            "annees" => p.Years.Select(x => new RegisterRow(x.Id.ToString(), x.Label, "", x.Status, "", [
                new("Période", x.Period), new("Année courante", x.IsCurrent ? "Oui" : "Non"),
                new("Paiements", x.Months), new("Échéances", x.Deadline)])).ToArray(),
            "sections" => p.SectionRows.Select(x => new RegisterRow(x.Id.ToString(), x.Name, x.Code, x.StatusLabel, "", [
                new("Promotions", x.Promotions), new("Effectif", x.Students)])).ToArray(),
            "promotions" => p.Promotions.Select(x => new RegisterRow(x.Id.ToString(), x.Name, x.Code, x.Active == "Oui" ? "Active" : "Inactive", "", [
                new("Section", x.Section), new("Niveau", x.Level), new("Effectif", x.Students),
                new(x.FeeLabel, x.Monthly), new("Total annuel", x.Annual)])).ToArray(),
            "frais" => p.Categories.Select(x => new RegisterRow(x.Id.ToString(), x.Name, "", x.Active == "Oui" ? "Active" : "Inactive", "", [
                new("Montant", x.Amount), new("Échéance", x.DueDate), new("Promotions", x.Promotions)])).ToArray(),
            "etablissement" when p.SchoolForm is { } form => form.Fields
                .Where(x => x.Kind != FieldKind.Header && x.Kind != FieldKind.Secret)
                .Select(x => new RegisterRow("", x.Label, "", "", "", [new("Valeur", x.Kind == FieldKind.Flag ? (x.Flag ? "Oui" : "Non") : x.Text)])).ToArray(),
            _ => [],
        };
        return new("settings", p.Title, p.SectionHint, "", selected, "", false,
            shell.Can(Perm.SettingsWrite) && p.SchoolSections.Any(x => x.Value == selected), false, false, registerToken,
            [], p.AllSections.Select(x => new FilterChoice(x.Value, x.Label)).ToArray(), rows,
            Overview: p.SectionTitle, Context: p.Subtitle);
    }
    private SchoolRegister DescribeUsers(UsersPage p) => new("users", p.Title, p.Subtitle, "", "", "", false, false, false, false,
        registerToken, [], [], p.Items.Select(x => new RegisterRow(x.Id.ToString(), x.DisplayName, x.UserName, x.Active, "", [
            new("Rôle", x.Role), new("Dernière connexion", x.LastLogin), new("Verrouillage", x.Locked)])).ToArray());

    private SchoolRegister DescribeLmd(LmdPlanningPage p)
    {
        var fees = p.FlutterPromotion?.Fees.Where(f => LmdInstallmentPlan.IsTranche(f.Label)).OrderBy(f => f.DueDate).ToList() ?? [];
        var rows = p.Structures.Select(s => new RegisterRow("", s.Name, s.Code, "Structure", "", [
            new("Type", TextCase.LmdKindLabel(s.Kind)), new("État", s.IsActive ? "Active" : "Inactive")]))
            .Concat(p.Semesters.Select(s => new RegisterRow("", s.Code, "", "Semestre", "", [
                new("Début", $"{s.StartDate:dd/MM/yyyy}"), new("Fin", $"{s.EndDate:dd/MM/yyyy}")])))
            .Concat(p.Program.Select(s => new RegisterRow("", s.Name, s.Code, "Programme", "", [
                new("Type", s.Kind == "TeachingUnit" ? "UE" : "EC")])))
            .Concat(fees.Select(f => new RegisterRow("", f.Label, "", "Tranche", "", [
                new("Montant", $"{f.Amount:0.##} {p.FlutterCurrency}"), new("Échéance", $"{f.DueDate:dd/MM/yyyy}")]))).ToArray();
        return new("lmd", p.Title, p.Subtitle, p.SelectedPromotion?.Value ?? "", "", "", false,
            p.FlutterPromotion is not null && shell.Can(Perm.SettingsWrite), false, false, registerToken,
            p.PromotionChoices.Select(x => new FilterChoice(x.Value, x.Label)).ToArray(), [], rows,
            Overview: p.FlutterPromotion is null ? "Aucune promotion sélectionnée." :
                $"{fees.Sum(f => f.Amount):0.##} {p.FlutterCurrency} · {fees.Count} tranche(s)");
    }
    private SchoolRegister DescribeDeliberation(DeliberationPage p) => new("deliberation", p.Title, p.Subtitle,
        p.Promotion?.Value ?? "", p.Period?.Value ?? "", "", p.OnlyCases, false, false, false, registerToken,
        p.Promotions.Select(x => new FilterChoice(x.Value, x.Label)).ToArray(),
        p.Periods.Select(x => new FilterChoice(x.Value, x.Label)).ToArray(),
        p.Items.Select(x => new RegisterRow(x.EnrollmentId.ToString(), x.FullName, x.Code == "—" ? "" : x.Code,
            x.Decision, x.Reason, [new("Rang", x.Rank), new("Points", x.Points), new("Pourcentage", x.Percent),
            new("Mention", x.Mention), new("Cotes manquantes", x.Missing), new("Échecs", x.Failed),
            new("Décision calculée", x.Computed)])).ToArray(), Overview: p.Summary, Context: p.JuryLine);
    private SchoolRegister DescribeProgress(EncodingProgressPage p) => new("progress", p.Title, p.Subtitle,
        p.Promotion?.Value ?? "", p.Period?.Value ?? "", "", p.OnlyLate, false, false, false, registerToken,
        p.Promotions.Select(x => new FilterChoice(x.Value, x.Label)).ToArray(),
        p.Periods.Select(x => new FilterChoice(x.Value, x.Label)).ToArray(),
        p.Items.Select(x => new RegisterRow("", x.Course, x.Number, x.Status, "", [
            new("Promotion", x.Promotion), new("Période", x.Kind), new("Enseignant", x.Teacher),
            new("Avancement", x.Progress), new("Échecs", x.Failed), new("Moyenne", x.Average)])).ToArray(), Overview: p.Summary);

    public sealed record MarkInput(string Rules, string Announcement, string Sheet, string Hint, string Maximum,
        string Code, string ValidatedCode, string Score, string Feedback, IReadOnlyList<FilterChoice> Kinds);
    private SchoolRegister DescribeMarks(MarksPage p) => new("marks", p.Title, p.Subtitle,
        p.Promotion?.Value ?? "", p.Course?.Id.ToString() ?? "", p.Kind?.Value ?? "",
        false, p.FlutterMayEncode, false, false, registerToken,
        p.Promotions.Select(x => new FilterChoice(x.Value, x.Label)).ToArray(),
        p.Courses.Select(x => new FilterChoice(x.Id.ToString(), $"{x.Number}. {x.Name}")).ToArray(),
        p.Rows.Where(x => !string.IsNullOrWhiteSpace(x.Code) && x.Code != "—")
            .Select(x => new RegisterRow("", "", x.Code, "", "", [])).ToArray(),
        new(p.EncodingRules, p.Notice, p.SheetTitle, p.SheetHint, p.ScoreMaximumText,
            p.CodeQuery, p.FlutterValidatedCode, p.ScoreText, p.CodeFeedback,
            p.Course is null ? [] : p.Kinds.Select(x => new FilterChoice(x.Value, x.Label)).ToArray()));

    private SchoolRegister DescribeCodes(AnonymousCodesPage p) => new("codes", p.Title, p.Subtitle, p.Promotion?.Value ?? "", "", "",
        false, false, false, false, registerToken,
        p.Promotions.Select(x => new FilterChoice(x.Value, x.Label)).ToArray(), [],
        p.Items.Select(x => new RegisterRow(x.EnrollmentId.ToString(), x.FullName, x.HasCode ? x.Code : "", x.Origin, "", [
            new("Promotion", x.Promotion)])).ToArray());

    private SchoolRegister DescribeStaff(StaffPage p) => new("staff", p.Title, p.Subtitle, "", p.Period, "",
        p.ShowPayroll, false, false, false, registerToken, [],
        p.Periods.Select(x => new FilterChoice(x.Value, x.Label)).ToArray(),
        p.ShowPayroll ? p.Payroll.Select(x => new RegisterRow(x.Id.ToString(), x.Name, x.Number, x.Status, "", [
            new("Fonction", x.Function), new("Base", x.Base), new("Primes", x.Allowances), new("Retenues", x.Deductions),
            new("Avances", x.Advances), new("Net", x.Net), new("Payé le", x.PaidOn)])).ToArray()
        : p.Staff.Select(x => new RegisterRow(x.Id.ToString(), x.Name, x.Number, x.Status, "", [
            new("Fonction", x.Function), new("Téléphone", x.Phone), new("Embauché le", x.Hired), new("Salaire", x.Salary)])).ToArray());
    private SchoolRegister DescribePointage(StaffAttendancePage p) => new("pointage", p.Title, p.Subtitle, "", "", "",
        p.PreviousMonth, false, false, false, registerToken, [], [],
        p.Members.Select(x => new RegisterRow(x.Id.ToString(), x.Name, "", x.Today, "", [
            new("Fonction", x.Function), new("Entrée aujourd’hui", x.Entry), new("Sortie aujourd’hui", x.Exit)]))
        .Concat(p.Bilan.Select(x => new RegisterRow("", x.Name, "", "", "", [
            new("Présences", x.Present), new("Retards", x.Late), new("Absences", x.Absent), new("Justifiées", x.Justified),
            new("Conversions", x.Conversions), new("Déductibles", x.Deductible)]))).ToArray());
    private SchoolRegister DescribeTeaching(TeachingPage p) => new("teaching", p.Title, p.Subtitle, p.Promotion?.Value ?? "", "", "",
        p.ShowArchived, false, false, false, registerToken,
        p.Promotions.Select(x => new FilterChoice(x.Value, x.Label)).ToArray(), [],
        p.Courses.Select(x => new RegisterRow(x.Id.ToString(), x.Name, x.Number, x.Status, "", [
            new("Promotion", x.Promotion), new("Heures", x.Hours), new("Cotation", x.MaxScore), new("Enseignant", x.Teacher)]))
        .Concat(p.Charges.Select(x => new RegisterRow("", x.Name, "", "", "", [
            new("Cours", x.Courses), new("Promotions", x.Promotions), new("Heures", x.Hours)]))).ToArray());

    private AttendancePage? attendanceDraft;
    private DisciplinePage? disciplineDraft;
    private object? registerOwner;
    private string registerToken = "";
    private bool registerDirty;
    private void ClearRegister()
    {
        attendanceDraft = null; disciplineDraft = null; staffDraft = null; pointageDraft = null; teachingDraft = null; codesDraft = null; marksDraft = null; deliberationDraft = null; progressDraft = null; lmdDraft = null; settingsDraft = null; usersDraft = null; registerOwner = null; registerToken = ""; registerDirty = false;
    }
    private SchoolRegister DescribeAttendance(AttendancePage page) => new("attendance", page.Title, page.Subtitle,
        page.Promotion?.Value ?? "", WireDate(page.Day), "", page.ShowSummary,
        shell.Can(Perm.AcademicsWrite) && !page.ShowSummary, page.CallPending && !page.ShowSummary, registerDirty, registerToken,
        page.Promotions.Select(x => new FilterChoice(x.Value, x.Label)).ToArray(),
        page.Statuses.Select(x => new FilterChoice(x.Value, x.Label)).ToArray(),
        page.ShowSummary ? page.Summary.Select(x => new RegisterRow("", x.Name, x.Matricule, "", "", [
            new("Présent", x.Present), new("Retard", x.Late), new("Absent", x.Absent), new("Justifié", x.Justified), new("Assiduité", x.Rate)])).ToArray()
            : page.Rows.Select(x => new RegisterRow(x.EnrollmentId.ToString(), x.FullName, x.Matricule, x.Status.Value, x.Note, [])).ToArray());
    private SchoolRegister DescribeDiscipline(DisciplinePage page) => new("discipline", page.Title, page.Subtitle,
        page.Promotion?.Value ?? "", "", page.Measure?.Value ?? "", false, shell.Can(Perm.AcademicsWrite), false, false, registerToken,
        page.Promotions.Select(x => new FilterChoice(x.Value, x.Label)).ToArray(),
        page.Measures.Select(x => new FilterChoice(x.Value, x.Label)).ToArray(),
        page.Items.Select(x => new RegisterRow(x.Id.ToString(), x.Name, x.Matricule, x.State, x.Reason, [
            new("Mesure", x.Measure), new("Décidé le", x.Date), new("Période", x.Period), new("Durée", x.Duration),
            new("Retour", x.ReturnFrom), new("Réception", x.Reception), new("Statut effectif", x.Status), new("Origine", x.Origin)])).ToArray());

    public sealed record JournalRow(string Id, string Number, string Date, string Code, string Student,
        string Promotion, string Amount, string Method, string Status, string Cashier);
    public sealed record ReceiptJournal(string Title, string Subtitle, string Search, string From, string To,
        string Method, string Status, string Promotion, int Page, int Count, bool HasPrevious, bool HasNext,
        IReadOnlyList<FilterChoice> Methods, IReadOnlyList<FilterChoice> Statuses,
        IReadOnlyList<FilterChoice> Promotions, IReadOnlyList<JournalRow> Rows);
    private ReceiptsPage? journalSelection;
    private object? journalOwner;
    private static string WireDate(DateTime? value) => value?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "";
    private static DateTime? ReadDate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var day)) throw new ArgumentException("Date invalide.");
        return day.ToDateTime(TimeOnly.MinValue);
    }
    private ReceiptJournal DescribeJournal(ReceiptsPage view, int requestedPage)
    {
        const int size = 25;
        var count = view.Items.Count;
        var page = Math.Clamp(requestedPage, 1, Math.Max(1, (count + size - 1) / size));
        return new(view.Title, view.Subtitle, view.Search, WireDate(view.From), WireDate(view.To),
            view.Method?.Value ?? "", view.Status?.Value ?? "", view.Promotion?.Value ?? "",
            page, count, page > 1, page * size < count,
            view.Methods.Select(x => new FilterChoice(x.Value, x.Label)).ToArray(),
            view.Statuses.Select(x => new FilterChoice(x.Value, x.Label)).ToArray(),
            view.Promotions.Select(x => new FilterChoice(x.Value, x.Label)).ToArray(),
            view.Items.Skip((page - 1) * size).Take(size).Select(x => new JournalRow(x.Id.ToString(),
                x.Number, x.Date, x.Matricule, x.Student, x.Promotion, x.Amount, x.Method, x.Status, x.Cashier)).ToArray());
    }

    public sealed record CashierPayer(string Id, string Label, string Detail);
    public sealed record CashierFee(string Id, string Label, string Amount, string Deadline, bool Selected, bool Overdue);
    public sealed record CashierAllocation(string Label, string Amount, string Remaining);
    public sealed record CashierScreen(string Student, string Info, string Balance, string Overdue, string Hint, string SearchStatus,
        string Amount, string Method, string Reference, string Note, string Total, string BalanceAfter, string Receipt,
        bool HasStudent, bool CanPay, bool NeedsReference, bool HasReceipt, IReadOnlyList<CashierPayer> Payers,
        IReadOnlyList<CashierFee> Fees, IReadOnlyList<FilterChoice> Methods, IReadOnlyList<CashierAllocation> Allocations);

    public sealed record EnrollmentField(string Key, string Label, string Kind, bool Required, string Value, string Hint, string Error, IReadOnlyList<FilterChoice> Options);
    public sealed record EnrollmentForm(string Token, string Title, string Subtitle, string Error, IReadOnlyList<EnrollmentField> Fields, string Purpose = "enrollment");

    public sealed record ProfileValue(string Label, string Value);
    public sealed record ProfileSection(string Title, IReadOnlyList<IReadOnlyList<ProfileValue>> Rows);
    public sealed record StudentProfile(string Id, string Title, string Subtitle, IReadOnlyList<ProfileSection> Sections);

    public sealed record WorkspaceHeader(string Greeting, string Date, string Initials, string SyncStatus, string SyncHint, bool Offline, bool Pending);

    public sealed record ModuleEntry(string Id, string Label, string Group);

    public sealed record StudentEntry(string Name, string Code, string Promotion, string Status, string Id);
    public sealed record StudentListing(string Title, string Subtitle, int Page, string Search,
        bool HasPrevious, bool HasNext, IReadOnlyList<StudentEntry> Rows);

    public sealed record FilterChoice(string Id, string Label);
    public sealed record PaymentEntry(string Name, string Promotion, string Expected, string Paid, string Remaining, string Status);
    public sealed record MonthlyListing(string Title, string Subtitle, string Period, string Promotion,
        string Status, string Search, int Page, int Count, bool HasPrevious, bool HasNext,
        IReadOnlyList<FilterChoice> Periods, IReadOnlyList<FilterChoice> Promotions,
        IReadOnlyList<FilterChoice> States, IReadOnlyList<PaymentEntry> Rows, string ExportScope);

    private CashierPage? ActiveCashier => AllowedPage("cashier") is not null && shell.Can(Perm.PaymentsCreate)
        ? shell.Current as CashierPage : null;

    private Snapshot DescribeCashier(CashierPage page) => Read() with { Stage = "cashier", Cashier = new(
        page.StudentName, page.StudentInfo, page.Balance, page.Overdue, page.Hint ?? page.LockedBy ?? "",
        page.SearchStatus ?? "", page.AmountText, page.Method?.Value ?? "", page.Reference, page.Note,
        page.Total, page.BalanceAfter, page.ReceiptText, page.HasStudent, page.CanPay, page.NeedsExternalReference,
        page.HasReceipt, page.Results.Select(x => new CashierPayer(x.StudentId.ToString(), x.Label, x.Detail)).ToArray(),
        page.Months.Select(x => new CashierFee(x.Id.ToString(), x.Label, x.AmountText, x.Deadline, x.IsSelected, x.Overdue)).ToArray(),
        page.Methods.Select(x => new FilterChoice(x.Value, x.Label)).ToArray(),
        page.Allocations.Select(x => new CashierAllocation(x.Label, x.Amount, x.Remaining)).ToArray()) };

    private MonthlyPage? monthlySelection;
    private object? monthlyOwner;
    private bool executing;
    private FormViewModel? enrollmentForm;
    private object? enrollmentOwner;
    private string? enrollmentToken;
    private string enrollmentPurpose = "enrollment";

    private void ClearEnrollment() { enrollmentForm = null; enrollmentOwner = null; enrollmentToken = null; enrollmentPurpose = "enrollment"; }

    private EnrollmentForm DescribeEnrollment(FormViewModel form) => new(enrollmentToken!, form.Title, form.Subtitle, form.Error ?? "",
        form.Fields.Select(field => new EnrollmentField(field.Key, field.Label, field.Kind.ToString(), field.Required,
            field.Kind switch
            {
                FieldKind.Choice => field.Choice?.Value ?? "",
                FieldKind.Flag => field.Flag ? "true" : "false",
                FieldKind.Day => field.Day?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? field.Text,
                _ => field.Text,
            }, field.Hint ?? "", field.Error ?? "", field.Options.Select(x => new FilterChoice(x.Value, x.Label)).ToArray())).ToArray(), enrollmentPurpose);
    private object? studentOwner;
    private HashSet<Guid> visibleStudents = [];

    private MonthlyListing DescribeMonthly(MonthlyPage view, int requestedPage)
    {
        const int size = 30;
        var count = view.Items.Count;
        var page = Math.Clamp(requestedPage, 1, Math.Max(1, (int)Math.Ceiling(count / (double)size)));
        return new(view.Title, view.Subtitle, view.Month!.Value, view.Promotion!.Value,
            view.State?.Value ?? "", view.Search, page, count, page > 1, page * size < count,
            view.Months.Select(x => new FilterChoice(x.Value, x.Label)).ToArray(),
            view.Promotions.Select(x => new FilterChoice(x.Value, x.Label)).ToArray(),
            view.StateChoices.Select(x => new FilterChoice(x.Value, x.Label)).ToArray(),
            view.Items.Skip((page - 1) * size).Take(size)
                .Select(x => new PaymentEntry(x.Name, x.Promotion, x.Amount, x.Paid, x.Remaining, x.Status)).ToArray(),
            view.ExportScope);
    }

    // Identifiants stables indépendants des libellés traduits et de la terminologie scolaire.
    private static readonly IReadOnlyDictionary<string, Type> Pages = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        ["students"] = typeof(StudentsPage),
        ["cashier"] = typeof(CashierPage),
        ["monthly"] = typeof(MonthlyPage),
        ["reports"] = typeof(ReportsPage),
        ["attendance"] = typeof(AttendancePage),
        ["grades"] = typeof(MarksPage),
        ["settings"] = typeof(SettingsPage),
        ["network"] = typeof(NetworkHubPage),
        ["receipts"] = typeof(ReceiptsPage),
        ["dashboard"] = typeof(DashboardPage),
        ["results"] = typeof(StudentResultsPage),
        ["lmd"] = typeof(LmdPlanningPage),
    };

    private bool MayShowWorkspace => shell.ShowApp && !shell.IsLocked && !shell.Opening &&
        !shell.SwitchingAccount && !shell.HasDialog && !shell.HasSheet &&
        shell.Session is { MustChangePassword: false };

    private NavItemVm? AllowedPage(string id)
    {
        if (!MayShowWorkspace) return null;
        if (Pages.TryGetValue(id, out var page))
            return shell.Navigation.SelectMany(g => g.Items).FirstOrDefault(i => i.PageType == page);
        return shell.Navigation.SelectMany(g => g.Items)
            .FirstOrDefault(i => string.Equals("page:" + i.PageType.Name, id, StringComparison.Ordinal));
    }

    private IReadOnlyList<ModuleEntry> Catalog()
    {
        var result = new List<ModuleEntry>();
        if (!MayShowWorkspace) return result;
        if (shell.Can(Perm.StudentsWrite) && AllowedPage("students") is not null)
            result.Add(new("enroll", "Inscrire", "Actions rapides"));
        foreach (var group in shell.Navigation)
            foreach (var item in group.Items)
            {
                var id = Pages.FirstOrDefault(x => x.Value == item.PageType).Key ?? "page:" + item.PageType.Name;
                if (result.Any(x => x.Id == id)) continue;
                result.Add(new(id, item.Label, group.Title.Length > 0 ? group.Title : "Espace de travail"));
            }
        return result;
    }

    private Snapshot Read(string? notice = null, bool native = false)
    {
        if (shell.ShowStartup || shell.Opening)
            return new(1, "loading", "", "", "", [], notice ?? "");
        if (shell.IsLocked || shell.SwitchingAccount || shell.HasDialog || shell.HasSheet ||
            shell.Session is { MustChangePassword: true })
            return new(1, "nativeScreen", "", "", "", [], "");
        if (native)
            return new(1, "nativeScreen", "", "", "", [], notice ?? "");
        if (MayShowWorkspace && shell.Session is { } session)
        {
            var catalog = Catalog();
            var header = new WorkspaceHeader($"{(DateTime.Now.Hour >= 18 ? "Bonsoir" : "Bonjour")}, {shell.AccountName}",
                Format.LongDate(DateOnly.FromDateTime(DateTime.Today)), shell.UserInitials,
                shell.Sync.StatusText, shell.Sync.StatusHint, shell.Sync.Offline, shell.Sync.HasPending);
            return new(1, "home", session.SchoolName, session.DisplayName, shell.SchoolYearLine,
                catalog.Select(x => x.Id).ToArray(), notice ?? "", Catalog: catalog, Header: header);
        }
        if (shell.ShowLogin)
            return new(1, "login", shell.Login.SchoolName, "", "", [], notice ?? "");
        if (shell.ShowConnection || shell.ShowSetup)
            return new(1, "pairing", "", "", "", [], notice ?? "");
        return new(1, "nativeScreen", "", "", "", [], "");
    }

    /// <summary>À appeler sur le dispatcher Avalonia ; l'hôte conserve la responsabilité du cycle de vie.</summary>
    public async Task<Snapshot> ExecuteAsync(string operation, IReadOnlyDictionary<string, string> arguments)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (executing) throw new InvalidOperationException("Une commande SCOLIA est déjà en cours.");
        if (operation != "listMonthly" && operation != "exportMonthly") { monthlySelection = null; monthlyOwner = null; }
        if (operation is not ("listStudents" or "openStudent" or "cashierStudent" or "studentActions")) { visibleStudents.Clear(); studentOwner = null; }
        if (operation is not ("listReceipts" or "exportReceipts" or "openReceipt"))
        { journalSelection = null; journalOwner = null; }
        if (operation is not ("listAttendance" or "editAttendance" or "saveAttendance" or "attendanceAll" or "attendanceDay" or "listDiscipline" or "disciplineExport" or "registerNative" or "listPersonnel" or "marksCheck" or "marksSave" or "academicExport" or "lmdFeeForm" or "settingsForm"))
            ClearRegister();
        if (operation is not ("submitEnrollment" or "lmdFeePreview" or "settingsSubmit" or "settingsPreview")) ClearEnrollment();
        if (operation == "snapshot") return ActiveCashier is { } active ? DescribeCashier(active) : Read();
        executing = true;
        try
        {
            switch (operation)
            {
                case "signIn" when shell.ShowLogin && !shell.IsLocked && !shell.SwitchingAccount:
                    if (shell.Login.RecoveryOpen) shell.Login.ToggleRecoveryHelp.Execute(null);
                    shell.Login.UserName = Required(arguments, "userName", 40);
                    shell.Login.Password = Required(arguments, "password", 256);
                    if (!shell.Login.CanSubmitCredentials) throw new InvalidOperationException("Connexion indisponible.");
                    await shell.Login.Submit.ExecuteAsync();
                    return Read(shell.Login.Error is null ? null : "Connexion refusée ou indisponible.");
                case "recover" when shell.ShowLogin && !shell.IsLocked && !shell.SwitchingAccount:
                    shell.Login.ShowRecoveryHelp(Required(arguments, "userName", 40));
                    shell.Login.RecoveryCode = Required(arguments, "recoveryCode", 256);
                    shell.Login.NewPassword = Required(arguments, "password", 256);
                    shell.Login.ConfirmPassword = Required(arguments, "confirmation", 256);
                    if (!shell.Login.CanRecoverWithCode) throw new InvalidOperationException("Récupération indisponible.");
                    await shell.Login.RecoverWithCode.ExecuteAsync();
                    return Read(shell.Login.Error is null ? null : "Récupération refusée ou indisponible.");
                case "biometric" when shell.ShowLogin && !shell.IsLocked && !shell.SwitchingAccount:
                    if (!shell.Login.CanUseBiometricSignIn) throw new InvalidOperationException("Empreinte non configurée.");
                    await shell.Login.SignInWithBiometrics.ExecuteAsync();
                    return Read(shell.Login.Error is null ? null : "Authentification indisponible.");
                case "scanQr" when !shell.IsLoggedIn && (shell.ShowConnection || shell.ShowSetup):
                    if (!shell.ShowConnection) await shell.ChooseServerAsync();
                    if (!shell.ShowConnection) return Read();
                    await shell.Connection.ScanCode.ExecuteAsync();
                    return Read(native: shell.ShowConnection);
                case "chooseServer" when !shell.IsLoggedIn && (shell.ShowSetup || shell.ShowConnection || shell.ShowLogin):
                    await shell.ChooseServerAsync();
                    return Read(native: true);
                case "createSchool" when shell.ShowSetup && !shell.IsLoggedIn:
                    return Read(native: true);
                case "listStudents":
                    visibleStudents.Clear();
                    studentOwner = null;
                    if (AllowedPage("students") is null || shell.Session is not { } owner)
                        throw new UnauthorizedAccessException("Registre non autorisé.");
                    var query = arguments.TryGetValue("q", out var term) ? term.Trim() : "";
                    if (query.Length > 100 || !int.TryParse(Required(arguments, "page", 6),
                        System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var page) ||
                        page is < 1 or > 100000)
                        throw new ArgumentException("Recherche ou page invalide.");
                    var students = new StudentsPage(shell);
                    await students.LoadForFlutterAsync(query, page);
                    if (AllowedPage("students") is null || !ReferenceEquals(owner, shell.Session))
                        throw new UnauthorizedAccessException("Session modifiée.");
                    visibleStudents = students.Items.Select(r => r.Id).ToHashSet();
                    studentOwner = owner;
                    return Read() with { Stage = "students", Students = new StudentListing(students.Title,
                        students.Subtitle, students.FlutterPage, query, students.CanPrevious, students.CanNext,
                        students.Items.Select(r => new StudentEntry(r.Name, r.Matricule, r.Promotion, r.Status, r.Id.ToString())).ToArray()) };
                case "listMonthly":
                    monthlySelection = null;
                    monthlyOwner = null;
                    if (AllowedPage("monthly") is null || shell.Session is not { } paymentOwner)
                        throw new UnauthorizedAccessException("Suivi non autorisé.");
                    var search = arguments.GetValueOrDefault("q", "").Trim();
                    if (search.Length > 100 || !int.TryParse(Required(arguments, "page", 6), out var paymentPage) ||
                        paymentPage is < 1 or > 100000) throw new ArgumentException("Recherche invalide.");
                    var paymentView = new MonthlyPage(shell);
                    await paymentView.LoadForFlutterAsync(arguments.GetValueOrDefault("period"),
                        arguments.GetValueOrDefault("promotion"), arguments.GetValueOrDefault("status"), search);
                    if (AllowedPage("monthly") is null || !ReferenceEquals(paymentOwner, shell.Session))
                        throw new UnauthorizedAccessException("Session modifiée.");
                    monthlySelection = paymentView;
                    monthlyOwner = paymentOwner;
                    return Read() with { Stage = "monthly", Monthly = DescribeMonthly(paymentView, paymentPage) };
                case "exportMonthly":
                    if (AllowedPage("monthly") is null || monthlySelection is not { } selected ||
                        !ReferenceEquals(monthlyOwner, shell.Session))
                        throw new UnauthorizedAccessException("Sélection non confirmée.");
                    var format = Required(arguments, "format", 4);
                    if (format is not ("pdf" or "xlsx" or "docx")) throw new ArgumentException("Format inconnu.");
                    // Utiliser la même instance qui a fourni les lignes affichées, avec l'empreinte du serveur.
                    try { await selected.ExportForFlutterAsync(format); }
                    catch { monthlySelection = null; monthlyOwner = null; throw; }
                    if (AllowedPage("monthly") is null || !ReferenceEquals(monthlyOwner, shell.Session))
                        throw new UnauthorizedAccessException("Session modifiée.");
                    var exportPage = int.TryParse(arguments.GetValueOrDefault("page"), out var ep) ? ep : 1;
                    return Read() with { Stage = "monthly", Monthly = DescribeMonthly(selected, exportPage) };
                case "openStudent":
                case "studentActions":
                case "cashierStudent":
                    if (AllowedPage("students") is null || !ReferenceEquals(studentOwner, shell.Session) ||
                        !Guid.TryParse(Required(arguments, "student", 36), out var studentId) ||
                        !visibleStudents.Contains(studentId))
                        throw new UnauthorizedAccessException("Dossier non autorisé.");
                    if (operation == "cashierStudent")
                    {
                        if (AllowedPage("cashier") is null || !shell.Can(Perm.PaymentsCreate))
                            throw new UnauthorizedAccessException("Encaissement non autorisé.");
                        await shell.Open(() => new CashierPage(shell, studentId));
                        return ActiveCashier is { } opened ? DescribeCashier(opened) : Read(native: true);
                    }
                    else if (operation == "studentActions")
                        await shell.Open(() => new StudentDetailPage(shell, studentId));
                    else
                    {
                        var profileOwner = shell.Session;
                        var detail = new StudentDetailPage(shell, studentId);
                        await detail.LoadAsync();
                        if (AllowedPage("students") is null || !ReferenceEquals(profileOwner, shell.Session))
                            throw new UnauthorizedAccessException("Session modifiée.");
                        var sections = new List<ProfileSection>();
                        static IReadOnlyList<ProfileValue> Row(params ProfileValue[] values) => values;
                        sections.Add(new("Identité", detail.Details.Select(x => Row(new ProfileValue(x.Label, x.Value))).ToArray()));
                        sections.Add(new("Inscriptions", detail.Enrollments.Select(x => Row(
                            new("Année", x.Year), new("Promotion", x.Promotion), new("Inscrit le", x.EnrolledOn),
                            new("Statut", x.Status), new("Sortie", x.Exit))).ToArray()));
                        if (shell.Can(Perm.PaymentsRead))
                        {
                            sections.Insert(0, new("Indicateurs du compte", detail.Totals.Select(x => Row(
                                new(x.Label, x.Value), new("Détail", x.Hint))).ToArray()));
                            sections.Add(new("Compte de l’année", detail.Lines.Select(x => Row(
                                new("Échéance", x.Label), new("Date", x.DueDate), new("Montant", x.Amount),
                                new("Pris en charge", x.Covered), new("Payé", x.Paid), new("Reste", x.Remaining),
                                new("Statut", x.Status))).ToArray()));
                            sections.Add(new("Reçus", detail.Receipts.Select(x => Row(
                                new("Numéro", x.Number), new("Date", x.Date), new("Montant", x.Amount), new("Mode", x.Method))).ToArray()));
                            sections.Add(new("Prises en charge", detail.Sponsorships.Select(x => Row(
                                new("Garant ou organisme", x.Sponsor), new("Type", x.Type), new("Motif", x.Reason),
                                new("Effet", x.Since), new("Couvert", x.Covered), new("État", x.Status))).ToArray()));
                        }
                        return Read() with { Stage = "studentProfile",
                            Profile = new(studentId.ToString(), detail.Title, detail.Subtitle, sections) };
                    }
                    return Read(native: true);
                case "marksCheck":
                case "marksSave":
                {
                    if (marksDraft is not { } marks || !shell.TeacherOnly || AllowedPage("grades") is null ||
                        !ReferenceEquals(registerOwner, shell.Session) || Required(arguments, "token", 36) != registerToken)
                        throw new UnauthorizedAccessException("Feuille non confirmée.");
                    var code = Required(arguments, "code", 64);
                    if (operation == "marksCheck") await marks.CheckForFlutterAsync(code);
                    else await marks.SaveForFlutterAsync(code, Required(arguments, "score", 32));
                    if (!shell.TeacherOnly || AllowedPage("grades") is null || !ReferenceEquals(registerOwner, shell.Session))
                    { ClearRegister(); throw new UnauthorizedAccessException("Session modifiée."); }
                    return Read() with { Stage = "schoolRegister", Register = DescribeMarks(marks) };
                }
                case "academicExport":
                {
                    if (shell.TeacherOnly || !MayShowWorkspace || !ReferenceEquals(registerOwner, shell.Session) ||
                        Required(arguments, "token", 36) != registerToken)
                        throw new UnauthorizedAccessException("Sélection non confirmée.");
                    var document = Required(arguments, "document", 20);
                    if (deliberationDraft is { } board && AllowedPage("page:DeliberationPage") is not null)
                    {
                        Guid? rowId = document == "bulletin" ? Guid.Parse(Required(arguments, "row", 36)) : null;
                        await board.ExportForFlutterAsync(document, rowId);
                        if (!ReferenceEquals(registerOwner, shell.Session) || AllowedPage("page:DeliberationPage") is null)
                        { ClearRegister(); throw new UnauthorizedAccessException("Session modifiée."); }
                        return Read() with { Stage = "schoolRegister", Register = DescribeDeliberation(board) };
                    }
                    if (progressDraft is { } progress && AllowedPage("page:EncodingProgressPage") is not null)
                    {
                        await progress.ExportForFlutterAsync(document);
                        if (!ReferenceEquals(registerOwner, shell.Session) || AllowedPage("page:EncodingProgressPage") is null)
                        { ClearRegister(); throw new UnauthorizedAccessException("Session modifiée."); }
                        return Read() with { Stage = "schoolRegister", Register = DescribeProgress(progress) };
                    }
                    throw new UnauthorizedAccessException("Export non autorisé.");
                }
                case "settingsForm":
                {
                    if (settingsDraft is not { } settings || AllowedPage("settings") is null || !shell.Can(Perm.SettingsWrite) ||
                        !ReferenceEquals(registerOwner, shell.Session) || Required(arguments, "token", 36) != registerToken)
                        throw new UnauthorizedAccessException("Configuration non confirmée.");
                    var purpose = settings.IsSchool ? "school" : settings.IsPromotions ? "promotion"
                        : throw new ArgumentException("Rubrique invalide.");
                    var row = arguments.GetValueOrDefault("row", "");
                    FormViewModel? settingsForm;
                    if (purpose == "school")
                    {
                        if (row.Length != 0) throw new ArgumentException("Sélection invalide.");
                        settingsForm = settings.SchoolForm;
                    }
                    else
                    {
                        var source = string.IsNullOrEmpty(row) ? null : settings.Promotions
                            .FirstOrDefault(x => x.Id == Guid.Parse(row))?.Source ?? throw new UnauthorizedAccessException("Promotion non proposée.");
                        settingsForm = await SettingsPage.CreatePromotionFormAsync(shell, source);
                    }
                    if (!ReferenceEquals(registerOwner, shell.Session) || AllowedPage("settings") is null || !shell.Can(Perm.SettingsWrite))
                        throw new UnauthorizedAccessException("Session modifiée.");
                    if (settingsForm is null || settingsForm.Fields.Count(x => x.Kind != FieldKind.Header) > 95)
                    {
                        var native = new SettingsPage(shell, settings.Section?.Value);
                        await shell.Open(() => native);
                        if (Guid.TryParse(row, out var id)) native.SelectedPromotion = native.Promotions.FirstOrDefault(x => x.Id == id);
                        ClearRegister();
                        return Read(native: true);
                    }
                    enrollmentForm = settingsForm;
                    enrollmentOwner = shell.Session;
                    enrollmentToken = Guid.NewGuid().ToString();
                    enrollmentPurpose = purpose;
                    ClearRegister();
                    return Read() with { Stage = "enrollment", Enrollment = DescribeEnrollment(settingsForm) };
                }
                case "lmdFeeForm":
                {
                    if (lmdDraft is not { } lmd || AllowedPage("lmd") is null || !shell.Can(Perm.SettingsWrite) ||
                        !ReferenceEquals(registerOwner, shell.Session) || Required(arguments, "token", 36) != registerToken)
                        throw new UnauthorizedAccessException("Promotion non confirmée.");
                    var feeForm = await lmd.CreateInstallmentFormAsync();
                    if (!ReferenceEquals(registerOwner, shell.Session) || AllowedPage("lmd") is null || !shell.Can(Perm.SettingsWrite))
                        throw new UnauthorizedAccessException("Session modifiée.");
                    enrollmentForm = feeForm;
                    enrollmentOwner = shell.Session;
                    enrollmentToken = Guid.NewGuid().ToString();
                    enrollmentPurpose = "lmdFee";
                    ClearRegister();
                    return Read() with { Stage = "enrollment", Enrollment = DescribeEnrollment(feeForm) };
                }
                case "listPersonnel":
                {
                    ClearRegister();
                    var kind = Required(arguments, "kind", 20);
                    var moduleId = kind switch { "staff" => "page:StaffPage", "pointage" => "page:StaffAttendancePage",
                        "teaching" => "page:TeachingPage", "codes" => "page:AnonymousCodesPage", "marks" => "grades", "deliberation" => "page:DeliberationPage",
                        "progress" => "page:EncodingProgressPage", "lmd" => "lmd", "settings" => "settings", "users" => "page:UsersPage", _ => throw new ArgumentException("Module invalide.") };
                    if (AllowedPage(moduleId) is null || ((kind is "codes" or "deliberation" or "progress") && shell.TeacherOnly) || shell.Session is not { } personnelOwner)
                        throw new UnauthorizedAccessException("Module non autorisé.");
                    if (kind == "marks")
                    {
                        // Les autres profils conservent la feuille nominative native.
                        if (!shell.TeacherOnly)
                        {
                            await AllowedPage(moduleId)!.Open.ExecuteAsync();
                            return Read(native: true);
                        }
                        var p = new MarksPage(shell);
                        await p.LoadTeacherForFlutterAsync(arguments.GetValueOrDefault("promotion"),
                            arguments.GetValueOrDefault("date"), arguments.GetValueOrDefault("measure"));
                        if (!shell.TeacherOnly || AllowedPage(moduleId) is null || !ReferenceEquals(personnelOwner, shell.Session))
                        { ClearRegister(); throw new UnauthorizedAccessException("Session modifiée."); }
                        marksDraft = p; registerOwner = personnelOwner; registerToken = Guid.NewGuid().ToString();
                        return Read() with { Stage = "schoolRegister", Register = DescribeMarks(p) };
                    }
                    var summary = arguments.GetValueOrDefault("summary") == "true";
                    if (kind == "settings")
                    {
                        var requested = arguments.GetValueOrDefault("date", "");
                        var catalog = new SettingsPage(shell);
                        if (!string.IsNullOrEmpty(requested) && !catalog.AllSections.Any(x => x.Value == requested))
                            throw new UnauthorizedAccessException("Rubrique non autorisée.");
                        var p = string.IsNullOrEmpty(requested) ? catalog : new SettingsPage(shell, requested);
                        // Les rubriques de poste, de licence et de sécurité gardent leurs réglages natifs.
                        if (p.IsPoste || p.IsLicense)
                        {
                            await shell.Open(() => p);
                            return Read(native: true);
                        }
                        await p.LoadAsync();
                        settingsDraft = p;
                    }
                    else if (kind == "users")
                    {
                        var p = new UsersPage(shell);
                        await p.LoadAsync();
                        usersDraft = p;
                    }
                    else if (kind == "lmd")
                    {
                        var p = new LmdPlanningPage(shell);
                        await p.LoadForFlutterAsync(arguments.GetValueOrDefault("promotion"));
                        lmdDraft = p;
                    }
                    else if (kind == "deliberation")
                    {
                        var p = new DeliberationPage(shell);
                        await p.LoadForFlutterAsync(arguments.GetValueOrDefault("promotion"), arguments.GetValueOrDefault("date"), summary);
                        deliberationDraft = p;
                    }
                    else if (kind == "progress")
                    {
                        var p = new EncodingProgressPage(shell);
                        await p.LoadForFlutterAsync(arguments.GetValueOrDefault("promotion"), arguments.GetValueOrDefault("date"), summary);
                        progressDraft = p;
                    }
                    else if (kind == "staff")
                    {
                        var p = new StaffPage(shell);
                        await p.LoadForFlutterAsync(summary, arguments.GetValueOrDefault("date"));
                        staffDraft = p;
                    }
                    else if (kind == "pointage")
                    {
                        var p = new StaffAttendancePage(shell);
                        await p.LoadForFlutterAsync(summary);
                        pointageDraft = p;
                    }
                    else if (kind == "codes")
                    {
                        var p = new AnonymousCodesPage(shell);
                        await p.LoadForFlutterAsync(arguments.GetValueOrDefault("promotion"));
                        codesDraft = p;
                    }
                    else
                    {
                        var p = new TeachingPage(shell);
                        await p.LoadForFlutterAsync(arguments.GetValueOrDefault("promotion"), summary);
                        teachingDraft = p;
                    }
                    if (AllowedPage(moduleId) is null || ((kind is "codes" or "deliberation" or "progress") && shell.TeacherOnly) || !ReferenceEquals(personnelOwner, shell.Session))
                    { ClearRegister(); throw new UnauthorizedAccessException("Session modifiée."); }
                    registerOwner = personnelOwner; registerToken = Guid.NewGuid().ToString();
                    return Read() with { Stage = "schoolRegister", Register = settingsDraft is { } settings ? DescribeSettings(settings)
                        : usersDraft is { } users ? DescribeUsers(users) : lmdDraft is { } lmd ? DescribeLmd(lmd) : deliberationDraft is { } board ? DescribeDeliberation(board)
                        : progressDraft is { } progress ? DescribeProgress(progress) : staffDraft is { } personnel ? DescribeStaff(personnel)
                        : pointageDraft is { } pointage ? DescribePointage(pointage) : codesDraft is { } codes ? DescribeCodes(codes) : DescribeTeaching(teachingDraft!) };
                }
                case "listAttendance":
                case "listDiscipline":
                {
                    ClearRegister();
                    var isAttendance = operation == "listAttendance";
                    var id = isAttendance ? "attendance" : "page:DisciplinePage";
                    if (AllowedPage(id) is null || shell.Session is not { } schoolOwner)
                        throw new UnauthorizedAccessException("Registre non autorisé.");
                    if (isAttendance)
                    {
                        var view = new AttendancePage(shell);
                        await view.LoadForFlutterAsync(arguments.GetValueOrDefault("promotion"),
                            ReadDate(arguments.GetValueOrDefault("date")) ?? DateTime.Today,
                            arguments.GetValueOrDefault("summary") == "true");
                        attendanceDraft = view;
                    }
                    else
                    {
                        var view = new DisciplinePage(shell);
                        await view.LoadForFlutterAsync(arguments.GetValueOrDefault("promotion"), arguments.GetValueOrDefault("measure"));
                        disciplineDraft = view;
                    }
                    if (AllowedPage(id) is null || !ReferenceEquals(schoolOwner, shell.Session))
                    { ClearRegister(); throw new UnauthorizedAccessException("Session modifiée."); }
                    registerOwner = schoolOwner;
                    registerToken = Guid.NewGuid().ToString();
                    return Read() with { Stage = "schoolRegister", Register = isAttendance
                        ? DescribeAttendance(attendanceDraft!) : DescribeDiscipline(disciplineDraft!) };
                }
                case "editAttendance":
                case "saveAttendance":
                case "attendanceAll":
                case "attendanceDay":
                case "disciplineExport":
                case "registerNative":
                {
                    if (!ReferenceEquals(registerOwner, shell.Session) || !MayShowWorkspace ||
                        Required(arguments, "token", 36) != registerToken)
                        throw new UnauthorizedAccessException("Registre non confirmé.");
                    if (operation == "registerNative" && settingsDraft is { } settings && AllowedPage("settings") is not null)
                    {
                        if (!shell.Can(Perm.SettingsWrite)) throw new UnauthorizedAccessException("Configuration non autorisée.");
                        var row = arguments.GetValueOrDefault("row", "");
                        Guid? selectedId = string.IsNullOrEmpty(row) ? null : Guid.Parse(row);
                        if (selectedId is { } sid && !(settings.IsYears && settings.Years.Any(x => x.Id == sid)
                            || settings.IsSections && settings.SectionRows.Any(x => x.Id == sid)
                            || settings.IsPromotions && settings.Promotions.Any(x => x.Id == sid)
                            || settings.IsFees && settings.Categories.Any(x => x.Id == sid)))
                            throw new UnauthorizedAccessException("Élément non proposé.");
                        // Une ouverture ciblée active le détail sur Android.
                        var native = new SettingsPage(shell, settings.Section?.Value);
                        await shell.Open(() => native);
                        if (selectedId is { } id)
                        {
                            if (native.IsYears) native.SelectedYear = native.Years.FirstOrDefault(x => x.Id == id);
                            if (native.IsSections) native.SelectedSection = native.SectionRows.FirstOrDefault(x => x.Id == id);
                            if (native.IsPromotions) native.SelectedPromotion = native.Promotions.FirstOrDefault(x => x.Id == id);
                            if (native.IsFees) native.SelectedCategory = native.Categories.FirstOrDefault(x => x.Id == id);
                        }
                        ClearRegister();
                        return Read(native: true);
                    }
                    if (operation == "registerNative" && usersDraft is { } users && AllowedPage("page:UsersPage") is not null)
                    {
                        var row = arguments.GetValueOrDefault("row", "");
                        Guid? selectedId = string.IsNullOrEmpty(row) ? null : Guid.Parse(row);
                        if (selectedId is { } sid && !users.Items.Any(x => x.Id == sid))
                            throw new UnauthorizedAccessException("Compte non proposé.");
                        await shell.Open(() => users);
                        users.Selected = users.Items.FirstOrDefault(x => x.Id == selectedId);
                        ClearRegister();
                        return Read(native: true);
                    }
                    if (operation == "registerNative" && lmdDraft is { } lmd && AllowedPage("lmd") is not null)
                    {
                        await shell.Open(() => lmd);
                        ClearRegister();
                        return Read(native: true);
                    }
                    if (operation == "registerNative" && marksDraft is { } marks && shell.TeacherOnly && AllowedPage("grades") is not null)
                    {
                        await shell.Open(() => new MarksPage(shell,
                            marks.Promotion is { } promotion ? Guid.Parse(promotion.Value) : null, marks.Course?.Id));
                        ClearRegister();
                        return Read(native: true);
                    }
                    if (operation == "registerNative" && deliberationDraft is { } board && !shell.TeacherOnly &&
                        AllowedPage("page:DeliberationPage") is not null)
                    {
                        var row = arguments.GetValueOrDefault("row", "");
                        Guid? selectedId = string.IsNullOrEmpty(row) ? null : Guid.Parse(row);
                        if (selectedId is { } sid && !board.Items.Any(x => x.EnrollmentId == sid))
                            throw new UnauthorizedAccessException("Dossier non proposé.");
                        await shell.Open(() => board);
                        board.Selected = board.Items.FirstOrDefault(x => x.EnrollmentId == selectedId);
                        ClearRegister();
                        return Read(native: true);
                    }
                    if (operation == "registerNative" && (staffDraft is not null || pointageDraft is not null || teachingDraft is not null || codesDraft is not null))
                    {
                        var row = arguments.GetValueOrDefault("row", "");
                        Guid? selectedId = string.IsNullOrEmpty(row) ? null : Guid.Parse(row);
                        if (staffDraft is { } staff && AllowedPage("page:StaffPage") is not null)
                        {
                            if (selectedId is { } sid && !(staff.ShowPayroll ? staff.Payroll.Any(x => x.Id == sid) : staff.Staff.Any(x => x.Id == sid)))
                                throw new UnauthorizedAccessException("Dossier non proposé.");
                            await shell.Open(() => staff);
                            if (staff.ShowPayroll) staff.SelectedPayroll = staff.Payroll.FirstOrDefault(x => x.Id == selectedId);
                            else staff.SelectedStaff = staff.Staff.FirstOrDefault(x => x.Id == selectedId);
                        }
                        else if (pointageDraft is { } pointage && AllowedPage("page:StaffAttendancePage") is not null)
                        {
                            if (selectedId is { } sid && !pointage.Members.Any(x => x.Id == sid))
                                throw new UnauthorizedAccessException("Membre non proposé.");
                            await shell.Open(() => pointage);
                            pointage.Selected = pointage.Members.FirstOrDefault(x => x.Id == selectedId);
                        }
                        else if (codesDraft is { } codes && !shell.TeacherOnly && AllowedPage("page:AnonymousCodesPage") is not null)
                        {
                            if (selectedId is { } sid && !codes.Items.Any(x => x.EnrollmentId == sid))
                                throw new UnauthorizedAccessException("Dossier non proposé.");
                            await shell.Open(() => codes);
                            codes.Selected = codes.Items.FirstOrDefault(x => x.EnrollmentId == selectedId);
                        }
                        else if (teachingDraft is { } teaching && AllowedPage("page:TeachingPage") is not null)
                        {
                            if (selectedId is { } sid && !teaching.Courses.Any(x => x.Id == sid))
                                throw new UnauthorizedAccessException("Cours non proposé.");
                            await shell.Open(() => teaching);
                            teaching.Selected = teaching.Courses.FirstOrDefault(x => x.Id == selectedId);
                        }
                        else throw new UnauthorizedAccessException("Module non autorisé.");
                        ClearRegister();
                        return Read(native: true);
                    }
                    if (attendanceDraft is { } attendance && AllowedPage("attendance") is not null)
                    {
                        if (operation == "registerNative")
                        {
                            await shell.Open(() => attendance);
                            ClearRegister();
                            return Read(native: true);
                        }
                        if (!shell.Can(Perm.AcademicsWrite) || attendance.ShowSummary || attendance.Promotion is null)
                            throw new UnauthorizedAccessException("Modification non autorisée.");
                        var notice = "";
                        if (operation == "editAttendance")
                        {
                            if (!Guid.TryParse(Required(arguments, "row", 36), out var rowId))
                                throw new ArgumentException("Ligne invalide.");
                            var row = attendance.Rows.FirstOrDefault(r => r.EnrollmentId == rowId)
                                ?? throw new UnauthorizedAccessException("Ligne non proposée.");
                            var state = row.Choices.FirstOrDefault(x => x.Value == Required(arguments, "status", 40))
                                ?? throw new ArgumentException("Statut invalide.");
                            var note = arguments.GetValueOrDefault("note", "");
                            if (note.Length > 500) throw new ArgumentException("Remarque trop longue.");
                            row.Status = state; row.Note = note; registerDirty = true;
                        }
                        else if (operation == "attendanceAll")
                        {
                            foreach (var row in attendance.Rows) row.Status = attendance.Statuses[0];
                            registerDirty = true;
                        }
                        else if (operation == "saveAttendance")
                        {
                            if (!registerDirty && !attendance.CallPending) throw new InvalidOperationException("Aucun changement.");
                            notice = await attendance.SaveForFlutterAsync();
                            registerDirty = false;
                            await attendance.LoadAsync();
                        }
                        else if (operation == "attendanceDay")
                        {
                            if (registerDirty) throw new InvalidOperationException("Enregistrez d'abord l'appel.");
                            await attendance.ConfirmForFlutterAsync(Required(arguments, "confirm", 5) == "true");
                        }
                        else throw new ArgumentException("Commande invalide.");
                        if (AllowedPage("attendance") is null || !ReferenceEquals(registerOwner, shell.Session))
                        { ClearRegister(); throw new UnauthorizedAccessException("Session modifiée."); }
                        return Read(notice) with { Stage = "schoolRegister", Register = DescribeAttendance(attendance) };
                    }
                    if (disciplineDraft is { } discipline && AllowedPage("page:DisciplinePage") is not null)
                    {
                        if (operation == "registerNative")
                        {
                            await shell.Open(() => discipline);
                            ClearRegister();
                            return Read(native: true);
                        }
                        if (operation != "disciplineExport") throw new ArgumentException("Commande invalide.");
                        await discipline.ExportForFlutterAsync();
                        if (!ReferenceEquals(registerOwner, shell.Session) || AllowedPage("page:DisciplinePage") is null)
                            throw new UnauthorizedAccessException("Session modifiée.");
                        return Read() with { Stage = "schoolRegister", Register = DescribeDiscipline(discipline) };
                    }
                    throw new UnauthorizedAccessException("Registre indisponible.");
                }
                case "listReceipts":
                {
                    journalSelection = null; journalOwner = null;
                    if (AllowedPage("receipts") is null || shell.Session is not { } ownerReceipt)
                        throw new UnauthorizedAccessException("Journal non autorisé.");
                    var queryReceipt = arguments.GetValueOrDefault("q", "").Trim();
                    if (queryReceipt.Length > 100 || !int.TryParse(arguments.GetValueOrDefault("page", "1"), out var receiptPage)
                        || receiptPage is < 1 or > 100000) throw new ArgumentException("Recherche invalide.");
                    var start = ReadDate(arguments.GetValueOrDefault("from"));
                    var end = ReadDate(arguments.GetValueOrDefault("to"));
                    if (start > end) throw new ArgumentException("Période invalide.");
                    var view = new ReceiptsPage(shell);
                    await view.LoadForFlutterAsync(queryReceipt, start, end, arguments.GetValueOrDefault("method"),
                        arguments.GetValueOrDefault("status"), arguments.GetValueOrDefault("promotion"));
                    if (AllowedPage("receipts") is null || !ReferenceEquals(ownerReceipt, shell.Session))
                        throw new UnauthorizedAccessException("Session modifiée.");
                    journalSelection = view; journalOwner = ownerReceipt;
                    return Read() with { Stage = "receipts", Journal = DescribeJournal(view, receiptPage) };
                }
                case "exportReceipts":
                case "openReceipt":
                {
                    if (AllowedPage("receipts") is null || journalSelection is not { } journal ||
                        !ReferenceEquals(journalOwner, shell.Session)) throw new UnauthorizedAccessException("Journal non autorisé.");
                    if (operation == "openReceipt")
                    {
                        if (!Guid.TryParse(Required(arguments, "receipt", 36), out var receiptId) ||
                            journal.Items.All(x => x.Id != receiptId)) throw new UnauthorizedAccessException("Reçu non proposé.");
                        await shell.Open(() => new ReceiptDetailPage(shell, receiptId));
                        journalSelection = null; journalOwner = null;
                        return Read(native: true);
                    }
                    var receiptFormat = Required(arguments, "format", 4);
                    if (receiptFormat is not ("pdf" or "xlsx")) throw new ArgumentException("Format invalide.");
                    await journal.ExportForFlutterAsync(receiptFormat);
                    if (AllowedPage("receipts") is null || !ReferenceEquals(journalOwner, shell.Session))
                        throw new UnauthorizedAccessException("Session modifiée.");
                    return Read() with { Stage = "receipts", Journal = DescribeJournal(journal,
                        int.TryParse(arguments.GetValueOrDefault("page"), out var journalPage) ? journalPage : 1) };
                }
                case "cashierSearch":
                case "cashierChoose":
                case "cashierPreview":
                case "cashierConfirm":
                case "cashierLeave":
                    if (ActiveCashier is not { } cashier || shell.Session is not { } cashierOwner)
                        throw new UnauthorizedAccessException("Caisse non autorisée.");
                    if (operation == "cashierSearch")
                    {
                        // A new search opens a new native page, releasing the previous page's lock.
                        var payerQuery = Required(arguments, "q", 100);
                        await shell.Open(() => new CashierPage(shell));
                        cashier = ActiveCashier ?? throw new UnauthorizedAccessException("Caisse indisponible.");
                        await cashier.SearchForFlutterAsync(payerQuery);
                    }
                    else if (operation == "cashierChoose")
                    {
                        if (!Guid.TryParse(Required(arguments, "student", 36), out var payerId))
                            throw new ArgumentException("Dossier invalide.");
                        var payer = cashier.Results.FirstOrDefault(x => x.StudentId == payerId)
                            ?? throw new UnauthorizedAccessException("Dossier non proposé.");
                        await cashier.ChooseAsync(payer);
                    }
                    else if (operation == "cashierPreview")
                    {
                        var ids = new HashSet<Guid>();
                        foreach (var value in arguments.GetValueOrDefault("fees", "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (ids.Count >= 100 || !Guid.TryParse(value, out var feeId) || !ids.Add(feeId))
                                throw new ArgumentException("Échéances invalides.");
                        }
                        await cashier.PrepareForFlutterAsync(ids, arguments.GetValueOrDefault("amount", ""),
                            Required(arguments, "method", 60), arguments.GetValueOrDefault("reference", ""),
                            arguments.GetValueOrDefault("note", ""));
                    }
                    else if (operation == "cashierConfirm")
                    {
                        if (!cashier.CanPay || cashier.HasReceipt)
                            throw new InvalidOperationException("Aperçu non valide.");
                        // Le dialogue natif garde exactement la confirmation, l'idempotence et le reçu.
                        _ = shell.Run(cashier.PayAsync);
                        return Read(native: true);
                    }
                    else
                    {
                        // Retour explicite au Shell : Left() libère la réservation du dossier.
                        await shell.Open(() => new HomePage(shell));
                        return Read();
                    }
                    if (!ReferenceEquals(cashierOwner, shell.Session) || !ReferenceEquals(cashier, ActiveCashier))
                        throw new UnauthorizedAccessException("Session modifiée.");
                    return DescribeCashier(cashier);
                case "settingsPreview":
                case "settingsSubmit":
                case "lmdFeePreview":
                case "submitEnrollment":
                    var lmdFee = enrollmentPurpose == "lmdFee";
                    var settingsEdit = enrollmentPurpose is "school" or "promotion";
                    bool FormAllowed() => settingsEdit ? AllowedPage("settings") is not null && shell.Can(Perm.SettingsWrite)
                        : lmdFee ? AllowedPage("lmd") is not null && shell.Can(Perm.SettingsWrite)
                        : AllowedPage("students") is not null && shell.Can(Perm.StudentsWrite);
                    if (!FormAllowed() ||
                        (operation == "lmdFeePreview" && !lmdFee) ||
                        ((operation is "settingsPreview" or "settingsSubmit") != settingsEdit) ||
                        enrollmentForm is not { } form || !ReferenceEquals(enrollmentOwner, shell.Session) ||
                        !string.Equals(enrollmentToken, Required(arguments, "token", 36), StringComparison.Ordinal))
                        throw new UnauthorizedAccessException("Inscription non autorisée.");
                    var fields = form.Fields.Where(x => x.Kind != FieldKind.Header).ToArray();
                    if (arguments.Count != fields.Length + 1) throw new ArgumentException("Formulaire incomplet.");
                    foreach (var field in fields)
                    {
                        if (!arguments.TryGetValue(field.Key, out var value) || value.Length > 500)
                            throw new ArgumentException("Champ invalide.");
                        if (field.Kind == FieldKind.Choice)
                        {
                            field.Choice = field.Options.FirstOrDefault(x => x.Value == value);
                            if (value.Length > 0 && field.Choice is null) throw new ArgumentException("Choix invalide.");
                        }
                        else if (field.Kind == FieldKind.Flag)
                        {
                            if (value is not ("true" or "false")) throw new ArgumentException("Valeur invalide.");
                            field.Flag = value == "true";
                        }
                        else if (field.Kind == FieldKind.Day)
                        {
                            field.Text = value;
                            field.Day = DateOnly.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.None, out var date) ? date.ToDateTime(TimeOnly.MinValue) : null;
                        }
                        else field.Text = value;
                    }
                    if (operation is "lmdFeePreview" or "settingsPreview")
                        return Read() with { Stage = "enrollment", Enrollment = DescribeEnrollment(form) };
                    await form.Submit.ExecuteAsync();
                    if (!FormAllowed() || !ReferenceEquals(enrollmentOwner, shell.Session))
                    {
                        ClearEnrollment();
                        throw new UnauthorizedAccessException("Session modifiée.");
                    }
                    if (form.Completion.IsCompletedSuccessfully)
                    {
                        var accepted = form.Completion.Result;
                        var notice = settingsEdit ? (accepted ? "Configuration enregistrée." : "Configuration non confirmée. Vérifiez la synchronisation.")
                            : lmdFee ? (accepted ? "Configuration des frais confirmée." : "Modification des frais non confirmée.")
                            : accepted ? form.Message : "Inscription non confirmée. Vérifiez le registre avant de réessayer.";
                        ClearEnrollment();
                        return Read(notice);
                    }
                    return Read() with { Stage = "enrollment", Enrollment = DescribeEnrollment(form) };
                case "openModule":
                    var module = Required(arguments, "module", 80);
                    if (module == "enroll")
                    {
                        if (AllowedPage("students") is null || !shell.Can(Perm.StudentsWrite))
                            throw new UnauthorizedAccessException("Inscription non autorisée.");
                        var ownerBefore = shell.Session;
                        var enrollment = await StudentForms.CreateFlutterEnrollmentAsync(shell);
                        if (AllowedPage("students") is null || !shell.Can(Perm.StudentsWrite) ||
                            !ReferenceEquals(ownerBefore, shell.Session))
                            throw new UnauthorizedAccessException("Session modifiée.");
                        if (enrollment is null)
                        {
                            // Conserver l'assistant natif de création des promotions si nécessaire.
                            _ = shell.Run(() => StudentForms.CreateAsync(shell));
                            return Read(native: true);
                        }
                        enrollmentForm = enrollment;
                        enrollmentOwner = ownerBefore;
                        enrollmentToken = Guid.NewGuid().ToString();
                        return Read() with { Stage = "enrollment", Enrollment = DescribeEnrollment(enrollment) };
                    }
                    var item = AllowedPage(module) ??
                        throw new UnauthorizedAccessException("Fonctionnalité non autorisée.");
                    await item.Open.ExecuteAsync();
                    return module == "cashier" && ActiveCashier is { } cashierPage ? DescribeCashier(cashierPage) : Read(native: true);
                case "signOut" when MayShowWorkspace:
                    await shell.LogoutAsync();
                    return Read();
                default:
                    throw new UnauthorizedAccessException("Commande non autorisée dans cet état.");
            }
        }
        finally
        {
            if (operation is "signIn" or "recover" or "biometric")
            {
                shell.Login.Password = "";
                shell.Login.RecoveryCode = "";
                shell.Login.NewPassword = "";
                shell.Login.ConfirmPassword = "";
            }
            executing = false;
        }
    }

    private static string Required(IReadOnlyDictionary<string, string> arguments, string key, int max)
    {
        if (!arguments.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value) || value.Length > max)
            throw new ArgumentException("Champ requis ou invalide.");
        return value;
    }
}
