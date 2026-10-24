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

    public LoginViewModel(ScoliaClient api)
    {
        this.api = api;
        Submit = new AsyncCommand(SubmitAsync);
    }

    public event Action<SessionDto>? LoggedIn;

    public string UserName { get => userName; set { if (Set(ref userName, value)) Submit.Refresh(); } }
    public string Password { get => password; set { if (Set(ref password, value)) Submit.Refresh(); } }
    public string? Error { get => error; set { if (Set(ref error, value)) Raise(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(error);
    public bool Busy { get => busy; set => Set(ref busy, value); }
    public AsyncCommand Submit { get; }

    public void Reset()
    {
        Password = string.Empty;
        Error = null;
    }

    private async Task SubmitAsync()
    {
        Error = null;
        Busy = true;
        try
        {
            var session = await api.LoginAsync(UserName.Trim(), Password);
            Password = string.Empty;
            LoggedIn?.Invoke(session);
        }
        catch (ApiException ex) { Error = ex.Message; }
        catch (HttpRequestException) { Error = "Le serveur SCOLIA ne répond pas. Vérifiez qu'il est lancé sur ce poste."; }
        finally { Busy = false; }
    }
}
