using System.Text.Json;
using Avalonia.Controls;

namespace JPServices.Owner;

public sealed partial class OwnerShell
{
    private JsonElement? _storage, _turn, _netlify;

    private void ClearSavedDrafts(IReadOnlyDictionary<string, string> sent)
    {
        // Une saisie faite pendant l'envoi reste disponible pour la prochaine modification.
        foreach (var (key, value) in sent)
            if (_drafts.TryGetValue(key, out var current) && current == value) _drafts.Remove(key);
    }

    private Control StoragePage()
    {
        var stack = PageContainer();
        stack.Children.Add(PageTitle("Stockage cloud", "Vérifier et configurer les sauvegardes chiffrées"));
        var data = _storage ?? default;
        var ready = OwnerText.Flag(data, "ready");
        var mode = OwnerText.Str(data, "mode");
        var status = new StackPanel { Spacing = 8 };
        status.Children.Add(Text(ready ? "Stockage vérifié et disponible" : "Stockage à configurer ou à vérifier", 18, true,
            ready ? OwnerPalette.Teal : OwnerPalette.Amber));
        status.Children.Add(Text(OwnerText.Str(data, "notice"), 13, color: OwnerPalette.Muted));
        if (OwnerText.Str(data, "error") is { Length: > 0 } error)
            status.Children.Add(Text(error, 13, color: OwnerPalette.Red));
        if (OwnerText.Flag(data, "unreadable"))
            status.Children.Add(Text("Les identifiants enregistrés doivent être saisis à nouveau.", 13, color: OwnerPalette.Red));
        status.Children.Add(Button("Vérifier le stockage", () => _ = LoadAsync(Page.Storage, force: true), subtle: true));
        stack.Children.Add(Box(status));

        if (mode != "binding")
        {
            var form = new StackPanel { Spacing = 10 };
            form.Children.Add(Text("Identifiants Cloudflare R2", 18, true));
            form.Children.Add(Text("Le service vérifie l'écriture, la lecture et la suppression d'un fichier témoin avant d'enregistrer les identifiants chiffrés.", 13, color: OwnerPalette.Muted));
            form.Children.Add(Text("Compte Cloudflare", 12, true));
            var account = Field("r2-account", "Compte Cloudflare R2", OwnerText.Str(data, "account_id"), "Identifiant du compte (32 caractères)");
            account.MaxLength = 32;
            form.Children.Add(account);
            form.Children.Add(Text("Nom du bucket", 12, true));
            var bucket = Field("r2-bucket", "Nom du bucket R2", OwnerText.Str(data, "bucket"), "Ex. scolia-pro-backups");
            bucket.MaxLength = 63;
            form.Children.Add(bucket);
            form.Children.Add(Text("Clé d'accès", 12, true));
            var access = Field("r2-access", "Clé d'accès R2", hint: "Identifiant de la clé R2");
            access.PasswordChar = '●';
            access.MaxLength = 128;
            form.Children.Add(access);
            form.Children.Add(Text("Clé secrète", 12, true));
            var secret = Field("r2-secret", "Clé secrète R2", hint: "Secret de la clé R2");
            secret.PasswordChar = '●';
            secret.MaxLength = 256;
            form.Children.Add(secret);
            form.Children.Add(Confirm("Enregistrer et vérifier le stockage", "Confirmer la vérification et l'enregistrement", () =>
            {
                var sent = new Dictionary<string, string>
                {
                    ["r2-account"] = account.Text ?? "", ["r2-bucket"] = bucket.Text ?? "",
                    ["r2-access"] = access.Text ?? "", ["r2-secret"] = secret.Text ?? ""
                };
                return RunAsync(async api =>
                {
                    var reply = await api.ConfigureStorageAsync(sent["r2-account"], sent["r2-bucket"], sent["r2-access"], sent["r2-secret"]);
                    ClearSavedDrafts(sent);
                    return reply;
                }, Page.Storage);
            }));
            stack.Children.Add(Box(form));
        }
        if (OwnerText.Str(data, "account_id").Length > 0)
        {
            var removal = new StackPanel { Spacing = 8 };
            removal.Children.Add(Text("Retirer les identifiants ajoutés", 16, true));
            removal.Children.Add(Text("Les sauvegardes existantes restent dans le bucket. La liaison R2 du déploiement, si elle est présente, reste disponible.", 13, color: OwnerPalette.Muted));
            removal.Children.Add(Confirm("Retirer les identifiants R2", "Confirmer le retrait des identifiants", () =>
                RunAsync(api => api.RemoveStorageAsync(), Page.Storage), danger: true));
            stack.Children.Add(Box(removal));
        }
        var proposal = new StackPanel { Spacing = 8 };
        proposal.Children.Add(Text("Sauvegarde des établissements", 18, true));
        proposal.Children.Add(Text("Proposer la sauvegarde en ligne à tous les établissements. Chaque serveur appliquera ce réglage lors de son prochain contrôle.", 13, color: OwnerPalette.Muted));
        var request = Confirm("Proposer la sauvegarde à toutes les écoles", "Confirmer la proposition à toutes les écoles", () =>
            RunAsync(api => api.RequestCloudForAllAsync(), Page.Storage));
        request.IsEnabled = ready;
        proposal.Children.Add(request);
        if (!ready) proposal.Children.Add(Text("Vérifiez d'abord le stockage pour activer cette commande.", 12, color: OwnerPalette.Muted));
        stack.Children.Add(Box(proposal));
        return stack;
    }

