using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Input;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Views;

public partial class LoginView : UserControl
{
    private LoginViewModel? subscribedLogin;

    /// <summary>
    /// Téléphone : la connexion prend le langage de la Configuration (Réglages d'iOS) — fond clair du thème, textes
    /// du thème, champs en blocs gris arrondis et actions bleues. L'ordinateur garde son fond bleu de Windows 11.
    /// Les pinceaux suivent le thème clair ou sombre sans recharger l'écran.
    /// </summary>
    private void ApplyPhoneLook()
    {
        Root.Classes.Add("ios");
        Backdrop.Background = ThemeBrush.Get("BgBrush");
        Halo.IsVisible = false;
        Resources["LoginInk"] = ThemeBrush.Get("TextBrush");
        Resources["LoginInk2"] = ThemeBrush.Get("MutedBrush");
        Resources["LoginInk3"] = ThemeBrush.Get("MutedBrush");
        Resources["LoginGlass"] = ThemeBrush.Get("GroupBrush");
        Resources["LoginGlassHover"] = ThemeBrush.Get("HoverBrush");
        Resources["LoginLink"] = ThemeBrush.Get("AccentBrush");
        // Logo dans une tuile arrondie, comme une icône d'application iOS, au lieu d'un disque blanc sur fond blanc.
        LogoTile.CornerRadius = new Avalonia.CornerRadius(24);
        LogoTile.Background = ThemeBrush.Get("GroupBrush");
        LogoTile.BoxShadow = Avalonia.Media.BoxShadows.Parse("0 4 14 0 #1F000000");
        SchoolHeading.FontWeight = Avalonia.Media.FontWeight.Bold;
    }

    public LoginView()
    {
        InitializeComponent();
        if (Preferences.MobileLayout) ApplyPhoneLook();
        PasswordBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || DataContext is not LoginViewModel login || !login.CanSubmitCredentials) return;
            login.Submit.Execute(null);
            e.Handled = true;
        };
        NewPasswordBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            ConfirmPasswordBox.Focus();
            e.Handled = true;
        };
        ConfirmPasswordBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            RecoveryCodeBox.Focus();
            e.Handled = true;
        };
        RecoveryCodeBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || DataContext is not LoginViewModel login || !login.CanRecoverWithCode) return;
            login.RecoverWithCode.Execute(null);
            e.Handled = true;
        };
        // Échap ramène à la session verrouillée quand on est venu de l'écran de veille.
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || DataContext is not LoginViewModel login || !login.CanResume) return;
            login.Resume.Execute(null);
            e.Handled = true;
        };
        // Sur le téléphone, laisser le formulaire visible : le clavier s'ouvre au premier toucher.
        AttachedToVisualTree += (_, _) =>
        {
            SubscribeLogin();
            Platform.AppResumed += RefreshOnResume;
            FocusRelevantField();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            Platform.AppResumed -= RefreshOnResume;
            ReleaseLogin();
        };
        DataContextChanged += (_, _) => SubscribeLogin();
    }

    private void SubscribeLogin()
    {
        ReleaseLogin();
        subscribedLogin = DataContext as LoginViewModel;
        if (subscribedLogin is not null)
        {
            subscribedLogin.Unlocked += OnUnlocked;
            subscribedLogin.PropertyChanged += OnLoginChanged;
            subscribedLogin.RefreshBiometrics();
        }
    }

    private void ReleaseLogin()
    {
        if (subscribedLogin is not null)
        {
            subscribedLogin.Unlocked -= OnUnlocked;
            subscribedLogin.PropertyChanged -= OnLoginChanged;
        }
        subscribedLogin = null;
    }

    private void RefreshOnResume() => subscribedLogin?.RefreshBiometrics();

    private void OnLoginChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(LoginViewModel.RecoveryOpen)
            or nameof(LoginViewModel.ShowManualCredentials)
            or nameof(LoginViewModel.HasRememberedAccount))
            Dispatcher.UIThread.Post(FocusRelevantField, DispatcherPriority.Background);
    }

    /// <summary>Respecte le compte déjà reconnu et n'ouvre pas le clavier mobile sans geste de l'utilisateur.</summary>
    private void FocusRelevantField()
    {
        if (Platform.IsMobile || PhoneLayout.Active || DataContext is not LoginViewModel login
            || !login.ShowManualCredentials) return;
        if (login.RecoveryOpen) NewPasswordBox.Focus();
        else if (login.ShowAccountEntry) UserNameBox.Focus();
        else PasswordBox.Focus();
    }

    private void OnUnlocked() => FocusRelevantField();
}
