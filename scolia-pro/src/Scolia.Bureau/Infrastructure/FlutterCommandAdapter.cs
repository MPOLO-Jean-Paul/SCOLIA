using Avalonia.Threading;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Adaptateur du contrat Flutter v1. Aucune ouverture HTTP, ni nouveau stockage de secrets.
/// L'hôte Android devra enregistrer le MethodChannel et afficher l'interface native lorsque
/// Stage vaut nativeScreen (verrouillage, dialogue ou écran métier non migré).
/// </summary>
public sealed class FlutterCommandAdapter(ShellViewModel shell)
{
    public sealed record Snapshot(int Protocol, string Stage, string School, string User,
        string Year, IReadOnlyList<string> Modules, string Notice);

    private bool executing;

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
        if (!MayShowWorkspace || !Pages.TryGetValue(id, out var page)) return null;
        return shell.Navigation.SelectMany(g => g.Items).FirstOrDefault(i => i.PageType == page);
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
                Pages.Keys.Where(id => AllowedPage(id) is not null).ToArray(), notice ?? "");
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
                    shell.Login.UserName = Required(arguments, "userName", 40);
                    if (!shell.Login.RecoveryOpen) shell.Login.ToggleRecoveryHelp.Execute(null);
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
                case "scanQr" when shell.ShowConnection && !shell.IsLoggedIn:
                    await shell.Connection.ScanCode.ExecuteAsync();
                    return Read(native: shell.ShowConnection);
                case "chooseServer" when !shell.IsLoggedIn && (shell.ShowSetup || shell.ShowConnection || shell.ShowLogin):
                    await shell.ChooseServerAsync();
                    return Read(native: true);
                case "createSchool" when shell.ShowSetup && !shell.IsLoggedIn:
                    return Read(native: true);
                case "openModule":
                    var item = AllowedPage(Required(arguments, "module", 40)) ??
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
