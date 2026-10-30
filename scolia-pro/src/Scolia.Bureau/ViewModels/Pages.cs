using Avalonia.Threading;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Écran de connexion.</summary>
public sealed class LoginViewModel : Observable
{
    private readonly ScoliaClient api;
    private string userName = string.Empty;
    private string password = string.Empty;
    private string? error;
    private bool busy;
    private string schoolName = string.Empty;

    public LoginViewModel(ScoliaClient api)
    {
        this.api = api;
        Submit = new AsyncCommand(SubmitAsync);
        SignInWithBiometrics = new AsyncCommand(BiometricSignInAsync);
        DisableBiometrics = new RelayCommand(() =>
        {
            Platform.DisableBiometricAccess?.Invoke();
            BiometricAccess.Disable();
            EnableBiometricsAfterLogin = false;
            RefreshBiometrics();
            Notice = "Connexion biométrique désactivée sur cet appareil.";
        });
    }

    public event Action<SessionDto>? LoggedIn;

    /// <summary>Connexion vérifiée par le serveur : le compte peut être gardé pour une connexion hors ligne.</summary>
    public event Action<string, string, SessionDto>? Authenticated;

    public string UserName { get => userName; set { if (Set(ref userName, value)) Submit.Refresh(); } }
    public string Password { get => password; set { if (Set(ref password, value)) Submit.Refresh(); } }
    public string? Error { get => error; set { if (Set(ref error, value)) Raise(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(error);

    private string? notice;
    /// <summary>Information au-dessus du formulaire : l'ordinateur principal est injoignable, travail hors ligne possible.</summary>
    public string? Notice { get => notice; set { if (Set(ref notice, value)) Raise(nameof(HasNotice)); } }
    public bool HasNotice => !string.IsNullOrEmpty(notice);

    /// <summary>Nom de l'établissement, connu dès le démarrage du serveur : il titre l'écran de connexion.</summary>
    public string SchoolName { get => schoolName; set { if (Set(ref schoolName, value)) { Raise(nameof(TitleText)); } } }
    public string TitleText => string.IsNullOrWhiteSpace(schoolName) ? "SCOLIA" : schoolName;
    public bool Busy { get => busy; set { if (Set(ref busy, value)) Raise(nameof(NotBusy)); } }
    public bool NotBusy => !busy;

    private bool showPassword;
    /// <summary>L'œil du champ affiche ou masque le mot de passe saisi.</summary>
    public bool ShowPassword { get => showPassword; set { if (Set(ref showPassword, value)) Raise(nameof(EyeIcon)); } }
    public Material.Icons.MaterialIconKind EyeIcon => showPassword ? Material.Icons.MaterialIconKind.EyeOffOutline : Material.Icons.MaterialIconKind.EyeOutline;
    public RelayCommand ToggleEye => toggleEye ??= new RelayCommand(() => ShowPassword = !ShowPassword);
    private RelayCommand? toggleEye;

    /// <summary>Pied de la fenêtre de connexion : version et éditeur.</summary>
    public string Footer => $"SCOLIA Pro {typeof(LoginViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.1.0"} · {AboutInfo.Publisher}";
    public AsyncCommand Submit { get; }
    public AsyncCommand SignInWithBiometrics { get; }
    public RelayCommand DisableBiometrics { get; }

    private bool enableBiometricsAfterLogin;
    public bool EnableBiometricsAfterLogin
    {
        get => enableBiometricsAfterLogin;
        set => Set(ref enableBiometricsAfterLogin, value);
    }
    public bool CanOfferBiometrics => Platform.IsMobile && Platform.IsBiometricAvailable?.Invoke() == true;
    public string BiometricAccount => BiometricAccess.ReadAccount(api.Server) ?? string.Empty;
    public bool HasBiometricAccount => CanOfferBiometrics && BiometricAccount.Length > 0
        && (Platform.HasBiometricProof?.Invoke() ?? false);
    public bool CanOfferEnrollment => CanOfferBiometrics && !HasBiometricAccount;
    public string BiometricLabel => $"Déverrouiller avec l’empreinte · {BiometricAccount}";

    public void RefreshBiometrics()
    {
        Raise(nameof(CanOfferBiometrics));
        Raise(nameof(BiometricAccount));
        Raise(nameof(HasBiometricAccount));
        Raise(nameof(CanOfferEnrollment));
        Raise(nameof(BiometricLabel));
    }

    private async Task BiometricSignInAsync()
    {
        if (Busy || Locked || !CanOfferBiometrics || Platform.VerifyBiometricAsync is not { } verify) return;
        Error = null;
        Notice = null;
        var account = BiometricAccess.ReadAccount(api.Server);
        if (account is null) { RefreshBiometrics(); return; }
        Busy = true;
        try
        {
            // L'empreinte confirme seulement la présence du propriétaire du téléphone.
            // Le serveur doit ENCORE valider le cookie sauvegardé pour ce compte et ce serveur.
            if (!await verify()) return;
            var cache = new OfflineStore(api.Server);
            var credential = cache.SavedCredential;
            if (credential is null || !BiometricAccess.AccountMatches(account, credential.UserName))
            {
                Error = "Ce compte n'est plus enregistré sur ce téléphone. Reconnectez-vous avec le mot de passe.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(credential.SessionCookie))
            {
                api.RestoreSessionCookie(credential.SessionCookie);
                try
                {
                    var session = await api.CurrentSessionAsync();
                    if (session is null || !BiometricAccess.AccountMatches(session.UserName, account))
                    {
                        Error = "La session du serveur est expirée ou a changé de compte. Reconnectez-vous avec le mot de passe.";
                        return;
                    }
                    cache.User = session.UserName;
                    api.Offline = cache;
                    Password = string.Empty;
                    LoggedIn?.Invoke(session);
                    return;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
                {
                    // Une coupure réseau autorise la même session locale limitée que la connexion hors ligne classique.
                }
            }

            // Si le serveur est joignable mais qu'aucun cookie valide n'existe,
            // l'empreinte seule ne peut pas recréer une identité côté serveur.
            if (string.IsNullOrWhiteSpace(credential.SessionCookie))
            {
                try
                {
                    if (await api.IsRunningAsync())
                    {
                        Error = "La session serveur n'est plus enregistrée. Reconnectez-vous avec votre mot de passe.";
                        return;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException) { }
            }
            // L'accès hors ligne ne peut être accordé qu'après la preuve Android Keystore.
            // Le cache ne vaut ni nouveau jeton serveur, ni validation de droits à distance.
            var offline = cache.BiometricOfflineLogin(account);
            if (offline is null)
            {
                Error = "Aucune session locale du compte n'est disponible. Connectez-vous avec le mot de passe.";
                return;
            }
            cache.User = offline.UserName;
            api.Offline = cache;
            Password = string.Empty;
            Notice = "Connexion biométrique hors ligne · les changements seront vérifiés au retour du serveur.";
            LoggedIn?.Invoke(offline);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ApiException
                                 or IOException or System.Security.Cryptography.CryptographicException)
        {
            Error = "Impossible de valider la session avec le serveur. Utilisez le mot de passe SCOLIA.";
        }
        finally { Busy = false; }
    }


    private Avalonia.Media.Imaging.Bitmap? logo;

    /// <summary>Logo de l'établissement ; le logo de SCOLIA s'affiche tant qu'il n'est pas chargé.</summary>
    public Avalonia.Media.Imaging.Bitmap? Logo { get => logo; set { if (Set(ref logo, value)) Raise(nameof(HasLogo)); } }
    public bool HasLogo => logo is not null;

    public void Reset()
    {
        Password = string.Empty;
        Error = null;
        Restored = false;
        EnableBiometricsAfterLogin = false;
        RefreshBiometrics();
    }

    // ---------- Verrouillage après 5 erreurs : compte à rebours jusqu'à 0, puis l'accès revient ----------
    private DateTime? lockedUntil;
    private int lockTotal = 1;
    private int secondsLeft;
    private bool restored;
    private DispatcherTimer? lockTimer;

    /// <summary>Déclenché quand l'accès revient : la vue remet le curseur dans le mot de passe.</summary>
    public event Action? Unlocked;

    public bool Locked => lockedUntil is not null;
    public bool CanType => !Locked;
    public int SecondsLeft { get => secondsLeft; private set { if (Set(ref secondsLeft, value)) { Raise(nameof(CountdownText)); Raise(nameof(LockProgress)); } } }
    public string CountdownText => $"{secondsLeft / 60}:{secondsLeft % 60:00}";
    public double LockProgress => 100.0 * secondsLeft / lockTotal;
    public bool Restored { get => restored; private set => Set(ref restored, value); }

    public void StartLock(int seconds, DateTime now)
    {
        lockTotal = Math.Max(1, seconds);
        lockedUntil = now.AddSeconds(seconds);
        Error = null;
        Restored = false;
        Password = string.Empty;
        Raise(nameof(Locked)); Raise(nameof(CanType));
        Tick(now);
        if (lockTimer is null && Avalonia.Application.Current is not null)
        {
            lockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            lockTimer.Tick += (_, _) => Tick(DateTime.UtcNow);
        }
        lockTimer?.Start();
    }

    /// <summary>Met le compteur à jour ; à 0 seconde, le formulaire est rendu.</summary>
    public void Tick(DateTime now)
    {
        if (lockedUntil is not { } until) return;
        SecondsLeft = Math.Max(0, (int)Math.Ceiling((until - now).TotalSeconds));
        if (secondsLeft > 0) return;
        lockTimer?.Stop();
        lockedUntil = null;
        Raise(nameof(Locked)); Raise(nameof(CanType));
        Restored = true;
        Unlocked?.Invoke();
    }

    private async Task SubmitAsync()
    {
        if (Locked) return;
        Error = null;
        Restored = false;
        Busy = true;
        try
        {
            var session = await api.LoginAsync(UserName.Trim(), Password);
            Authenticated?.Invoke(UserName.Trim(), Password, session);
            if (EnableBiometricsAfterLogin && CanOfferBiometrics
                && Platform.EnrollBiometricAsync is { } enroll)
            {
                if (await enroll())
                    BiometricAccess.Enable(api.Server, session.UserName);
                else
                    Notice = "L'accès par empreinte n'a pas été activé. Vous pourrez réessayer après une connexion par mot de passe.";
            }
            EnableBiometricsAfterLogin = false;
            RefreshBiometrics();
            Password = string.Empty;
            LoggedIn?.Invoke(session);
        }
        catch (Exception ex) when (ex is OfflineException or HttpRequestException or TaskCanceledException)
        {
            // Ordinateur principal injoignable : le dernier compte connecté sur cet appareil peut travailler hors ligne.
            var store = api.Offline ?? new Infrastructure.OfflineStore(api.Server);
            if (store.OfflineLogin(UserName.Trim(), Password) is { } saved)
            {
                api.Offline = store;
                Authenticated?.Invoke(UserName.Trim(), Password, saved);
                Password = string.Empty;
                LoggedIn?.Invoke(saved);
            }
            else Error = store.HasCredential
                ? "Ordinateur principal injoignable. Hors ligne, seul le dernier compte connecté sur cet appareil peut s'ouvrir, avec son mot de passe."
                : "Ordinateur principal injoignable. Une première connexion en ligne est nécessaire pour travailler ensuite hors ligne.";
        }
        catch (ApiException locked) when (locked.RetryAfterSeconds is > 0) { StartLock(locked.RetryAfterSeconds.Value, DateTime.UtcNow); }
        catch (ApiException ex) { Error = ex.Message; }
        catch (HttpRequestException) { Error = "Le serveur SCOLIA ne répond pas. Vérifiez qu'il est lancé sur ce poste."; }
        finally { Busy = false; }
    }
}