    private Control TurnPage()
    {
        var stack = PageContainer();
        stack.Children.Add(PageTitle("Découverte TURN", "Catalogue Cloudflare Realtime"));
        var data = _turn ?? default;
        var state = OwnerText.Str(data, "status");
        var intro = new StackPanel { Spacing = 8 };
        intro.Children.Add(Text(state == "available" ? "Catalogue disponible" : "Catalogue à vérifier", 18, true,
            state == "available" ? OwnerPalette.Teal : OwnerPalette.Amber));
        intro.Children.Add(Text(OwnerText.Str(data, "explanation"), 13, color: OwnerPalette.Muted));
        intro.Children.Add(Text("Les relais TURN accompagnent les communications WebRTC. Les connexions actuelles des écoles utilisent le catalogue WSS et leur accord de connexion.", 13, color: OwnerPalette.Muted));
        intro.Children.Add(Button("Actualiser le catalogue TURN", () => _ = LoadAsync(Page.Turn, force: true), subtle: true));
        stack.Children.Add(Box(intro));
        var relays = OwnerText.Items(_turn, "relays");
        if (relays.Count == 0 && !_loading) stack.Children.Add(Empty("Aucun relais TURN disponible dans ce catalogue."));
        foreach (var relay in relays)
            stack.Children.Add(Row(OwnerText.Str(relay, "label"), OwnerText.Str(relay, "provider"), ("Répertorié", LicenseTone.Info)));
        return stack;
    }

    private Control NetlifyPage()
    {
        var stack = PageContainer();
        stack.Children.Add(PageTitle("Secours HTTPS", "Configuration du site Netlify"));
        var data = _netlify ?? default;
        var info = new StackPanel { Spacing = 8 };
        info.Children.Add(Text("Pilote désactivé", 18, true, OwnerPalette.Amber));
        info.Children.Add(Text(OwnerText.Str(data, "notice"), 13, color: OwnerPalette.Muted));
        info.Children.Add(Text("Ce réglage permet d'enregistrer le site et de vérifier sa disponibilité. L'activation du transport reste soumise à sa validation et à l'accord des établissements.", 13, color: OwnerPalette.Muted));
        stack.Children.Add(Box(info));
        var form = new StackPanel { Spacing = 10 };
        form.Children.Add(Text("Nom du site", 12, true));
        var label = Field("netlify-label", "Nom du secours HTTPS", OwnerText.Str(data, "name"), "Ex. Secours JP-Services");
        label.MaxLength = 90;
        form.Children.Add(label);
        form.Children.Add(Text("Adresse HTTPS", 12, true));
        var address = Field("netlify-address", "Adresse du site Netlify", OwnerText.Str(data, "base_url"), "https://votre-site.netlify.app/");
        address.MaxLength = 210;
        form.Children.Add(address);
        form.Children.Add(Confirm("Enregistrer le site de secours", "Confirmer : conserver le pilote désactivé", () =>
        {
            var sent = new Dictionary<string, string> { ["netlify-label"] = label.Text ?? "", ["netlify-address"] = address.Text ?? "" };
            return RunAsync(async api =>
            {
                var reply = await api.ConfigureNetlifyAsync(sent["netlify-label"], sent["netlify-address"]);
                ClearSavedDrafts(sent);
                return reply;
            }, Page.Netlify);
        }));
        if (OwnerText.Flag(data, "configured"))
        {
            form.Children.Add(Button("Tester le site de secours", () => _ = RunAsync(api => api.TestNetlifyAsync(), Page.Netlify), subtle: true));
            form.Children.Add(Confirm("Retirer le site de secours", "Confirmer le retrait du site", () =>
                RunAsync(api => api.RemoveNetlifyAsync(), Page.Netlify), danger: true));
        }
        stack.Children.Add(Box(form));
        return stack;
    }
}
