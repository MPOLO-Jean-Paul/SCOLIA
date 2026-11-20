using Avalonia.Media;
using Material.Icons;
using System.Collections.ObjectModel;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Nature d'un champ de formulaire. Chaque nature a son contrôle dans la boîte de dialogue.</summary>
public enum FieldKind { Text, Secret, Multiline, Amount, Day, Choice, Flag, Phone, Header }

/// <summary>Champ d'un formulaire : sa valeur, son libellé et son erreur éventuelle.</summary>
public sealed class FormField : Observable
{
    private string text = string.Empty;
    private DateTime? day;
    private ChoiceItem? choice;
    private bool flag;
    private string? error;
    private IReadOnlyList<ChoiceItem> options = [];

    public FormField(string key, string label, FieldKind kind, bool required, string? hint)
    {
        Key = key;
        Label = Vocabulary.Adapt(label);
        Kind = kind;
        Required = required;
        this.hint = hint is null ? null : Vocabulary.Adapt(hint);
    }

    private string? hint;
    private IReadOnlyList<string> suggestions = [];
    private RelayCommand<string>? pick;

    public string Key { get; }
    public string Label { get; }
    public FieldKind Kind { get; }
    public bool Required { get; }
    /// <summary>Aide sous le champ ; elle peut suivre la saisie (exemple de retard, période calculée).</summary>
    public string? Hint { get => hint; set { if (Set(ref hint, value)) Raise(nameof(HasHint)); } }

    // ---------- Prédiction : propositions sous le champ, comme la barre de suggestions du clavier ----------
    /// <summary>Calcule les propositions d'après ce champ et les autres champs du formulaire.</summary>
    public Func<FormField, IReadOnlyList<string>>? Suggest { get; set; }
    /// <summary>Après un choix : les champs liés se remplissent (code, niveau, promotion suivante…).</summary>
    public Action<FormField, string>? Picked { get; set; }
    public IReadOnlyList<string> Suggestions { get => suggestions; private set { if (Set(ref suggestions, value)) Raise(nameof(HasSuggestions)); } }
    public bool HasSuggestions => suggestions.Count > 0;
    public RelayCommand<string> PickSuggestion => pick ??= new RelayCommand<string>(value =>
    {
        if (string.IsNullOrEmpty(value)) return;
        Text = value;
        Picked?.Invoke(this, value);
        RefreshSuggestions();
    });
    public void RefreshSuggestions()
    {
        if (Suggest is null) return;
        IReadOnlyList<string> next;
        try { next = Suggest(this); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException) { next = []; }
        if (!next.SequenceEqual(suggestions)) Suggestions = next;
    }

