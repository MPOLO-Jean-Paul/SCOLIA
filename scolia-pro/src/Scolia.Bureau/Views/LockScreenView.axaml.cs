using Avalonia.Controls;
using Avalonia.Input;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Views;

/// <summary>Écran de veille : Entrée déverrouille ; sur l'ordinateur, le curseur est déjà dans le champ du code.</summary>
public partial class LockScreenView : UserControl
{
    public LockScreenView()
    {
        InitializeComponent();
        // Même reprise de session iOS sur le téléphone et sur l'ordinateur.
        var ios = AuthLayout.UseIosStyle;
        ClassicLockScreen.IsVisible = !ios;
        IosLockScroll.IsVisible = ios;
        AuthLayout.Track(IosLockScroll);
        IosLockCode.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || DataContext is not LockScreenViewModel vm) return;
            if (vm.Unlock.CanExecute(null)) vm.Unlock.Execute(null);
            e.Handled = true;
        };
        LockCode.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || DataContext is not LockScreenViewModel lockScreen) return;
            if (lockScreen.Unlock.CanExecute(null)) lockScreen.Unlock.Execute(null);
            e.Handled = true;
        };
    }

    /// <summary>Sur l'ordinateur, le curseur va dans le champ du code dès que l'écran de veille apparaît.</summary>
    public void FocusCode()
    {
        if (!Platform.IsMobile) (AuthLayout.UseIosStyle ? IosLockCode : LockCode).Focus();
    }
}
