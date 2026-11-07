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


    public LoginView()
    {
        InitializeComponent();
        // Les écrans Android disposent de leur propre hiérarchie iOS ; la page Windows reste intacte.
        var mobile = Platform.IsMobile;
        LoginScroll.IsVisible = !mobile;
        IosLoginScroll.IsVisible = mobile;
        IosLoginThemeScope.IsVisible = mobile;
        Backdrop.IsVisible = !mobile;
        Halo.IsVisible = !mobile;
        IosBackdrop.IsVisible = mobile;
        // Une seule colonne, avec marges et titre adaptés aux écrans très étroits.
        IosLoginScroll.SizeChanged += (_, e) =>
            IosLoginScroll.Classes.Set("compact", e.NewSize.Width < 390);
        IosPasswordBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || DataContext is not LoginViewModel login || !login.CanSubmitCredentials) return;
            login.Submit.Execute(null);
            e.Handled = true;
        };
        IosNewPasswordBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { IosConfirmPasswordBox.Focus(); e.Handled = true; }
        };
        IosConfirmPasswordBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { IosRecoveryCodeBox.Focus(); e.Handled = true; }
        };
        IosRecoveryCodeBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || DataContext is not LoginViewModel login || !login.CanRecoverWithCode) return;
            login.RecoverWithCode.Execute(null);
            e.Handled = true;
        };
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
