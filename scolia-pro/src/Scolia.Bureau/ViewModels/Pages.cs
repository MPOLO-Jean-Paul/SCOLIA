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
    private bool recoveryOpen;
    private bool editingAccount;
    private bool linkedClient;
    private bool manualLoginOpen;
    private Uri? accountServer;
    private string recoveryCode = string.Empty;
    private string newPassword = string.Empty;
    private string confirmPassword = string.Empty;

    public LoginViewModel(ScoliaClient api)
    {
        this.api = api;
        Submit = new AsyncCommand(SubmitAsync);
        SignInWithBiometrics = new AsyncCommand(BiometricSignInAsync);
        DisableBiometrics = new AsyncCommand(DisableBiometricsAsync);
        RecoverWithBiometrics = new AsyncCommand(RecoverWithBiometricsAsync);
        RecoverWithCode = new AsyncCommand(RecoverWithCodeAsync);
    }

    public event Action<SessionDto>? LoggedIn;

    /// <summary>
    /// « Changer d'ordinateur principal » : l'ordinateur principal ne répond pas, ou ce poste doit rejoindre un autre
    /// établissement. La page de connexion n'est jamais une impasse.
    /// </summary>
    public event Action? ChangeServerRequested;
    public RelayCommand ChangeServer => changeServer ??= new RelayCommand(() => { if (!Busy) ChangeServerRequested?.Invoke(); });
    private RelayCommand? changeServer;
    /// <summary>Ordinateur auquel ce poste est relié, rappelé en bas de la page de connexion.</summary>
    public string ServerLine => api.Server.IsLoopback
        ? "Cet appareil est l'ordinateur principal"
        : $"Relié à l'ordinateur principal {api.Server.Host}";
    public void RefreshServer()
    {
        RecallLastAccount();
        Raise(nameof(ServerLine));
    }

    /// <summary>Réutilise uniquement l'identifiant du dernier compte pour ce serveur.</summary>
    private void RecallLastAccount()
    {
        // Changer de compte doit rester un choix explicite, même après un rafraîchissement graphique.
        if (accountServer == api.Server) return;
        accountServer = api.Server;
        try
        {
            var last = new OfflineStore(api.Server).LastAccount;
            if (string.IsNullOrWhiteSpace(last)) last = BiometricAccess.ReadAccount(api.Server);
            if (!string.IsNullOrWhiteSpace(last)) RememberAccount(last);
            else { editingAccount = true; UserName = string.Empty; RaiseAccountState(); }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException
            or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
        {
            editingAccount = true;
            RaiseAccountState();
        }
    }

    public sealed record LocalAccount(string? UserName);

    /// <summary>Sur le serveur seul, retrouve un compte déjà configuré même avant la première connexion de ce poste.</summary>
    public async Task DetectAccountAsync()
    {
        RecallLastAccount();
        if (!string.IsNullOrWhiteSpace(UserName) || !api.Server.IsLoopback) return;
        try
        {
            var suggested = await api.GetAsync<LocalAccount>("api/installation/compte-local");
            if (!string.IsNullOrWhiteSpace(suggested?.UserName)) RememberAccount(suggested.UserName);
        }
        catch (Exception ex) when (ex is ApiException or OfflineException or HttpRequestException or TaskCanceledException) { }
    }

    public void RememberAccount(string account)
    {
        if (string.IsNullOrWhiteSpace(account)) return;
        editingAccount = false;
        UserName = account.Trim();
        RaiseAccountState();
    }

    private void RaiseAccountState()
    {
        Raise(nameof(HasRememberedAccount));
        Raise(nameof(ShowAccountEntry));
        Raise(nameof(IntroText));
    }

    /// <summary>Un poste invité rejoint le compte approuvé sans demander son mot de passe.</summary>
    public bool LinkedClient => linkedClient;
    public bool ShowManualCredentials => !linkedClient || manualLoginOpen;
    public bool ShowBiometricSignIn => ShowManualCredentials && !RecoveryOpen && CanUseBiometricSignIn;
    public bool ShowBiometricEnrollment => ShowManualCredentials && !RecoveryOpen && CanOfferEnrollment;
    public bool ShowRemoveBiometric => ShowManualCredentials && !RecoveryOpen && HasBiometricAccount;
    public bool ShowPairingCallToAction => linkedClient && !manualLoginOpen;
    public void ConfigureClientMode(bool isClient)
    {
        if (linkedClient == isClient) return;
        linkedClient = isClient;
        manualLoginOpen = false;
        Raise(nameof(LinkedClient));
        Raise(nameof(ShowManualCredentials));
        Raise(nameof(ShowPairingCallToAction));
        Raise(nameof(ShowBiometricSignIn));
        Raise(nameof(ShowBiometricEnrollment));
        Raise(nameof(ShowRemoveBiometric));
        Raise(nameof(IntroText));
    }

    public RelayCommand EnableManualLogin => enableManualLogin ??= new RelayCommand(() =>
    {
        manualLoginOpen = true;
        Raise(nameof(ShowManualCredentials));
        Raise(nameof(ShowPairingCallToAction));
        Raise(nameof(ShowBiometricSignIn));
        Raise(nameof(ShowBiometricEnrollment));
        Raise(nameof(ShowRemoveBiometric));
        Raise(nameof(IntroText));
    });
    private RelayCommand? enableManualLogin;

    public bool HasRememberedAccount => !editingAccount && !string.IsNullOrWhiteSpace(UserName);
    public bool ShowAccountEntry => !HasRememberedAccount;
    public RelayCommand ChangeAccount => changeAccount ??= new RelayCommand(() =>
    {
        if (Busy) return;
        editingAccount = true;
        UserName = string.Empty;
        Password = string.Empty;
        ClearRecoverySecrets();
        Error = null;
        RaiseAccountState();
    });
    private RelayCommand? changeAccount;

    /// <summary>« Revenir à la session de … » : la page de connexion rend la main à l'écran de veille.</summary>
    public event Action? ResumeRequested;
    private string? resumeName;
    private string? resumeUser;
    /// <summary>Compte resté verrouillé derrière la page de connexion, ou rien.</summary>
    public string? ResumeName => resumeName;
    public bool CanResume => resumeName is not null;
    public string ResumeText => $"Revenir à la session de {resumeName}";
    /// <summary>Phrase sous « Connexion » : elle rappelle qu'une session attend, verrouillée.</summary>
    public string IntroText => resumeName is null
        ? (linkedClient && !manualLoginOpen
            ? "Ce poste utilise une invitation du serveur : scannez le QR pour vous connecter automatiquement."
            : HasRememberedAccount ? "Saisissez votre mot de passe pour continuer." : "Saisissez votre identifiant et votre mot de passe.")
        : "Connectez un autre compte. La session verrouillée reste ouverte : vous pouvez y revenir à tout moment.";
    public string ResumeHint => resumeUser is null ? string.Empty : $"{resumeUser} · session verrouillée, rien n'est perdu";
    public RelayCommand Resume => resume ??= new RelayCommand(() =>
    {
        if (Busy || !CanResume) return;
        Password = string.Empty;
        Error = null;
        ResumeRequested?.Invoke();
    });
    private RelayCommand? resume;

    /// <summary>Annonce, ou retire, la session verrouillée à laquelle on peut revenir.</summary>
    public void OfferResume(string? displayName, string? userName)
    {
        resumeName = displayName is null ? null : (string.IsNullOrWhiteSpace(displayName) ? userName : displayName);
        resumeUser = displayName is null ? null : userName;
        if (resumeName is not null) { RememberAccount(userName ?? string.Empty); Password = string.Empty; Error = null; }
        foreach (var name in new[] { nameof(ResumeName), nameof(CanResume), nameof(ResumeText), nameof(ResumeHint), nameof(IntroText) }) Raise(name);
    }

    /// <summary>Connexion vérifiée par le serveur : le compte peut être gardé pour une connexion hors ligne.</summary>
    public event Action<string, string, SessionDto>? Authenticated;

    public string UserName { get => userName; set { if (Set(ref userName, value)) { Raise(nameof(CanSubmitCredentials)); Submit.Refresh(); RaiseAccountState(); RefreshRecoveryValidation(); } } }
    public string Password { get => password; set { if (Set(ref password, value)) { Raise(nameof(CanSubmitCredentials)); Submit.Refresh(); } } }
    public bool CanSubmitCredentials => !Locked && !Busy && !string.IsNullOrWhiteSpace(UserName) && Password.Length > 0;
    public string? Error { get => error; set { if (Set(ref error, value)) Raise(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(error);

    /// <summary>Guide d'accès sans contournement de l'authentification, disponible avant connexion.</summary>
    /// <summary>La récupération remplace visuellement le champ de connexion, elle ne s'y empile pas.</summary>
    public bool RecoveryOpen
    {
        get => recoveryOpen;
        private set
        {
            if (!Set(ref recoveryOpen, value)) return;
            Raise(nameof(ShowSignInPassword));
            Raise(nameof(RecoveryActionLabel));
            Raise(nameof(CanRecoverWithCode));
            Raise(nameof(CanRecoverWithBiometrics));
            Raise(nameof(ShowBiometricSignIn));
            Raise(nameof(ShowBiometricEnrollment));
            Raise(nameof(ShowRemoveBiometric));
        }
    }
    public bool ShowSignInPassword => !RecoveryOpen;
    public string RecoveryActionLabel => RecoveryOpen ? "Revenir à la connexion" : "Mot de passe oublié ?";
    private void ClearRecoverySecrets()
    {
        RecoveryCode = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
        ShowRecoveryCode = false;
        ShowNewPassword = false;
        ShowConfirmPassword = false;
    }
    public RelayCommand ToggleRecoveryHelp => recoveryHelp ??= new RelayCommand(() =>
    {
        if (Busy) return;
        if (RecoveryOpen)
        {
            RecoveryOpen = false;
            ClearRecoverySecrets();
        }
        else
        {
            Password = string.Empty;
            ShowPassword = false;
            Notice = null;
            RecoveryOpen = true;
        }
        Error = null;
    });
    private RelayCommand? recoveryHelp;

    /// <summary>Ouvre l'aide depuis une session verrouillée sans tenter de déverrouiller le compte.</summary>
    public void ShowRecoveryHelp(string account)
    {
        if (!string.IsNullOrWhiteSpace(account)) RememberAccount(account);
        manualLoginOpen = true;
        Raise(nameof(ShowManualCredentials));
        Raise(nameof(ShowPairingCallToAction));
        Raise(nameof(ShowBiometricSignIn));
        Raise(nameof(ShowBiometricEnrollment));
        Raise(nameof(ShowRemoveBiometric));
        Password = string.Empty;
        ShowPassword = false;
        ClearRecoverySecrets();
        RecoveryOpen = true;
        Error = null;
        Notice = null;
    }
    public string RecoveryInstructions =>
        "Choisissez un nouveau mot de passe. Validez avec votre empreinte déjà associée à ce compte, " +
        "ou saisissez le code de secours détenu par le responsable du serveur. Aucune ouverture des paramètres n'est nécessaire.";

    private void RefreshRecoveryValidation()
    {
        Raise(nameof(CanRecoverWithCode));
        Raise(nameof(CanRecoverWithBiometrics));
        Raise(nameof(RecoveryPasswordHint));
        Raise(nameof(RecoveryConfirmationHint));
        Raise(nameof(HasRecoveryPasswordHint));
        Raise(nameof(HasRecoveryConfirmationHint));
    }

    public string RecoveryCode
    {
        get => recoveryCode;
        set { if (Set(ref recoveryCode, value ?? string.Empty)) { Error = null; RefreshRecoveryValidation(); } }
    }
    public string NewPassword
    {
        get => newPassword;
        set { if (Set(ref newPassword, value ?? string.Empty)) { Error = null; RefreshRecoveryValidation(); } }
    }
    public string ConfirmPassword
    {
        get => confirmPassword;
        set { if (Set(ref confirmPassword, value ?? string.Empty)) { Error = null; RefreshRecoveryValidation(); } }
    }

    /// <summary>Retours discrets sous les champs, plutôt qu'une erreur seulement après soumission.</summary>
    public string RecoveryPasswordHint => NewPassword.Length == 0 ? AccessCode.Hint
        : AccessCode.Problem(NewPassword) ?? "Nouveau mot de passe conforme.";
    public string RecoveryConfirmationHint => ConfirmPassword.Length == 0 ? "Saisissez le même mot de passe une deuxième fois."
        : NewPassword == ConfirmPassword ? "Les deux mots de passe correspondent."
        : "Les deux mots de passe ne correspondent pas.";
    public bool HasRecoveryPasswordHint => RecoveryOpen;
    public bool HasRecoveryConfirmationHint => RecoveryOpen && ConfirmPassword.Length > 0;

    private bool HasValidRecoveryPassword => !string.IsNullOrWhiteSpace(UserName)
        && NewPassword.Length > 0 && AccessCode.Problem(NewPassword) is null
        && NewPassword == ConfirmPassword;

    public bool CanRecoverWithCode => RecoveryOpen && !Busy && HasValidRecoveryPassword
        && !string.IsNullOrWhiteSpace(RecoveryCode);
    public bool CanRecoverWithBiometrics => RecoveryOpen && !Busy && HasValidRecoveryPassword
        && CanUseBiometricSignIn && BiometricAccess.AccountMatches(BiometricAccount, UserName);

    private bool showRecoveryCode;
    private bool showNewPassword;
    private bool showConfirmPassword;

    public bool ShowRecoveryCode { get => showRecoveryCode; set { if (Set(ref showRecoveryCode, value)) Raise(nameof(RecoveryCodeEyeIcon)); } }
    public bool ShowNewPassword { get => showNewPassword; set { if (Set(ref showNewPassword, value)) Raise(nameof(NewPasswordEyeIcon)); } }
    public bool ShowConfirmPassword { get => showConfirmPassword; set { if (Set(ref showConfirmPassword, value)) Raise(nameof(ConfirmPasswordEyeIcon)); } }

    public Material.Icons.MaterialIconKind RecoveryCodeEyeIcon => ShowRecoveryCode ? Material.Icons.MaterialIconKind.EyeOffOutline : Material.Icons.MaterialIconKind.EyeOutline;
    public Material.Icons.MaterialIconKind NewPasswordEyeIcon => ShowNewPassword ? Material.Icons.MaterialIconKind.EyeOffOutline : Material.Icons.MaterialIconKind.EyeOutline;
    public Material.Icons.MaterialIconKind ConfirmPasswordEyeIcon => ShowConfirmPassword ? Material.Icons.MaterialIconKind.EyeOffOutline : Material.Icons.MaterialIconKind.EyeOutline;
    public RelayCommand ToggleRecoveryCodeEye => toggleRecoveryCodeEye ??= new RelayCommand(() => ShowRecoveryCode = !ShowRecoveryCode);
    public RelayCommand ToggleNewPasswordEye => toggleNewPasswordEye ??= new RelayCommand(() => ShowNewPassword = !ShowNewPassword);
    public RelayCommand ToggleConfirmPasswordEye => toggleConfirmPasswordEye ??= new RelayCommand(() => ShowConfirmPassword = !ShowConfirmPassword);
    private RelayCommand? toggleRecoveryCodeEye;
    private RelayCommand? toggleNewPasswordEye;
    private RelayCommand? toggleConfirmPasswordEye;

    public AsyncCommand RecoverWithCode { get; }
    public AsyncCommand RecoverWithBiometrics { get; }

    private async Task RecoverWithCodeAsync()
    {
        if (Busy || !RecoveryOpen) return;
        if (string.IsNullOrWhiteSpace(UserName)) { Error = "Choisissez le compte à restaurer."; return; }
        if (string.IsNullOrWhiteSpace(RecoveryCode)) { Error = "Saisissez le code de secours du serveur."; return; }
        if (AccessCode.Problem(NewPassword) is { } problem) { Error = problem; return; }
        if (NewPassword != ConfirmPassword) { Error = "Les deux mots de passe ne correspondent pas."; return; }
        Busy = true;
        Error = null;
        try
        {
            await api.SendAsync(HttpMethod.Post, "api/session/recuperation", new
            {
                userName = UserName.Trim(), recoveryCode = RecoveryCode.Trim(),
                newPassword = NewPassword, confirmation = ConfirmPassword
            });
            try
            {
                var store = api.Offline ?? new OfflineStore(api.Server);
                store.ForgetCredential();
                store.RememberAccount(UserName);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
            ClearRecoverySecrets();
            Password = string.Empty;
            RecoveryOpen = false;
            Notice = "Mot de passe restauré. Connectez-vous avec le nouveau mot de passe. Le code de secours du serveur a été renouvelé.";
        }
        catch (ApiException ex) { Error = ex.Status is 401 or 429
            ? "Code de secours non reconnu ou nombre de tentatives dépassé." : ex.Message; }
        catch (Exception ex) when (ex is OfflineException or HttpRequestException or TaskCanceledException)
        { Error = "Le serveur doit être accessible pour restaurer le mot de passe."; }
        finally { Busy = false; }
    }

    private async Task RecoverWithBiometricsAsync()
    {
        if (Busy || !RecoveryOpen || !CanUseBiometricSignIn
            || Platform.UnlockBiometricAsync is not { } unlock) return;
        if (!BiometricAccess.AccountMatches(BiometricAccount, UserName))
        { Error = "Choisissez le compte associé à l'empreinte de cet appareil."; return; }
        if (AccessCode.Problem(NewPassword) is { } problem) { Error = problem; return; }
        if (NewPassword != ConfirmPassword)
        { Error = "Les deux mots de passe ne correspondent pas."; return; }
        Busy = true;
        Error = null;
        bool authenticated = false;
        bool changed = false;
        var replacement = NewPassword;
        try
        {
            var opened = BiometricAccess.Open(await unlock());
            if (opened is not { } proof || !BiometricAccess.AccountMatches(proof.UserName, UserName))
            { Error = "Empreinte non reconnue pour ce compte."; return; }
            await api.LoginAsync(proof.UserName, proof.Secret);
            authenticated = true;
            await api.SendAsync<SessionDto>(HttpMethod.Post, "api/session/mot-de-passe",
                new { currentPassword = proof.Secret, newPassword = replacement });
            changed = true;
        }
        catch (ApiException ex) { Error = ex.Message; }
        catch (Exception ex) when (ex is OfflineException or HttpRequestException or TaskCanceledException)
        { Error = "Le serveur doit être accessible pour restaurer le mot de passe."; }
        finally
        {
            if (authenticated) { try { await api.LogoutAsync(); } catch (Exception) { } }
            if (changed)
            {
                bool reactivated = false;
                try
                {
                    if (Platform.EnrollBiometricAsync is { } enroll)
                        reactivated = await enroll(BiometricAccess.Seal(UserName, replacement));
                }
                catch (Exception) { }
                if (reactivated) BiometricAccess.Enable(api.Server, UserName);
                else { BiometricAccess.Disable(); Platform.DisableBiometricAccess?.Invoke(); }
                RefreshBiometrics();
                try
                {
                    var store = api.Offline ?? new OfflineStore(api.Server);
                    store.ForgetCredential();
                    store.RememberAccount(UserName);
                }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
                Password = string.Empty;
                ClearRecoverySecrets();
                RecoveryOpen = false;
                Error = null;
                Notice = reactivated
                    ? "Mot de passe restauré. Votre empreinte a aussi été mise à jour."
                    : "Mot de passe restauré. Connectez-vous avec le nouveau code puis réactivez l'empreinte.";
            }
            Busy = false;
        }
    }

    private string? notice;
    /// <summary>Information au-dessus du formulaire : l'ordinateur principal est injoignable, travail hors ligne possible.</summary>
    public string? Notice { get => notice; set { if (Set(ref notice, value)) Raise(nameof(HasNotice)); } }
    public bool HasNotice => !string.IsNullOrEmpty(notice);

    /// <summary>Nom de l'établissement, connu dès le démarrage du serveur : il titre l'écran de connexion.</summary>
    public string SchoolName { get => schoolName; set { if (Set(ref schoolName, value)) { Raise(nameof(TitleText)); } } }
    public string TitleText => string.IsNullOrWhiteSpace(schoolName) ? "SCOLIA" : schoolName;
    public bool Busy { get => busy; set { if (Set(ref busy, value)) { Raise(nameof(NotBusy)); Raise(nameof(CanType)); Raise(nameof(CanSubmitCredentials)); Raise(nameof(CanRecoverWithCode)); Raise(nameof(CanRecoverWithBiometrics)); } } }
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
        Raise(nameof(CanRecoverWithBiometrics));
        Raise(nameof(CanOfferEnrollment));
        Raise(nameof(ShowBiometricSignIn));
        Raise(nameof(ShowBiometricEnrollment));
        Raise(nameof(ShowRemoveBiometric));
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
        RecoveryOpen = false;
        ClearRecoverySecrets();
        ShowPassword = false;
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
    public bool CanType => !Locked && !Busy;
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
        Raise(nameof(Locked)); Raise(nameof(CanType)); Raise(nameof(CanSubmitCredentials));
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
        Raise(nameof(Locked)); Raise(nameof(CanType)); Raise(nameof(CanSubmitCredentials));
        Restored = true;
        Unlocked?.Invoke();
    }

    private async Task SubmitAsync()
    {
        if (Locked || Busy) return;
        if (string.IsNullOrWhiteSpace(UserName)) { Error = "Renseignez votre identifiant."; return; }
        if (Password.Length == 0) { Error = "Renseignez votre mot de passe."; return; }
        Busy = true;
        try { await SignInAsync(UserName.Trim(), Password, fromBiometrics: false); }
        finally { Busy = false; }
    }

    private bool SignInFromOfflineCache(string user, string secret, bool fromBiometrics)
    {
        var store = api.Offline ?? new Infrastructure.OfflineStore(api.Server);
        if (store.OfflineLogin(user, secret) is not { } saved)
        {
            Error = store.HasCredential
                ? "Ordinateur principal injoignable. Hors ligne, seul le dernier compte connecté sur cet appareil peut s'ouvrir, avec son code."
                : "Ordinateur principal injoignable. Une première connexion en ligne est nécessaire pour travailler ensuite hors ligne.";
            return false;
        }
        api.Offline = store;
        Authenticated?.Invoke(user, secret, saved);
        Password = string.Empty;
        Notice = "Mode hors ligne · données enregistrées sur cet appareil. Les opérations seront synchronisées au retour du serveur.";
        LoggedIn?.Invoke(saved);
        return true;
    }

    /// <summary>Connexion au serveur, au clavier ou après l'empreinte ; hors ligne, le dernier compte de l'appareil.</summary>
    private async Task SignInAsync(string user, string secret, bool fromBiometrics)
    {
        Error = null;
        Restored = false;

        // Après une coupure déjà constatée, vérifier immédiatement le compte à
        // partir de son empreinte locale ; ne pas attendre un nouveau délai HTTP.
        // Un mot de passe incorrect ne donne jamais accès au cache.
        if (!api.IsOnline && (api.Offline ?? new Infrastructure.OfflineStore(api.Server)) is { HasCredential: true })
        {
            SignInFromOfflineCache(user, secret, fromBiometrics);
            return;
        }

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
            // Le réseau est tombé pendant l'authentification : réutiliser la
            // même vérification locale, sans contourner le mot de passe enregistré.
            SignInFromOfflineCache(user, secret, fromBiometrics);
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
