using System.Text.Json;
using Avalonia.Threading;

namespace JPServices.Owner;

public sealed partial class OwnerShell
{
    private CancellationTokenSource? _readCancellation;
    private int _readRevision;
    private bool _writing;

    private static bool Same(JsonElement? before, JsonElement after) =>
        before is { } value && value.GetRawText() == after.GetRawText();

    /// <summary>Une navigation remplace la lecture en cours ; une réponse ancienne ne peut remplir la nouvelle page.</summary>
    private async Task LoadAsync(Page page, bool force = false)
    {
        var revision = ++_readRevision;
        _readCancellation?.Cancel();
        _readCancellation = null;
        _loading = false;
        if (Api() is not { } api || page is Page.Account or Page.Menu)
        {
            ShowStatus();
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _readCancellation = cancellation;
        var ct = cancellation.Token;
        var schoolId = _selectedSchool;
        _loading = true;
        _error = null;
        ShowStatus();
        try
        {
            JsonElement? summary = null, schools = null, school = null, servers = null, relays = null, audit = null,
                storage = null, turn = null, netlify = null;
            switch (page)
            {
                case Page.Home or Page.Backups:
                    if (force || _summary is null) summary = await api.SummaryAsync(ct);
                    if (page == Page.Backups && (force || _schools is null)) schools = await api.SchoolsAsync(ct);
                    break;
                case Page.Schools or Page.Licenses:
                    if (force || _schools is null) schools = await api.SchoolsAsync(ct);
                    break;
                case Page.SchoolDetail when schoolId is not null:
                    school = await api.SchoolAsync(schoolId, ct);
                    break;
                case Page.Servers:
                    if (force || _servers is null) servers = await api.ServersAsync(ct);
                    break;
                case Page.Relays:
                    if (force || _relays is null) relays = await api.RelaysAsync(ct);
                    break;
                case Page.Activity:
                    if (force || _audit is null) audit = await api.AuditAsync(ct);
                    break;
                case Page.Storage:
                    if (force || _storage is null) storage = await api.StorageAsync(ct);
                    break;
                case Page.Turn:
                    if (force || _turn is null) turn = await api.DiscoverTurnAsync(ct);
                    break;
                case Page.Netlify:
                    if (force || _netlify is null) netlify = await api.NetlifyAsync(ct);
                    break;
            }
            ct.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (revision != _readRevision || !ReferenceEquals(_api, api)) return;
                var changed = false;
                if (summary is { } a) { changed |= !Same(_summary, a); _summary = a; }
                if (schools is { } b) { changed |= !Same(_schools, b); _schools = b; }
                if (school is { } c) { changed |= !Same(_school, c); _school = c; }
                if (servers is { } d) { changed |= !Same(_servers, d); _servers = d; }
                if (relays is { } e) { changed |= !Same(_relays, e); _relays = e; }
                if (audit is { } f) { changed |= !Same(_audit, f); _audit = f; }
                if (storage is { } g) { changed |= !Same(_storage, g); _storage = g; }
                if (turn is { } h) { changed |= !Same(_turn, h); _turn = h; }
                if (netlify is { } i) { changed |= !Same(_netlify, i); _netlify = i; }
                if (summary is not null || schools is not null || school is not null || servers is not null || relays is not null || audit is not null || storage is not null || turn is not null || netlify is not null)
                    _loadedAt = DateTimeOffset.Now;
                _loading = false;
                if (changed) ShowPage(preserveScroll: true);
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception error)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (revision == _readRevision)
                    _error = error is OwnerApiException ? error.Message : "Lecture impossible. Réessayez.";
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (revision != _readRevision) return;
                _readCancellation = null;
                _loading = false;
                ShowStatus();
            });
        }
    }

    /// <summary>Une seule écriture à la fois. Son refus reste visible et ne déclenche jamais un faux succès.</summary>
    private async Task RunAsync(Func<OwnerApi, Task<JsonElement>> action, Page refresh, string? success = null)
    {
        if (Api() is not { } api || _writing) return;
        _writing = true;
        _error = null;
        _notice = null;
        ShowStatus();
        try
        {
            var reply = await action(api);
            var notice = OwnerText.Str(reply, "notice");
            _notice = success ?? (notice.Length > 0 ? notice : "Opération enregistrée.");
            _summary = _schools = _servers = _audit = _relays = null;
            _storage = _turn = _netlify = null;
            // Ne pas déplacer l'administrateur s'il a changé de page pendant l'envoi.
            await LoadAsync(_page, force: true);
        }
        catch (OwnerApiException ex) { _error = ex.Message; _notice = null; }
        catch (Exception) { _error = "Opération impossible. Réessayez."; _notice = null; }
        finally
        {
            _writing = false;
            ShowStatus();
        }
    }

    private void ResetData()
    {
        ++_readRevision;
        _readCancellation?.Cancel();
        _readCancellation = null;
        _loading = false;
        _api?.Dispose();
        _api = null;
        _summary = _schools = _servers = _relays = _audit = _school = null;
        _storage = _turn = _netlify = null;
        _selectedSchool = null;
        _drafts.Clear();
    }

    private async Task CreateInvitationAsync(Func<OwnerApi, Task<JsonElement>> create)
    {
        if (Api() is not { } api || _writing) return;
        _writing = true;
        _error = null;
        _notice = null;
        ShowStatus();
        try
        {
            var reply = await create(api);
            _invitation = (OwnerText.Str(reply, "invite_code"), OwnerText.Str(reply, "school_name"), OwnerText.Number(reply, "expires_at"));
            _notice = OwnerText.Str(reply, "notice");
            _audit = null;
            _drafts.Remove("invitation-name");
            ShowPage(preserveScroll: true);
        }
        catch (OwnerApiException ex) { _error = ex.Message; }
        catch (Exception) { _error = "Invitation impossible. Réessayez."; }
        finally { _writing = false; ShowStatus(); }
    }

    /// <summary>Le formulaire reste en place pendant la vérification et en cas de refus.</summary>
    private async Task SaveCredentialsAsync(OwnerCredentials candidate)
    {
        if (_writing) return;
        _notice = null;
        if (!candidate.IsComplete)
        {
            _error = "Les trois champs sont obligatoires (identifiant, secret et jeton propriétaire).";
            ShowStatus();
            return;
        }
        _writing = true;
        _error = null;
        ShowStatus();
        try
        {
            using var probe = ApiFactory(candidate);
            var summary = await probe.SummaryAsync();
            if (_vault is not { CanStore: true }) throw new OwnerApiException("Le coffre sécurisé de cet appareil est indisponible.");
            _vault.Save(candidate);
            ResetData();
            _credentials = candidate;
            _summary = summary;
            _loadedAt = DateTimeOffset.Now;
            _notice = "Accès vérifié et enregistré de façon chiffrée sur cet appareil.";
            ShowPage(preserveScroll: true);
        }
        catch (OwnerApiException ex) { _error = ex.Message; }
        catch (Exception) { _error = "Vérification impossible. Réessayez."; }
        finally { _writing = false; ShowStatus(); }
    }

    private async Task TestConnectionAsync()
    {
        if (Api() is not { } api || _writing) return;
        _writing = true;
        _notice = null;
        _error = null;
        ShowStatus();
        try
        {
            _summary = await api.SummaryAsync();
            _notice = "Connexion vérifiée : accès propriétaire valide.";
        }
        catch (OwnerApiException ex) { _error = ex.Message; }
        catch (Exception) { _error = "Connexion impossible. Réessayez."; }
        finally { _writing = false; ShowStatus(); }
    }
}
