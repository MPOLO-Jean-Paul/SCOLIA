using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>
/// Écran de mise en veille : la session reste ouverte derrière (rien n'est perdu), mais tout est caché jusqu'à ce que
/// la personne redonne son code ou son empreinte. Le serveur vérifie le code, avec les mêmes protections que la
/// connexion (cinq erreurs, deux minutes de blocage) ; sans réseau, le code est vérifié sur l'appareil.
/// </summary>
public sealed class LockScreenViewModel : Observable
{
    private readonly ShellViewModel shell;
    private string code = string.Empty;
    private string? error;
    private bool busy;
    private bool visible;

    public LockScreenViewModel(ShellViewModel shell)
    {
        this.shell = shell;
        Unlock = new AsyncCommand(UnlockAsync);
        UnlockWithBiometrics = new AsyncCommand(BiometricUnlockAsync);
        SwitchAccount = new AsyncCommand(SwitchAsync);
    }

    // ---------- Présentation : heure, date, établissement et compte, comme l'écran verrouillé d'un téléphone ----------
    private static readonly System.Globalization.CultureInfo Fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
    private DateTime lockedAt = DateTime.Now;
    private Avalonia.Threading.DispatcherTimer? clock;
    public string Time => DateTime.Now.ToString("HH:mm", Fr);
    public string DateText
    {
        get
        {
            var text = DateTime.Now.ToString("dddd d MMMM", Fr);
            return char.ToUpperInvariant(text[0]) + text[1..];
        }
    }
    public string LockedSince => $"Verrouillé depuis {lockedAt.ToString("HH:mm", Fr)}";
    public string Role => shell.Session?.RoleLabel ?? string.Empty;
    public string AccountLine => string.IsNullOrWhiteSpace(Role) ? UserName : $"{Role} · {UserName}";
    public Avalonia.Media.Imaging.Bitmap? Logo => shell.SchoolLogo;
    public bool HasLogo => shell.HasSchoolLogo;
    public string Footer => "SCOLIA Pro · session protégée, rien n'est perdu";

    private void StartClock()
    {
        if (clock is not null || Avalonia.Application.Current is null) return;
        clock = new Avalonia.Threading.DispatcherTimer(TimeSpan.FromSeconds(15), Avalonia.Threading.DispatcherPriority.Background,
            (_, _) => { Raise(nameof(Time)); Raise(nameof(DateText)); });
        clock.Start();
    }

    private void StopClock()
    {
        clock?.Stop();
        clock = null;
    }

