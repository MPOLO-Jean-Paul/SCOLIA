using Avalonia.Controls;
using Avalonia.Input;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
        PasswordBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && DataContext is LoginViewModel login && login.Submit.CanExecute(null)) login.Submit.Execute(null);
        };
        UserNameBox.Focus();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is LoginViewModel login) login.Unlocked += () => PasswordBox.Focus();
        };
    }
}
