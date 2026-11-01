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
        DisableBiometrics = new AsyncCommand(DisableBiometricsAsync);
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
    public AsyncCommand DisableBiometrics { get; }

    private bool enableBiometricsAfterLogin;
    /// <summary>Compte dont le code a changé ailleurs : sa prochaine connexion au clavier met l'empreinte à jour.</summary>
    private string? resealFor;
    public bool ResealPending => resealFor is not null;
    public bool EnableBiometricsAfterLogin
    {
        get => enableBiometricsAfterLogin;
        set => Set(ref enableBiometricsAfterLogin, value);
    }
    public bool CanOfferBiometrics => Platform.IsMobile && Platform.IsBiometricAvailable?.Invoke() == true;
    public string BiometricAccount => BiometricAccess.ReadAccount(api.Server) ?? string.Empty;
    // Le retrait doit rester disponible par mot de passe lorsque le capteur a été désactivé.
    public bool HasBiometricAccount => Platform.IsMobile && BiometricAccount.Length > 0
        && (Platform.HasBiometricProof?.Invoke() ?? false);
    public bool CanUseBiometricSignIn => HasBiometricAccount && CanOfferBiometrics;
    public bool CanOfferEnrollment => CanOfferBiometrics && !HasBiometricAccount && Platform.EnrollBiometricAsync is not null;
    public string BiometricLabel => $"Déverrouiller avec {Platform.BiometricInSentence} · {BiometricAccount}";
    public string EnrollmentText => $"Activer {Platform.BiometricInSentence} après cette connexion";
    public string EnrollmentHint => Platform.BiometricInSentence == "l’empreinte"
        ? "Réservé à un téléphone personnel : toute empreinte forte enregistrée sur cet appareil pourra ouvrir le compte associé. Aucun modèle d’empreinte n’est enregistré par SCOLIA."
        : $"Réservé à un appareil personnel : tout visage ou empreinte enregistré dans {Platform.BiometricDisplayName} pourra ouvrir le compte associé. SCOLIA n’enregistre aucune donnée biométrique.";

    public void RefreshBiometrics()
    {
        Raise(nameof(CanOfferBiometrics));
        Raise(nameof(BiometricAccount));
        Raise(nameof(HasBiometricAccount));
        Raise(nameof(CanUseBiometricSignIn));
        Raise(nameof(CanOfferEnrollment));
        Raise(nameof(BiometricLabel));
    }

    /// <summary>
    /// Révoquer la liaison biométrique exige une preuve fraîche : Android Keystore,
    /// ou le mot de passe SCOLIA du compte exact, vérifié par le serveur.
    /// Un simple accès à la page de connexion ne suffit jamais.
    /// </summary>
    private async Task DisableBiometricsAsync()
    {
        if (Busy || Locked) return;
        Error = null;
        Notice = null;
        var account = BiometricAccess.ReadAccount(api.Server);
        if (string.IsNullOrWhiteSpace(account)) { RefreshBiometrics(); return; }

        Busy = true;
        try
        {
            var authorized = false;
            if (CanOfferBiometrics && Platform.UnlockBiometricAsync is { } unlock)
                authorized = BiometricAccess.Open(await unlock()) is { } opened
                    && BiometricAccess.AccountMatches(opened.UserName, account);

            // Si l'empreinte a été annulée, supprimée ou est indisponible,
            // le propriétaire peut saisir le mot de passe lié dans le formulaire.
            if (!authorized && !string.IsNullOrWhiteSpace(Password))
            {
                if (!string.IsNullOrWhiteSpace(UserName)
                    && !BiometricAccess.AccountMatches(UserName, account))
                {
                    Error = $"Le mot de passe doit être celui du compte associé à {Platform.BiometricInSentence}.";
                    return;
                }
                try
                {
                    var verified = await api.LoginAsync(account, Password);
                    authorized = BiometricAccess.AccountMatches(verified.UserName, account);
                }
                catch (ApiException)
                {
                    Error = "Mot de passe incorrect. La biométrie reste activée.";
                    return;
                }
            }
            if (!authorized)
            {
                Error = "Désactivation refusée. Validez votre empreinte ou saisissez le mot de passe du compte associé, puis réessayez.";
                return;
            }

            Platform.DisableBiometricAccess?.Invoke();
            BiometricAccess.Disable();
            EnableBiometricsAfterLogin = false;
            Password = string.Empty;
            RefreshBiometrics();
            Notice = "Accès biométrique désactivé après vérification d’identité.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Error = "Impossible de vérifier le mot de passe avec le serveur. La biométrie reste activée.";
        }
        finally { Busy = false; }
    }

    /// <summary>
    /// Connexion par empreinte : le téléphone rend l'accès scellé à l'activation, puis SCOLIA se connecte au serveur
    /// comme avec le clavier. Elle marche donc après une déconnexion, qu'on l'ait activée depuis cette page ou depuis
    /// Sécurité du compte. Sans réseau, le dernier compte connecté s'ouvre hors ligne.
    /// </summary>
    private async Task BiometricSignInAsync()
    {
        if (Busy || Locked || !CanOfferBiometrics || Platform.UnlockBiometricAsync is not { } unlock) return;
        Error = null;
        Notice = null;
        var account = BiometricAccess.ReadAccount(api.Server);
        if (account is null) { RefreshBiometrics(); return; }
        Busy = true;
        try
        {
            var opened = BiometricAccess.Open(await unlock());
            if (opened is not { } access)
            {
                // Annulé, ou empreintes du téléphone modifiées depuis l'activation : le code reste disponible.
                RefreshBiometrics();
                if (!HasBiometricAccount)
                    Error = $"{Platform.BiometricDisplayName} a changé sur ce téléphone. Connectez-vous avec votre code, puis réactivez-la.";
                return;
            }
            if (!BiometricAccess.AccountMatches(access.UserName, account))
            {
                Error = "Ce téléphone est associé à un autre compte. Connectez-vous avec votre code.";
                return;
            }
            UserName = access.UserName;
            await SignInAsync(access.UserName, access.Secret, fromBiometrics: true);
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
        Busy = true;
        try { await SignInAsync(UserName.Trim(), Password, fromBiometrics: false); }
        finally { Busy = false; }
    }

    /// <summary>Connexion au serveur, au clavier ou après l'empreinte ; hors ligne, le dernier compte de l'appareil.</summary>
    private async Task SignInAsync(string user, string secret, bool fromBiometrics)
    {
        Error = null;
        Restored = false;
        try
        {
            var session = await api.LoginAsync(user, secret);
            Authenticated?.Invoke(user, secret, session);
            // Mise à jour après un code changé ailleurs : déjà consentie, elle ne dépend pas de la case à cocher.
            var reseal = resealFor is { } account && BiometricAccess.AccountMatches(account, session.UserName);
            resealFor = null;
            if ((EnableBiometricsAfterLogin || reseal) && CanOfferBiometrics
                && Platform.EnrollBiometricAsync is { } enroll)
            {
                if (await enroll(BiometricAccess.Seal(session.UserName, secret)))
                    BiometricAccess.Enable(api.Server, session.UserName);
                else
                    Notice = $"L'accès par {Platform.BiometricDisplayName.ToLowerInvariant()} n'a pas été activé. Vous pourrez l'activer plus tard dans Sécurité du compte.";
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
            if (store.OfflineLogin(user, secret) is { } saved)
            {
                api.Offline = store;
                Authenticated?.Invoke(user, secret, saved);
                Password = string.Empty;
                if (fromBiometrics) Notice = "Connexion hors ligne · les changements seront envoyés au retour du serveur.";
                LoggedIn?.Invoke(saved);
            }
            else Error = store.HasCredential
                ? "Ordinateur principal injoignable. Hors ligne, seul le dernier compte connecté sur cet appareil peut s'ouvrir, avec son code."
                : "Ordinateur principal injoignable. Une première connexion en ligne est nécessaire pour travailler ensuite hors ligne.";
        }
        catch (ApiException locked) when (locked.RetryAfterSeconds is > 0) { StartLock(locked.RetryAfterSeconds.Value, DateTime.UtcNow); }
        catch (ApiException ex) when (fromBiometrics && ex.Status is 400 or 401)
        {
            // Le code a été changé depuis un autre appareil : une saisie suffit, l'empreinte est alors mise à jour.
            resealFor = user;
            Error = $"Votre code a changé depuis l'activation de {Platform.BiometricInSentence}. Saisissez-le une fois : {Platform.BiometricInSentence} sera mise à jour.";
        }
        catch (ApiException ex) { Error = ex.Message; }
        catch (HttpRequestException) { Error = "Le serveur SCOLIA ne répond pas. Vérifiez qu'il est lancé sur ce poste."; }
    }
}
