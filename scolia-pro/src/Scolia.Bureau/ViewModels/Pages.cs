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

    private Avalonia.Media.Imaging.Bitmap? logo;

    /// <summary>Logo de l'établissement ; le logo de SCOLIA s'affiche tant qu'il n'est pas chargé.</summary>
    public Avalonia.Media.Imaging.Bitmap? Logo { get => logo; set { if (Set(ref logo, value)) Raise(nameof(HasLogo)); } }
    public bool HasLogo => logo is not null;

    public void Reset()
    {
        Password = string.Empty;
        Error = null;
        Restored = false;
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
