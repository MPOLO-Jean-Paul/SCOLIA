using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Commande synchrone : un bouton, un clic, une action.</summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Commande avec paramètre : le bouton passe sa valeur, par exemple le son à écouter.</summary>
public sealed class RelayCommand<T>(Action<T?> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute(parameter is T value ? value : default);
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Commande vocale à deux états : contrairement aux commandes d'écriture,
/// elle reste cliquable pendant l'écoute pour permettre le deuxième clic « Arrêter ».
/// ExecuteAsync est conservée pour les tests et les parcours de capture de l'interface.
/// </summary>
public sealed class ToggleVoiceCommand(Func<Task> toggle) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public async void Execute(object? parameter) => await ExecuteAsync();
    public Task ExecuteAsync() => toggle();
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Commande asynchrone : désactivée pendant son exécution, pour éviter les doubles envois.</summary>
public sealed class AsyncCommand(Func<Task> execute) : ICommand
{
    private bool running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !running;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public async void Execute(object? parameter) => await ExecuteAsync();

    /// <summary>Même commande, attendue jusqu'au bout (essais et enchaînements).</summary>
    public async Task ExecuteAsync()
    {
        if (running) return;
        running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await execute(); }
        finally
        {
            running = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