    public string UserName => shell.Session?.UserName ?? string.Empty;
    public string DisplayName => shell.Session?.DisplayName ?? string.Empty;
    public string School => shell.Session?.SchoolName ?? string.Empty;
    public string Initials => string.Concat(DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
    public string Delay => shell.Prefs.LockMinutes == 1 ? "1 minute" : $"{shell.Prefs.LockMinutes} minutes";
    private bool manual;
    public string Message => manual
        ? "Verrouillé à votre demande. Saisissez votre code pour reprendre là où vous étiez."
        : $"Mis en veille après {Delay} sans activité. Saisissez votre code pour reprendre là où vous étiez.";

    public string Code { get => code; set { if (Set(ref code, value ?? string.Empty)) { Error = null; Unlock.Refresh(); } } }
    public string? Error { get => error; private set { if (Set(ref error, value)) Raise(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(error);
    public bool Busy { get => busy; private set { if (Set(ref busy, value)) Raise(nameof(NotBusy)); } }
    public bool NotBusy => !busy;

    private bool showCode;
    public bool ShowCode { get => showCode; set { if (Set(ref showCode, value)) { Raise(nameof(CodeMask)); Raise(nameof(EyeIcon)); } } }
    public char CodeMask => showCode ? '\0' : '•';
    public MaterialIconKind EyeIcon => showCode ? MaterialIconKind.EyeOffOutline : MaterialIconKind.EyeOutline;
    public RelayCommand ToggleCode => toggleCode ??= new RelayCommand(() => ShowCode = !ShowCode);
    private RelayCommand? toggleCode;

    /// <summary>Empreinte ou Face ID proposés si ce téléphone les a activés pour ce compte.</summary>
    public bool CanUseBiometrics => Platform.UnlockBiometricAsync is not null && Platform.IsBiometricAvailable?.Invoke() == true
        && BiometricAccess.ReadAccount(shell.Api.Server) is { } account && BiometricAccess.AccountMatches(account, UserName);
    public string BiometricLabel => $"Déverrouiller avec {Platform.BiometricInSentence}";

    public AsyncCommand Unlock { get; }
    public AsyncCommand UnlockWithBiometrics { get; }
    public AsyncCommand SwitchAccount { get; }

    /// <summary>Le verrou vient de se fermer : champ vide, informations du compte à jour.</summary>
    public void Shown(bool byRequest = false)
    {
        visible = true;
        manual = byRequest;
        lockedAt = DateTime.Now;
        StartClock();
        Code = string.Empty;
        Error = null;
        ShowCode = false;
        foreach (var name in new[] { nameof(UserName), nameof(DisplayName), nameof(School), nameof(Initials), nameof(Delay),
                     nameof(Message), nameof(CanUseBiometrics), nameof(BiometricLabel), nameof(Time), nameof(DateText),
                     nameof(LockedSince), nameof(Role), nameof(AccountLine), nameof(Logo), nameof(HasLogo) })
            Raise(name);
    }

    private async Task UnlockAsync()
    {
        if (Busy || !visible) return;
        if (code.Length == 0) { Error = "Saisissez votre code PIN ou votre mot de passe."; return; }
        await VerifyAsync(code);
    }

    private async Task BiometricUnlockAsync()
    {
        if (Busy || !visible || Platform.UnlockBiometricAsync is not { } unlock) return;
        Busy = true;
        string? secret;
        try { secret = BiometricAccess.Open(await unlock()) is { } access && BiometricAccess.AccountMatches(access.UserName, UserName) ? access.Secret : null; }
        finally { Busy = false; }
        if (secret is null) { Error = $"{Platform.BiometricDisplayName} non reconnue. Saisissez votre code."; return; }
        await VerifyAsync(secret);
    }

    private async Task VerifyAsync(string secret)
    {
        Busy = true;
        Error = null;
        try
        {
            var session = await shell.Api.LoginAsync(UserName, secret);
            if (!BiometricAccess.AccountMatches(session.UserName, UserName)) { Error = "Ce code n'est pas celui de ce compte."; return; }
            Done();
        }
        catch (ApiException locked) when (locked.RetryAfterSeconds is > 0)
        {
            Error = $"Trop d'essais : réessayez dans {locked.RetryAfterSeconds} secondes.";
        }
        catch (Exception ex) when (ex is OfflineException or HttpRequestException or TaskCanceledException)
        {
            // Sans réseau, le code est vérifié sur l'appareil, comme pour la connexion hors ligne.
            var store = shell.Api.Offline ?? new OfflineStore(shell.Api.Server);
            if (store.OfflineLogin(UserName, secret) is not null) Done();
            else Error = "Code incorrect, ou ordinateur principal injoignable et code jamais utilisé sur cet appareil.";
        }
        // Sur l'écran de veille, seul le code est saisi : le message parle du code, pas de l'identifiant.
        catch (ApiException ex) { Error = ex.Message.Replace("Identifiant ou mot de passe incorrect.", "Code incorrect.", StringComparison.Ordinal); }
        finally { Busy = false; }
    }

    private void Done()
    {
        visible = false;
        StopClock();
        Code = string.Empty;
        shell.Unlock();
    }

    /// <summary>
    /// Une autre personne veut travailler : la page de connexion s'affiche, mais cette session reste verrouillée derrière.
    /// La page de connexion propose d'y revenir.
    /// </summary>
    private Task SwitchAsync()
    {
        visible = false;
        StopClock();
        Code = string.Empty;
        shell.BeginAccountSwitch();
        return Task.CompletedTask;
    }
}
