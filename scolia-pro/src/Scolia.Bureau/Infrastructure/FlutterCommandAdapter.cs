using Avalonia.Threading;
using Scolia.Bureau.ViewModels;
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
        string Year, IReadOnlyList<string> Modules, string Notice, StudentListing? Students = null, MonthlyListing? Monthly = null, IReadOnlyList<ModuleEntry>? Catalog = null);

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

    private MonthlyPage? monthlySelection;
    private object? monthlyOwner;
    private bool executing;
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
            return new(1, "home", session.SchoolName, session.DisplayName, shell.SchoolYearLine,
                Catalog().Select(x => x.Id).ToArray(), notice ?? "", Catalog: Catalog());
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
        if (operation is not ("listStudents" or "openStudent" or "cashierStudent")) { visibleStudents.Clear(); studentOwner = null; }
        if (operation == "snapshot") return Read();
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
                    }
                    else await shell.Open(() => new StudentDetailPage(shell, studentId));
                    return Read(native: true);
                case "openModule":
                    var module = Required(arguments, "module", 80);
                    if (module == "enroll")
                    {
                        if (AllowedPage("students") is null || !shell.Can(Perm.StudentsWrite))
                            throw new UnauthorizedAccessException("Inscription non autorisée.");
                        // Le formulaire natif conserve ses contrôles de doublons et la file hors ligne.
                        _ = shell.Run(() => StudentForms.CreateAsync(shell));
                        return Read(native: true);
                    }
                    var item = AllowedPage(module) ??
                        throw new UnauthorizedAccessException("Fonctionnalité non autorisée.");
                    await item.Open.ExecuteAsync();
                    return Read(native: true);
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
