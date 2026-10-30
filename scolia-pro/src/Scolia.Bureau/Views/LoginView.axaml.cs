using Avalonia.Controls;
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
        PasswordBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && DataContext is LoginViewModel login && login.Submit.CanExecute(null)) login.Submit.Execute(null);
        };
        // Sur le téléphone, laisser le formulaire visible : le clavier s'ouvre au premier toucher.
        AttachedToVisualTree += (_, _) =>
        {
            SubscribeLogin();
            if (!Platform.IsMobile && !PhoneLayout.Active) UserNameBox.Focus();
        };
        DetachedFromVisualTree += (_, _) => ReleaseLogin();
        DataContextChanged += (_, _) => SubscribeLogin();
    }

    private void SubscribeLogin()
    {
        ReleaseLogin();
        subscribedLogin = DataContext as LoginViewModel;
        if (subscribedLogin is not null)
        {
            subscribedLogin.Unlocked += OnUnlocked;
            subscribedLogin.RefreshBiometrics();
        }
    }

    private void ReleaseLogin()
    {
        if (subscribedLogin is not null) subscribedLogin.Unlocked -= OnUnlocked;
        subscribedLogin = null;
    }

    private void OnUnlocked()
    {
        if (!Platform.IsMobile && !PhoneLayout.Active) PasswordBox.Focus();
    }
}