    /// <summary>Un téléphone ne garde que ses 10 chiffres, même si la valeur vient d'un ancien dossier.</summary>
    public string Text { get => text; set { if (Set(ref text, Kind == FieldKind.Phone ? PhoneInput.Clean(value) : value)) Error = null; } }
    public DateTime? Day { get => day; set { if (Set(ref day, value)) Error = null; } }
    public ChoiceItem? Choice { get => choice; set { if (Set(ref choice, value)) Error = null; } }
    public IReadOnlyList<ChoiceItem> Options { get => options; set => Set(ref options, value); }
    public bool Flag { get => flag; set => Set(ref flag, value); }
    public string? Error { get => error; set { if (Set(ref error, value)) Raise(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(error);
    public bool HasHint => !string.IsNullOrWhiteSpace(Hint);

    public bool IsText => Kind is FieldKind.Text or FieldKind.Amount;
    public bool IsPhone => Kind == FieldKind.Phone;
    public bool IsSecret => Kind == FieldKind.Secret;

    // Visible only on the user's request; secret fields remain masked by default.
    private bool showSecret;
    private RelayCommand? toggleSecret;
    public bool ShowSecret { get => showSecret; set { if (Set(ref showSecret, value)) Raise(nameof(SecretEyeIcon)); } }
    public MaterialIconKind SecretEyeIcon => ShowSecret ? MaterialIconKind.EyeOffOutline : MaterialIconKind.EyeOutline;
    public RelayCommand ToggleSecret => toggleSecret ??= new RelayCommand(() => ShowSecret = !ShowSecret);
    public bool IsMultiline => Kind == FieldKind.Multiline;
    public bool IsDay => Kind == FieldKind.Day;
    public bool IsChoice => Kind == FieldKind.Choice;
    public bool IsFlag => Kind == FieldKind.Flag;
    /// <summary>Titre de groupe : il range les champs d'un long formulaire, sans valeur à saisir.</summary>
    public bool IsHeader => Kind == FieldKind.Header;
    public bool IsInput => Kind is not (FieldKind.Flag or FieldKind.Header);
    public string Caption => Required ? $"{Label} *" : Label;

    public bool IsEmpty => Kind switch
    {
        FieldKind.Day => Day is null,
        FieldKind.Choice => Choice is null,
        FieldKind.Flag or FieldKind.Header => false,
        _ => string.IsNullOrWhiteSpace(Text),
    };
}

/// <summary>Boîte de dialogue modale : titre, champs, validation et actions. Renvoie vrai si l'utilisateur a validé.</summary>
public sealed class FormViewModel : Observable
{
    private readonly Func<FormViewModel, Task<bool>>? onSubmit;
    private readonly TaskCompletionSource<bool> done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? error;
    private bool busy;

    public FormViewModel(string title, string subtitle = "", Func<FormViewModel, Task<bool>>? onSubmit = null)
    {
        // Vocabulaire du profil : « élève » dans une école, « étudiant » dans une université.
        Title = Vocabulary.Adapt(title);
        Subtitle = Vocabulary.Adapt(subtitle);
        this.onSubmit = onSubmit;
        Submit = new AsyncCommand(SubmitAsync);
        Cancel = new RelayCommand(() => { if (!Busy) Close(false); });
        Fields.CollectionChanged += (_, e) =>
        {
            foreach (FormField field in e.NewItems ?? Array.Empty<FormField>())
                field.PropertyChanged += OnFieldChanged;
        };
    }

    private bool reacting;

    /// <summary>
    /// Une valeur change : les champs liés réagissent (<see cref="Changed"/>), puis chaque champ prédictif recalcule
    /// ses propositions. Une seule passe à la fois, pour qu'un remplissage automatique ne relance pas tout.
    /// </summary>
    private void OnFieldChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (reacting || sender is not FormField field || e.PropertyName is not (nameof(FormField.Text) or nameof(FormField.Choice) or nameof(FormField.Day) or nameof(FormField.Flag)))
            return;
        reacting = true;
        try
        {
            Changed?.Invoke(field);
            foreach (var other in Fields) other.RefreshSuggestions();
        }
        finally { reacting = false; }
    }

    /// <summary>Réaction d'un formulaire à la saisie : remplir un champ lié, recalculer une aide.</summary>
    public Action<FormField>? Changed { get; set; }

    /// <summary>Premier calcul des propositions, avant toute saisie.</summary>
    public void RefreshSuggestions()
    {
        foreach (var field in Fields) field.RefreshSuggestions();
    }

    public string Title { get; }
    public string Subtitle { get; }

    /// <summary>Icône de la feuille, choisie d'après sa fonction : la même famille d'icônes que le volet latéral.</summary>
    public MaterialIconKind HeaderIcon => Header.Icon;
    public IBrush HeaderBrush => ThemeBrush.Get(Danger ? "ToneRed" : Header.Tone);

    private (MaterialIconKind Icon, string Tone) Header => header ??= IconFor(Title, Danger);
    private (MaterialIconKind Icon, string Tone)? header;

    /// <summary>Correspondance entre le sujet d'un formulaire et son icône ; le premier mot-clé trouvé l'emporte.</summary>
    private static readonly (string Key, MaterialIconKind Icon, string Tone)[] Topics =
    [
        ("mot de passe", MaterialIconKind.Key, "ToneOrange"),
        ("reçu", MaterialIconKind.ReceiptText, "ToneOrange"),
        ("dépense", MaterialIconKind.CashMinus, "ToneRed"),
        ("caisse", MaterialIconKind.CashRegister, "ToneGreen"),
        ("salaire", MaterialIconKind.Cash, "ToneGreen"),
        ("prise en charge", MaterialIconKind.ShieldCheck, "TonePink"),
        ("inscription", MaterialIconKind.AccountPlus, "ToneBlue"),
        ("apprenant", MaterialIconKind.AccountEdit, "ToneBlue"),
        ("statut", MaterialIconKind.AccountSwitch, "ToneIndigo"),
        ("promotion", MaterialIconKind.School, "ToneIndigo"),
        ("section", MaterialIconKind.ViewGrid, "ToneIndigo"),
        ("année", MaterialIconKind.Calendar, "ToneRed"),
        ("évaluation", MaterialIconKind.ClipboardEdit, "TonePurple"),
        ("bulletin", MaterialIconKind.Certificate, "TonePurple"),
        ("cours", MaterialIconKind.BookOpenVariant, "TonePurple"),
        ("attribution", MaterialIconKind.AccountSchool, "ToneOrange"),
        ("décision", MaterialIconKind.Gavel, "ToneRed"),
        ("parent", MaterialIconKind.AccountCheck, "ToneTeal"),
        ("absence", MaterialIconKind.ClipboardCheck, "ToneTeal"),
        ("membre", MaterialIconKind.Briefcase, "ToneOrange"),
        ("utilisateur", MaterialIconKind.AccountCog, "ToneBlue"),
        ("compte", MaterialIconKind.AccountCog, "ToneBlue"),
        ("carte", MaterialIconKind.CardAccountDetails, "ToneBlue"),
        ("établissement", MaterialIconKind.OfficeBuilding, "ToneNeutral"),
        ("préférences", MaterialIconKind.TuneVariant, "TonePurple"),
        ("raccourcis", MaterialIconKind.Keyboard, "ToneNeutral"),
        ("sauvegarde", MaterialIconKind.DatabaseExport, "ToneGreen"),
        ("restaur", MaterialIconKind.BackupRestore, "ToneGreen"),
        ("effacer", MaterialIconKind.DeleteSweep, "ToneRed"),
        ("import", MaterialIconKind.Upload, "ToneGreen"),
        ("réseau", MaterialIconKind.LanConnect, "ToneGreen"),
        ("scolia", MaterialIconKind.Information, "ToneBlue"),
    ];

    private static (MaterialIconKind, string) IconFor(string title, bool danger)
    {
        var text = title.ToLowerInvariant();
        foreach (var (key, icon, tone) in Topics)
            if (text.Contains(key, StringComparison.Ordinal)) return (icon, tone);
        return danger ? (MaterialIconKind.AlertCircle, "ToneRed") : (MaterialIconKind.Information, "ToneNeutral");
    }
    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);
    /// <summary>Texte libre affiché sous le titre, par exemple le détail d'une confirmation.</summary>
    private string message = string.Empty;
    public string Message
    {
        get => message;
        set { if (Set(ref message, Vocabulary.Adapt(value))) Raise(nameof(HasMessage)); }
    }
    public string SubmitText { get; init; } = "Enregistrer";
    public string CancelText { get; init; } = "Annuler";
    /// <summary>Formulaire posé dans une page : pas d'en-tête ni d'ombre, et les champs défilent avec la page.</summary>
    public bool Embedded { get; init; }
    public double FieldsMaxHeight => Embedded ? double.PositiveInfinity : 460;
    /// <summary>Faux pour un formulaire intégré à une page, qui n'a pas de bouton d'annulation.</summary>
    public bool ShowCancel { get; init; } = true;
    /// <summary>Action destructive : le bouton de validation prend la couleur d'alerte.</summary>
    public bool Danger { get; init; }
    public bool CanSubmit => onSubmit is not null;
    public bool HasMessage => !string.IsNullOrEmpty(Message);
    public ObservableCollection<FormField> Fields { get; } = [];

    public string? Error { get => error; private set { if (Set(ref error, value)) Raise(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(error);
    public bool Busy { get => busy; private set => Set(ref busy, value); }
    public AsyncCommand Submit { get; }
    public RelayCommand Cancel { get; }

    /// <summary>Se termine quand la boîte se ferme.</summary>
    public Task<bool> Completion => done.Task;

    public void Close(bool result) => done.TrySetResult(result);

    /// <summary>Affiche un message d'erreur sans fermer la boîte : l'utilisateur corrige et valide à nouveau.</summary>
    public void Fail(string message) => Error = message;

    public FormField Add(string key, string label, FieldKind kind = FieldKind.Text, string? value = null, bool required = false, string? hint = null)
    {
        var field = new FormField(key, label, kind, required, hint) { Text = value ?? string.Empty };
        Fields.Add(field);
        return field;
    }

    public FormField AddDay(string key, string label, DateOnly? value, bool required = false)
    {
        var field = new FormField(key, label, FieldKind.Day, required, null) { Day = value is { } d ? d.ToDateTime(TimeOnly.MinValue) : null };
        Fields.Add(field);
        return field;
    }

    public FormField AddChoice(string key, string label, IReadOnlyList<ChoiceItem> options, string? value = null, bool required = false)
    {
        var field = new FormField(key, label, FieldKind.Choice, required, null) { Options = options };
        field.Choice = options.FirstOrDefault(o => o.Value == value)
            ?? (required && string.IsNullOrWhiteSpace(value) && options.Count == 1 ? options[0] : null);
        Fields.Add(field);
        return field;
    }

    /// <summary>Titre de groupe, avec une phrase d'explication facultative.</summary>
    public FormField AddHeader(string label, string? hint = null) => Add($"titre-{Fields.Count}", label, FieldKind.Header, hint: hint);

    public FormField AddFlag(string key, string label, bool value) =>
        Track(new FormField(key, label, FieldKind.Flag, false, null) { Flag = value });

    private FormField Track(FormField field)
    {
        Fields.Add(field);
        return field;
    }

    public FormField Find(string key) => Fields.First(f => f.Key == key);

    public bool Has(string key) => Fields.Any(f => f.Key == key);

    public string Text(string key) => Find(key).Text.Trim();

    public string? OptionalText(string key) => Text(key) is { Length: > 0 } t ? t : null;

    public decimal Amount(string key) => Format.TryAmount(Find(key).Text, out var value) ? value : 0m;

    public DateOnly? Day(string key) => Find(key).Day is { } d ? DateOnly.FromDateTime(d) : null;

    public string? ChoiceValue(string key) => Find(key).Choice?.Value;

    public bool Flag(string key) => Find(key).Flag;

    /// <summary>Signale les champs obligatoires vides et les montants illisibles.</summary>
    public bool Validate()
    {
        var valid = true;
        foreach (var field in Fields)
        {
            field.Error = null;
            if (field.Required && field.IsEmpty)
            {
                field.Error = "Champ obligatoire.";
                valid = false;
            }
            else if (field.Kind == FieldKind.Amount && !string.IsNullOrWhiteSpace(field.Text) && !Format.TryAmount(field.Text, out _))
            {
                field.Error = "Montant invalide.";
                valid = false;
            }
            else if (field.Kind == FieldKind.Phone && field.Text.Length > 0 && !(field.Text.Length == PhoneInput.Length && field.Text[0] == '0'))
            {
                field.Error = "10 chiffres commençant par 0, par exemple 0812345678.";
                valid = false;
            }
            else if (field.Kind == FieldKind.Day && field.Day is null && !string.IsNullOrWhiteSpace(field.Text))
            {
                field.Error = "Date invalide.";
                valid = false;
            }
        }
        Error = valid ? null : "Corrigez les champs signalés pour continuer.";
        return valid;
    }

    /// <summary>Signale chaque validation, réussie ou refusée : la fenêtre principale joue le son choisi dans Paramètres, Sons.</summary>
    public static event Action<bool>? Finished;

    /// <summary>Faux pour une simple confirmation : le son vient de l'action qui suit, pas du clic.</summary>
    public bool PlaysSound { get; init; } = true;

    private async Task SubmitAsync()
    {
        if (onSubmit is null || Busy) return;
        if (!Validate()) { Finished?.Invoke(false); return; }
        Error = null;
        Busy = true;
        try
        {
            if (await onSubmit(this))
            {
                Close(true);
                if (PlaysSound) Finished?.Invoke(true);
            }
        }
        // Hors ligne : l'opération est gardée sur l'appareil, la fenêtre se ferme ; la file d'envoi le signale.
        catch (OfflineQueuedException) { Close(false); }
        catch (ApiException ex) { Error = ex.Message; }
        catch (HttpRequestException) { Error = "Le serveur SCOLIA ne répond pas. Vérifiez qu'il est lancé sur ce poste."; }
        catch (Exception ex) when (ex is not OperationCanceledException) { Error = $"Erreur inattendue : {ex.Message}"; }
        finally { Busy = false; }
        if (Error is not null) Finished?.Invoke(false);
    }
}
