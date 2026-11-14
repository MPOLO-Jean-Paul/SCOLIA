using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Formats et couleurs partagés par les écrans EPST et LMD.</summary>
internal static class ProfileFormat
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    public static string N(decimal? v) => v is { } x ? x.ToString("0.##", Fr) : "—";
    public static string Pct(decimal? v) => v is { } x ? x.ToString("0.0", Fr) + " %" : "—";

    public static IBrush Tone(string? decision) => ThemeBrush.Get(decision switch
    {
        null or "" or "Incomplet" => "ToneNeutral",
        "Double" or "Redouble" or "Ajourné" or "Ajourne" => "ToneRed",
        "Repêchage" or "Repechage" or "Deuxième session" or "DeuxiemeSession" or "Réorienté" or "Reoriente"
            or "Admis avec dettes" or "AdmisAvecDettes" => "ToneOrange",
        _ => "ToneGreen",
    });

    public static string EpstLabel(string? outcome) => outcome switch
    {
        "Passe" => "Passe",
        "Repechage" => "Repêchage",
        "Redouble" => "Double",
        "PasseApresRepechage" => "Passe après repêchage",
        "Reoriente" => "Réorienté",
        "Incomplet" => "Incomplet",
        null => "",
        _ => outcome,
    };

    public static string LmdLabel(string? outcome) => outcome switch
    {
        "Admis" => "Admis",
        "DeuxiemeSession" => "Deuxième session",
        "AdmisAvecDettes" => "Admis avec dettes",
        "Ajourne" => "Ajourné",
        "Incomplet" => "Incomplet",
        null => "",
        _ => outcome,
    };
}

// =====================================================================================
// Règlement pédagogique
// =====================================================================================

public sealed record RegulationRowVm(AcademicRegulationDto Dto, string Summary, string StatusLabel, IBrush StatusBrush)
{
    public string Label => Dto.Label;
    public string Validation => Dto.ValidatedAtUtc is { } at
        ? $"Validé par {Dto.ValidatedByName} le {at.ToLocalTime():dd/MM/yyyy à HH:mm}"
        : Dto.Saved ? "Enregistré, en attente de validation" : "Proposition SCOLIA, à relire puis valider";
    public string Reference => string.IsNullOrWhiteSpace(Dto.LegalReference) ? "" : "Référence : " + Dto.LegalReference;
}

/// <summary>
/// Règlement pédagogique de l'année : seuils, repêchage, place, compensation LMD.
/// Aucun bulletin officiel ni aucune délibération sans règlement validé.
/// </summary>
public sealed class RegulationPage : PageViewModel
{
    private RegulationRowVm? selected;

    public RegulationPage(ShellViewModel shell) : base(shell)
    {
        Action("Valider le règlement", MaterialIconKind.CheckDecagramOutline, ValidateAsync, primary: true, row: true);
        Action("Modifier", MaterialIconKind.PencilOutline, EditAsync, row: true, open: true);
        Action("Rouvrir", MaterialIconKind.LockOpenVariantOutline, ReopenAsync, row: true, danger: true);
        ActionRefresh();
    }

    public override string Title => "Règlement pédagogique";
    public override string Subtitle => "Règles de l'année : à relire, ajuster puis valider";
    public override MaterialIconKind Glyph => MaterialIconKind.ScaleBalance;
    public ObservableCollection<RegulationRowVm> Items { get; } = [];
    public RegulationRowVm? Selected { get => selected; set => Set(ref selected, value); }

    public override async Task LoadAsync()
    {
        var list = await Shell.Api.GetAsync<List<AcademicRegulationDto>>("api/etablissement/reglements");
        var keep = selected?.Dto.Scope;
        Items.Clear();
        foreach (var r in list)
        {
            var status = r.Status == "Valide" ? "Validé" : r.Saved ? "Brouillon" : "Proposition";
            Items.Add(new RegulationRowVm(r, Summary(r), status, ThemeBrush.Get(r.Status == "Valide" ? "ToneGreen" : "ToneOrange")));
        }
        Selected = Items.FirstOrDefault(i => i.Dto.Scope == keep) ?? Items.FirstOrDefault();
    }

    private static string Summary(AcademicRegulationDto r) => r.Scope switch
    {
        "SuperieurLmd" => $"UE acquise à {ProfileFormat.N(r.UeValidationMark)}/{ProfileFormat.N(r.MarkScale)} · " +
                          $"compensation entre EC : {(r.CompensateWithinUe ? "oui" : "non")} · entre UE : {(r.CompensateBetweenUe ? "oui" : "non")} · " +
                          $"passage avec au plus {ProfileFormat.N(r.MaxDebtCredits)} crédits en dette",
        "Maternelle" => $"{r.TermsCount} trimestre(s) d'observation, sans points ni classement",
        _ => $"{r.TermsCount} {(r.TermsCount == 3 ? "trimestres" : "semestres")} de {r.PeriodsPerTerm} périodes et un examen " +
             $"(maximum ×{ProfileFormat.N(r.ExamMaxFactor)}) · réussite {ProfileFormat.N(r.PassPercent)} % · " +
             $"branche {ProfileFormat.N(r.SubjectPassPercent)} % · repêchage jusqu'à {r.MaxRepechageSubjects} branche(s) · " +
             $"place {(r.ShowRank ? "affichée" : "masquée")}",
    };

    private async Task EditAsync()
    {
        if (Selected?.Dto is not { } r) return;
        if (r.Status == "Valide") { Shell.Notify("Ce règlement est validé : rouvrez-le avant de le modifier."); return; }
        var lmd = r.Scope == "SuperieurLmd";
        var epst = r.Scope is "Primaire" or "Secondaire";
        var form = new FormViewModel($"Règlement — {r.Label}",
            "Valeurs proposées selon les usages en RDC : vérifiez-les avec les instructions de l'année et le conseil de l'établissement.",
            async f =>
            {
                await Shell.Api.SendAsync(HttpMethod.Put, $"api/etablissement/reglements/{r.Scope}", new
                {
                    termsCount = epst || r.Scope == "Maternelle" ? (int)f.Amount("termes") : r.TermsCount,
                    periodsPerTerm = epst ? (int)f.Amount("periodes") : r.PeriodsPerTerm,
                    examMaxFactor = epst ? f.Amount("examen") : r.ExamMaxFactor,
                    passPercent = epst ? f.Amount("reussite") : r.PassPercent,
                    subjectPassPercent = epst ? f.Amount("branche") : r.SubjectPassPercent,
                    maxRepechageSubjects = epst ? (int)f.Amount("repechage") : r.MaxRepechageSubjects,
                    showRank = epst ? f.Flag("place") : r.ShowRank,
                    markScale = lmd ? f.Amount("echelle") : r.MarkScale,
                    ueValidationMark = lmd ? f.Amount("validation") : r.UeValidationMark,
                    compensateWithinUe = lmd ? f.Flag("compEc") : r.CompensateWithinUe,
                    compensateBetweenUe = lmd ? f.Flag("compUe") : r.CompensateBetweenUe,
                    maxDebtCredits = lmd ? f.Amount("dette") : r.MaxDebtCredits,
                    legalReference = f.OptionalText("reference"),
                });
                Shell.Notify("Règlement enregistré. Validez-le pour produire les documents officiels.");
                return true;
            });
        if (epst)
        {
            form.Add("termes", "Trimestres (3) ou semestres (2)", FieldKind.Amount, r.TermsCount.ToString(CultureInfo.InvariantCulture), required: true);
            form.Add("periodes", "Périodes de travail par trimestre ou semestre", FieldKind.Amount, r.PeriodsPerTerm.ToString(CultureInfo.InvariantCulture), required: true);
            form.Add("examen", "Maximum de l'examen (multiple du maximum d'une période)", FieldKind.Amount, ProfileFormat.N(r.ExamMaxFactor), required: true);
            form.Add("reussite", "Seuil de réussite de l'année (%)", FieldKind.Amount, ProfileFormat.N(r.PassPercent), required: true);
            form.Add("branche", "Seuil d'une branche (%)", FieldKind.Amount, ProfileFormat.N(r.SubjectPassPercent), required: true);
            form.Add("repechage", "Branches admises au repêchage", FieldKind.Amount, r.MaxRepechageSubjects.ToString(CultureInfo.InvariantCulture), required: true);
            form.AddFlag("place", "Afficher la place de l'élève sur l'effectif", r.ShowRank);
        }
        else if (lmd)
        {
            form.Add("echelle", "Note maximale d'un EC", FieldKind.Amount, ProfileFormat.N(r.MarkScale), required: true);
            form.Add("validation", "Note de validation d'une UE", FieldKind.Amount, ProfileFormat.N(r.UeValidationMark), required: true);
            form.AddFlag("compEc", "Compensation entre les EC d'une même UE", r.CompensateWithinUe);
            form.AddFlag("compUe", "Compensation entre les UE d'un même semestre", r.CompensateBetweenUe);
            form.Add("dette", "Crédits en dette autorisés pour passer", FieldKind.Amount, ProfileFormat.N(r.MaxDebtCredits), required: true);
        }
        else
            form.Add("termes", "Trimestres d'observation", FieldKind.Amount, r.TermsCount.ToString(CultureInfo.InvariantCulture), required: true);
        form.Add("reference", "Référence (instruction, décision du conseil)", value: r.LegalReference);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task ValidateAsync()
    {
        if (Selected?.Dto is not { } r) return;
        if (r.Status == "Valide") { Shell.Notify("Ce règlement est déjà validé."); return; }
        if (!await Shell.Confirm("Valider le règlement",
                $"{r.Label} : {Selected.Summary}.\n\nUne fois validé, le règlement sert aux bulletins et aux délibérations de l'année. " +
                "Votre nom et l'heure de validation seront conservés.", "Valider")) return;
        await Shell.Api.SendAsync(HttpMethod.Post, $"api/etablissement/reglements/{r.Scope}/validation", new { });
        Shell.Notify("Règlement validé.");
        await RefreshAsync();
    }

    private async Task ReopenAsync()
    {
        if (Selected?.Dto is not { } r) return;
        var form = new FormViewModel("Rouvrir le règlement",
            "Impossible une fois des décisions publiées. Le motif est conservé dans le journal.",
            async f =>
            {
                await Shell.Api.SendAsync(HttpMethod.Post, $"api/etablissement/reglements/{r.Scope}/reouverture",
                    new { reason = f.Text("motif") });
                Shell.Notify("Règlement rouvert.");
                return true;
            }) { Danger = true };
        form.Add("motif", "Motif", required: true);
        if (await Shell.Ask(form)) await RefreshAsync();
    }
}

// =====================================================================================
// EPST : résultats et bulletins
// =====================================================================================

public sealed record EpstResultRowVm(Guid EnrollmentId, string Rank, string Matricule, string Name, string Points,
    string Percent, string Failed, string Missing, string Decision, IBrush DecisionBrush, string Subjects);

/// <summary>Résultats d'une classe du primaire ou du secondaire, bulletins officiels et conduite.</summary>
public sealed class EpstResultsPage : PageViewModel
{
    private ChoiceItem? promotion;
    private EpstResultRowVm? selected;
    private string subtitle = string.Empty;
    private bool ready;
    private EpstClassResultsDto? current;

    public EpstResultsPage(ShellViewModel shell) : base(shell)
    {
        Action("Bulletins de la classe", MaterialIconKind.FilePdfBox, ClassBulletinsAsync, primary: true);
        Action("Bulletin de l'élève", MaterialIconKind.FileAccountOutline, PupilBulletinAsync, row: true, open: true);
        Action("Application et conduite", MaterialIconKind.AccountStarOutline, ConductAsync);
        ActionRefresh();
    }

    public override string Title => "Résultats et bulletins";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.CertificateOutline;
    public ObservableCollection<EpstResultRowVm> Items { get; } = [];
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];
    public ChoiceItem? Promotion { get => promotion; set { if (Set(ref promotion, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public EpstResultRowVm? Selected { get => selected; set => Set(ref selected, value); }

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            Promotions = await PromotionChoicesAsync(withAll: false);
            promotion = Promotions.Count > 0 ? Promotions[0] : null;
            Raise(nameof(Promotions));
            Raise(nameof(Promotion));
            ready = true;
        }
        Items.Clear();
        if (Promotion is null) return;
        var r = current = await Shell.Api.GetAsync<EpstClassResultsDto>($"api/etablissement/cycles/promotions/{Promotion.Value}/resultats");
        foreach (var p in r.Pupils)
        {
            var subjects = string.Join(PhoneLayout.Active ? "\n" : " · ",
                p.Subjects.Select(s => $"{s.Name} : {ProfileFormat.N(s.Total)}/{ProfileFormat.N(s.Max)}" + (s.Failed ? " (échec)" : "")));
            Items.Add(new EpstResultRowVm(p.EnrollmentId, p.Rank?.ToString(CultureInfo.InvariantCulture) ?? "—",
                p.Matricule, p.FullName, $"{ProfileFormat.N(p.Total)} / {ProfileFormat.N(p.Max)}", ProfileFormat.Pct(p.Percent),
                p.FailedSubjects.ToString(CultureInfo.InvariantCulture),
                p.MissingCells == 0 ? "" : p.MissingCells.ToString(CultureInfo.InvariantCulture),
                p.Decision ?? (p.MissingCells > 0 ? "Incomplet" : "À délibérer"), ProfileFormat.Tone(p.Decision ?? "Incomplet"),
                subjects));
        }
        subtitle = $"{r.ClassName} · {r.CycleName} · {r.Headcount} élève(s) · réussite à {ProfileFormat.N(r.PassPercent)} %";
        Raise(nameof(Subtitle));
    }

    private Task ClassBulletinsAsync() => Promotion is null ? Task.CompletedTask
        : Shell.Download($"api/etablissement/cycles/promotions/{Promotion.Value}/bulletins.pdf", "bulletins-classe.pdf");

    private Task PupilBulletinAsync() => Promotion is null || Selected is null ? Task.CompletedTask
        : Shell.Download($"api/etablissement/cycles/promotions/{Promotion.Value}/bulletins.pdf?inscription={Selected.EnrollmentId}",
            $"bulletin-{Selected.Matricule}.pdf");

    private async Task ConductAsync()
    {
        if (current is null || Promotion is null) return;
        var periods = current.Periods.Where(p => !p.IsExam).Select(p => new ChoiceItem(p.Id.ToString(), p.Code)).ToList();
        if (periods.Count == 0) { Shell.Notify("Aucune période de travail dans le calendrier de ce cycle."); return; }
        var pick = new FormViewModel("Application et conduite", "Choisissez la période.", _ => Task.FromResult(true));
        pick.AddChoice("periode", "Période", periods, periods[0].Value, required: true);
        if (!await Shell.Ask(pick)) return;
        var periodId = Guid.Parse(pick.ChoiceValue("periode")!);
        var levels = new[] { "", "E", "TB", "B", "AB", "M", "MA" }
            .Select(c => new ChoiceItem(c, c.Length == 0 ? "—" : c)).ToList();
        var promotionId = Promotion.Value;
        var form = new FormViewModel("Application et conduite",
            "E : élite · TB : très bien · B : bien · AB : assez bien · M : médiocre · MA : mauvais.",
            async f =>
            {
                await Shell.Api.SendAsync(HttpMethod.Put, $"api/etablissement/cycles/promotions/{promotionId}/conduite", new
                {
                    periodId,
                    lines = current.Pupils.Select(p => new
                    {
                        enrollmentId = p.EnrollmentId,
                        application = f.ChoiceValue($"a-{p.EnrollmentId:N}"),
                        conduct = f.ChoiceValue($"c-{p.EnrollmentId:N}"),
                    }).ToArray(),
                });
                Shell.Notify("Application et conduite enregistrées.");
                return true;
            });
        foreach (var p in current.Pupils.OrderBy(p => p.FullName, StringComparer.CurrentCultureIgnoreCase))
        {
            var mark = p.Conduct.FirstOrDefault(c => c.PeriodId == periodId);
            form.AddHeader(p.FullName);
            form.AddChoice($"a-{p.EnrollmentId:N}", "Application", levels, mark?.Application ?? "");
            form.AddChoice($"c-{p.EnrollmentId:N}", "Conduite", levels, mark?.Conduct ?? "");
        }
        if (await Shell.Ask(form)) await RefreshAsync();
    }
}

// =====================================================================================
// EPST : délibération de fin d'année
// =====================================================================================

public sealed record EpstDecisionRowVm(EpstDecisionDto Dto, string Rank, string Percent, string Computed, string Jury,
    string Decision, IBrush DecisionBrush, string Repechage);

public sealed class EpstDeliberationPage : PageViewModel
{
    private ChoiceItem? promotion;
    private EpstDecisionRowVm? selected;
    private string subtitle = string.Empty;
    private bool ready;
    private EpstDeliberationDto? current;

    public EpstDeliberationPage(ShellViewModel shell) : base(shell) => BuildActions("Ouverte");

    /// <summary>Actions de l'étape en cours : l'étape suivante est le bouton principal, une étape passée disparaît.</summary>
    private void BuildActions(string status)
    {
        Actions.Clear();
        if (status == "Ouverte")
        {
            Action("Clôturer la première session", MaterialIconKind.Gavel, CloseFirstAsync, primary: true);
            Action("Décision du jury", MaterialIconKind.AccountTieVoiceOutline, JuryAsync, row: true, open: true);
        }
        if (status == "PremiereSessionCloturee")
        {
            Action("Clôturer le repêchage", MaterialIconKind.LockCheckOutline, CloseAsync, primary: true);
            Action("Points de repêchage", MaterialIconKind.PencilBoxOutline, RepechageAsync, row: true, open: true);
        }
        Action("Procès-verbal et palmarès", MaterialIconKind.FilePdfBox, PalmaresAsync, primary: status == "Cloturee");
        ActionRefresh();
    }

    public override string Title => "Délibération de fin d'année";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.Gavel;
    public ObservableCollection<EpstDecisionRowVm> Items { get; } = [];
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];
    public ChoiceItem? Promotion { get => promotion; set { if (Set(ref promotion, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public EpstDecisionRowVm? Selected { get => selected; set => Set(ref selected, value); }

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            Promotions = await PromotionChoicesAsync(withAll: false);
            promotion = Promotions.Count > 0 ? Promotions[0] : null;
            Raise(nameof(Promotions));
            Raise(nameof(Promotion));
            ready = true;
        }
        Items.Clear();
        if (Promotion is null) return;
        var d = current = await Shell.Api.GetAsync<EpstDeliberationDto>($"api/etablissement/cycles/promotions/{Promotion.Value}/deliberation");
        foreach (var p in d.Pupils)
            Items.Add(new EpstDecisionRowVm(p, p.Rank?.ToString(CultureInfo.InvariantCulture) ?? "—", ProfileFormat.Pct(p.Percent),
                ProfileFormat.EpstLabel(p.Computed), p.Jury is null ? "" : ProfileFormat.EpstLabel(p.Jury) + " — " + p.JuryReason,
                p.EffectiveLabel, ProfileFormat.Tone(p.EffectiveLabel),
                string.Join(", ", p.Repechage.Select(r => r.Name + (r.Score is { } s ? $" : {ProfileFormat.N(s)}/{ProfileFormat.N(r.MaxScore)}" : "")))));
        var status = d.Status switch
        {
            "Ouverte" => "délibération ouverte",
            "PremiereSessionCloturee" => "première session publiée, repêchage en cours",
            _ => "décisions définitives publiées",
        };
        subtitle = $"{d.ClassName} · {d.YearLabel} · {status}";
        Raise(nameof(Subtitle));
        BuildActions(d.Status);
    }

    private async Task JuryAsync()
    {
        if (Selected is null || Promotion is null) return;
        var outcomes = new[]
        {
            new ChoiceItem("", "Retour au calcul"), new ChoiceItem("Passe", "Passe"), new ChoiceItem("Repechage", "Repêchage"),
            new ChoiceItem("Redouble", "Double"), new ChoiceItem("Reoriente", "Réorienté"),
        };
        var p = Selected.Dto;
        var promotionId = Promotion.Value;
        var form = new FormViewModel($"Décision du jury — {p.FullName}",
            $"Calcul : {ProfileFormat.EpstLabel(p.Computed)} ({ProfileFormat.Pct(p.Percent)}, {p.FailedSubjects} échec(s)). Le motif figure au procès-verbal.",
            async f =>
            {
                await Shell.Api.SendAsync(HttpMethod.Put,
                    $"api/etablissement/cycles/promotions/{promotionId}/deliberation/eleves/{p.EnrollmentId}/jury",
                    new { outcome = f.ChoiceValue("decision"), reason = f.OptionalText("motif") });
                Shell.Notify("Décision du jury enregistrée.");
                return true;
            });
        form.AddChoice("decision", "Décision", outcomes, p.Jury ?? "");
        form.Add("motif", "Motif", value: p.JuryReason);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task CloseFirstAsync()
    {
        if (Promotion is null || current is null) return;
        var counts = string.Join(", ", current.Pupils.GroupBy(p => p.EffectiveLabel).Select(g => $"{g.Key} : {g.Count()}"));
        if (!await Shell.Confirm("Clôturer la première session",
                $"{current.ClassName} : {counts}.\n\nLes décisions seront publiées et les cotes, la conduite et les maxima de la classe seront figés.",
                "Clôturer", danger: true)) return;
        await Shell.Api.SendAsync(HttpMethod.Post, $"api/etablissement/cycles/promotions/{Promotion.Value}/deliberation/premiere-session", new { });
        Shell.Notify("Première session clôturée.");
        await RefreshAsync();
    }

    private async Task RepechageAsync()
    {
        if (Selected is null || Promotion is null) return;
        var p = Selected.Dto;
        if (p.Repechage.Count == 0) { Shell.Notify("Cet élève n'est pas au repêchage."); return; }
        var promotionId = Promotion.Value;
        var form = new FormViewModel($"Repêchage — {p.FullName}",
            "Points obtenus à l'épreuve de chaque branche présentée.",
            async f =>
            {
                await Shell.Api.SendAsync(HttpMethod.Put,
                    $"api/etablissement/cycles/promotions/{promotionId}/deliberation/eleves/{p.EnrollmentId}/repechage", new
                    {
                        marks = p.Repechage.Select(r => new
                        {
                            subjectId = r.SubjectId, score = (decimal?)f.Amount($"s-{r.SubjectId:N}"), maxScore = f.Amount($"m-{r.SubjectId:N}"),
                        }).ToArray(),
                    });
                Shell.Notify("Points de repêchage enregistrés.");
                return true;
            });
        foreach (var r in p.Repechage)
        {
            form.AddHeader(r.Name, $"Résultat de l'année : {ProfileFormat.Pct(r.SubjectPercent)}");
            form.Add($"s-{r.SubjectId:N}", "Points obtenus", FieldKind.Amount, r.Score is { } s ? ProfileFormat.N(s) : null, required: true);
            form.Add($"m-{r.SubjectId:N}", "Maximum de l'épreuve", FieldKind.Amount, ProfileFormat.N(r.MaxScore ?? 20), required: true);
        }
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task CloseAsync()
    {
        if (Promotion is null) return;
        if (!await Shell.Confirm("Clôturer le repêchage",
                "Les décisions de l'année deviennent définitives et serviront au passage de classe.", "Clôturer", danger: true)) return;
        await Shell.Api.SendAsync(HttpMethod.Post, $"api/etablissement/cycles/promotions/{Promotion.Value}/deliberation/cloture", new { });
        Shell.Notify("Délibération de l'année clôturée.");
        await RefreshAsync();
    }

    private Task PalmaresAsync() => Promotion is null ? Task.CompletedTask
        : Shell.Download($"api/etablissement/cycles/promotions/{Promotion.Value}/palmares.pdf", "proces-verbal-palmares.pdf");
}

// =====================================================================================
// EPST : examens nationaux
// =====================================================================================

public sealed record NationalExamRowVm(NationalExamCandidateDto Dto, string Birth, string Result, IBrush ResultBrush);

public sealed class NationalExamsPage : PageViewModel
{
    private ChoiceItem? exam;
    private NationalExamRowVm? selected;
    private string subtitle = string.Empty;

    public NationalExamsPage(ShellViewModel shell) : base(shell)
    {
        exam = Exams[0];
        Action("Exporter (Excel)", MaterialIconKind.MicrosoftExcel, () => ExportAsync("xlsx"), primary: true);
        Action("Imprimer la liste", MaterialIconKind.FilePdfBox, () => ExportAsync("pdf"));
        Action("Saisir le résultat", MaterialIconKind.PencilOutline, ResultAsync, row: true, open: true);
        ActionRefresh();
    }

    public override string Title => "Examens nationaux";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.ClipboardTextOutline;
    public ObservableCollection<NationalExamRowVm> Items { get; } = [];
    public IReadOnlyList<ChoiceItem> Exams { get; } =
    [
        new("Enafep", "ENAFEP (6e primaire)"),
        new("FinCteb", "Fin du CTEB (8e)"),
        new("ExamenEtat", "Examen d'État"),
    ];
    public ChoiceItem? Exam { get => exam; set { if (Set(ref exam, value)) _ = Shell.Run(RefreshAsync); } }
    public NationalExamRowVm? Selected { get => selected; set => Set(ref selected, value); }

    public override async Task LoadAsync()
    {
        Items.Clear();
        if (Exam is null) return;
        var data = await Shell.Api.GetAsync<NationalExamDto>($"api/etablissement/examens-nationaux/{Exam.Value}");
        foreach (var c in data.Candidates)
            Items.Add(new NationalExamRowVm(c,
                string.Join(" à ", new[] { c.BirthDate?.ToString("dd/MM/yyyy"), c.BirthPlace }.Where(x => !string.IsNullOrWhiteSpace(x))),
                c.Passed switch { true => "Réussi" + (c.Percent is { } p ? $" ({ProfileFormat.Pct(p)})" : ""), false => "Échec", _ => "Non communiqué" },
                ThemeBrush.Get(c.Passed switch { true => "ToneGreen", false => "ToneRed", _ => "ToneNeutral" })));
        subtitle = $"{data.Label} · {data.Candidates.Count} candidat(s)";
        Raise(nameof(Subtitle));
    }

    private Task ExportAsync(string format) => Exam is null ? Task.CompletedTask
        : Shell.Download($"api/etablissement/examens-nationaux/{Exam.Value}/liste" + (format == "xlsx" ? "?format=xlsx" : ""),
            $"candidats-{Exam.Value.ToLowerInvariant()}.{format}");

    private async Task ResultAsync()
    {
        if (Selected?.Dto is not { } c || Exam is null) return;
        var results = new[] { new ChoiceItem("", "Non communiqué"), new ChoiceItem("true", "Réussi"), new ChoiceItem("false", "Échec") };
        var exam = Exam.Value;
        var form = new FormViewModel($"{Exam.Label} — {c.FullName}", "Résultat officiel communiqué par l'autorité nationale.",
            async f =>
            {
                await Shell.Api.SendAsync(HttpMethod.Put, $"api/etablissement/examens-nationaux/{exam}/eleves/{c.EnrollmentId}", new
                {
                    passed = f.ChoiceValue("resultat") is { Length: > 0 } v ? bool.Parse(v) : (bool?)null,
                    percent = Format.TryAmount(f.Text("pourcentage"), out var p) ? p : (decimal?)null,
                    reference = f.OptionalText("reference"),
                });
                Shell.Notify("Résultat enregistré.");
                return true;
            });
        form.AddChoice("resultat", "Résultat", results, c.Passed?.ToString().ToLowerInvariant() ?? "");
        form.Add("pourcentage", "Pourcentage obtenu", FieldKind.Amount, c.Percent is { } x ? ProfileFormat.N(x) : null);
        form.Add("reference", "Numéro de candidat ou centre", value: c.Reference);
        if (await Shell.Ask(form)) await RefreshAsync();
    }
}

// =====================================================================================
// LMD : notes des EC
// =====================================================================================

public sealed class LmdMarkRowVm(LmdMarkLineDto dto) : Observable
{
    private string score = dto.Score is { } s ? ProfileFormat.N(s) : string.Empty;
    public Guid EnrollmentId => dto.EnrollmentId;
    public string Matricule => dto.Matricule;
    public string FullName => dto.FullName;
    public bool Editable => dto.Editable;
    public string Score { get => score; set => Set(ref score, value); }
    public decimal? Original => dto.Score;
}

/// <summary>Saisie des notes d'un élément constitutif, par session.</summary>
public sealed class LmdMarksPage : PageViewModel
{
    private ChoiceItem? promotion;
    private ChoiceItem? element;
    private ChoiceItem? session;
    private LmdMarkRowVm? selected;
    private string subtitle = string.Empty;
    private string lockReason = string.Empty;
    private bool ready;

    public LmdMarksPage(ShellViewModel shell) : base(shell)
    {
        session = Sessions[0];
        Action("Enregistrer les notes", MaterialIconKind.ContentSaveOutline, SaveAsync, primary: true);
        ActionRefresh();
    }

    public override string Title => "Notes des EC";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.PencilBoxMultipleOutline;
    public ObservableCollection<LmdMarkRowVm> Items { get; } = [];
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];
    public IReadOnlyList<ChoiceItem> Elements { get; private set; } = [];
    public IReadOnlyList<ChoiceItem> Sessions { get; } = [new("1", "Première session"), new("2", "Deuxième session")];
    public ChoiceItem? Promotion { get => promotion; set { if (Set(ref promotion, value) && ready) _ = Shell.Run(ReloadElementsAsync); } }
    public ChoiceItem? Element { get => element; set { if (Set(ref element, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public ChoiceItem? Session { get => session; set { if (Set(ref session, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public LmdMarkRowVm? Selected { get => selected; set => Set(ref selected, value); }
    public string LockReason { get => lockReason; private set { if (Set(ref lockReason, value)) Raise(nameof(IsLocked)); } }
    public bool IsLocked => !string.IsNullOrEmpty(lockReason);

    private async Task ReloadElementsAsync()
    {
        await LoadElementsAsync();
        await LoadSheetAsync();
    }

    private async Task LoadElementsAsync()
    {
        Elements = [];
        element = null;
        if (Promotion is not null)
        {
            var program = await Shell.Api.GetAsync<List<LmdProgramItemDto>>($"api/etablissement/lmd/promotions/{Promotion.Value}/programme");
            var units = program.Where(p => p.Kind == "TeachingUnit").ToDictionary(p => p.Id);
            Elements = program.Where(p => p.Kind == "TeachingElement")
                .OrderBy(p => p.Semester).ThenBy(p => p.Code)
                .Select(p => new ChoiceItem(p.Id.ToString(),
                    $"{p.Semester} · {(p.ParentItemId is { } u && units.TryGetValue(u, out var ue) ? ue.Code + " › " : "")}{p.Code} · {p.Name}"))
                .ToList();
            element = Elements.Count > 0 ? Elements[0] : null;
        }
        Raise(nameof(Elements));
        Raise(nameof(Element));
    }

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            Promotions = await PromotionChoicesAsync(withAll: false);
            promotion = Promotions.Count > 0 ? Promotions[0] : null;
            Raise(nameof(Promotions));
            Raise(nameof(Promotion));
            ready = true;
            await LoadElementsAsync();
        }
        await LoadSheetAsync();
    }

    private async Task LoadSheetAsync()
    {
        Items.Clear();
        if (Promotion is null || Element is null) { LockReason = string.Empty; return; }
        var sheet = await Shell.Api.GetAsync<LmdMarkSheetDto>(
            $"api/etablissement/lmd/promotions/{Promotion.Value}/ecs/{Element.Value}/notes?session={Session?.Value ?? "1"}");
        foreach (var line in sheet.Lines) Items.Add(new LmdMarkRowVm(line));
        LockReason = sheet.LockReason ?? (sheet.Lines.Any(l => l.Editable) ? string.Empty : "Aucun étudiant ne compose cet EC à cette session.");
        subtitle = $"{sheet.UeCode} › {sheet.Code} · {sheet.Name} · {sheet.Semester} · notes sur {ProfileFormat.N(sheet.MarkScale)}";
        Raise(nameof(Subtitle));
    }

    private async Task SaveAsync()
    {
        if (Promotion is null || Element is null) return;
        var lines = new List<object>();
        foreach (var row in Items.Where(r => r.Editable))
        {
            decimal? value = string.IsNullOrWhiteSpace(row.Score) ? null
                : Format.TryAmount(row.Score, out var parsed) ? parsed
                : throw new InvalidOperationException($"Note illisible pour {row.FullName} : « {row.Score} ».");
            if (value != row.Original) lines.Add(new { enrollmentId = row.EnrollmentId, score = value });
        }
        if (lines.Count == 0) { Shell.Notify("Aucune note modifiée."); return; }
        await Shell.Api.SendAsync(HttpMethod.Put, $"api/etablissement/lmd/promotions/{Promotion.Value}/ecs/{Element.Value}/notes",
            new { session = int.Parse(Session?.Value ?? "1", CultureInfo.InvariantCulture), lines });
        Shell.Notify($"{lines.Count} note(s) enregistrée(s).");
        await RefreshAsync();
    }
}

// =====================================================================================
// LMD : délibération et documents
// =====================================================================================

public sealed record LmdResultRowVm(LmdStudentLineDto Dto, string Rank, string S1, string Year, string Credits,
    string Computed, string Decision, IBrush DecisionBrush, string Missing);

public sealed class LmdResultsPage : PageViewModel
{
    private ChoiceItem? promotion;
    private ChoiceItem? stage;
    private LmdResultRowVm? selected;
    private string subtitle = string.Empty;
    private bool ready;
    private LmdPromotionResultsDto? current;

    public LmdResultsPage(ShellViewModel shell) : base(shell)
    {
        stage = Stages[0];
        BuildActions("Ouverte");
    }

    /// <summary>
    /// Actions de l'étape en cours : publier le S1, puis clôturer la première session, puis la deuxième.
    /// L'étape suivante est le bouton principal ; une étape franchie n'est plus proposée.
    /// </summary>
    private void BuildActions(string status)
    {
        Actions.Clear();
        if (status == "Ouverte") Action("Publier les résultats du S1", MaterialIconKind.Publish, PublishS1Async, primary: true);
        if (status is "Ouverte" or "S1Publie")
            Action("Clôturer la première session", MaterialIconKind.Gavel, CloseFirstAsync, primary: status == "S1Publie");
        if (status is "S1Publie" or "PremiereSessionCloturee")
            Action("Clôturer la deuxième session", MaterialIconKind.LockCheckOutline, CloseAsync, primary: status == "PremiereSessionCloturee");
        if (status != "Cloturee") Action("Décision du jury", MaterialIconKind.AccountTieVoiceOutline, JuryAsync, row: true, open: true);
        Action("Relevé de l'étudiant", MaterialIconKind.FileAccountOutline, TranscriptAsync, row: true);
        Action("Relevés de la promotion", MaterialIconKind.FilePdfBox, TranscriptsAsync, primary: status == "Cloturee");
        Action("Procès-verbal", MaterialIconKind.FileSign, MinutesAsync);
        ActionRefresh();
    }

    public override string Title => "Délibération LMD";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.Gavel;
    public ObservableCollection<LmdResultRowVm> Items { get; } = [];
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];
    public IReadOnlyList<ChoiceItem> Stages { get; } =
    [
        new("", "Étape en cours"), new("S1", "Résultats partiels du S1"),
        new("PremiereSession", "Première session"), new("Definitif", "Résultats définitifs"),
    ];
    public ChoiceItem? Promotion { get => promotion; set { if (Set(ref promotion, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public ChoiceItem? Stage { get => stage; set { if (Set(ref stage, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public LmdResultRowVm? Selected { get => selected; set => Set(ref selected, value); }

    private string StageQuery => Stage?.Value is { Length: > 0 } s ? $"?etape={s}" : string.Empty;

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            Promotions = await PromotionChoicesAsync(withAll: false);
            promotion = Promotions.Count > 0 ? Promotions[0] : null;
            Raise(nameof(Promotions));
            Raise(nameof(Promotion));
            ready = true;
        }
        Items.Clear();
        if (Promotion is null) return;
        var r = current = await Shell.Api.GetAsync<LmdPromotionResultsDto>($"api/etablissement/lmd/promotions/{Promotion.Value}/resultats{StageQuery}");
        foreach (var s in r.Students)
            Items.Add(new LmdResultRowVm(s, s.Rank?.ToString(CultureInfo.InvariantCulture) ?? "—",
                $"{ProfileFormat.N(s.S1.Average)} · {ProfileFormat.N(s.S1.CreditsAcquired)}/{ProfileFormat.N(s.S1.CreditsPlanned)} cr.",
                ProfileFormat.N(s.Average), $"{ProfileFormat.N(s.CreditsAcquired)}/{ProfileFormat.N(s.CreditsPlanned)}",
                r.Stage == "S1" ? s.Computed : ProfileFormat.LmdLabel(s.Computed),
                s.Decision ?? "À délibérer", ProfileFormat.Tone(s.Decision ?? "Incomplet"),
                string.Join(", ", s.Units.Where(u => u.Average is not null && !u.Acquired).Select(u => u.Code))));
        var status = r.Status switch
        {
            "Ouverte" => "notes du S1 en cours",
            "S1Publie" => "S1 publié, S2 en cours",
            "PremiereSessionCloturee" => "première session publiée, deuxième session en cours",
            _ => "année clôturée",
        };
        subtitle = $"{r.PromotionName} ({r.Level}){(r.Filiere is { } f ? " · " + f : "")} · {r.YearLabel} · {status}";
        Raise(nameof(Subtitle));
        BuildActions(r.Status);
    }

    private async Task Step(string title, string message, string route, string done)
    {
        if (Promotion is null) return;
        if (!await Shell.Confirm(title, message, "Publier", danger: true)) return;
        await Shell.Api.SendAsync(HttpMethod.Post, $"api/etablissement/lmd/promotions/{Promotion.Value}/{route}", new { });
        Shell.Notify(done);
        stage = Stages[0];
        Raise(nameof(Stage));
        await RefreshAsync();
    }

    private Task PublishS1Async() => Step("Publier les résultats du S1",
        "Les résultats partiels du premier semestre seront publiés et les notes du S1 figées.", "publication-s1",
        "Résultats partiels du S1 publiés.");

    private Task CloseFirstAsync() => Step("Clôturer la première session",
        "Les résultats de la première session portent sur tous les cours de la promotion. Les notes de première session seront figées.",
        "premiere-session", "Première session publiée.");

    private Task CloseAsync() => Step("Clôturer la deuxième session",
        "Les décisions de l'année deviennent définitives ; les dettes des admis avec dettes sont créées.", "cloture",
        "Délibération de l'année clôturée.");

    private async Task JuryAsync()
    {
        if (Selected is null || Promotion is null || current is null) return;
        var final = current.Status == "PremiereSessionCloturee";
        var outcomes = new List<ChoiceItem> { new("", "Retour au calcul"), new("Admis", "Admis") };
        if (final) outcomes.Add(new("AdmisAvecDettes", "Admis avec dettes"));
        else outcomes.Add(new("DeuxiemeSession", "Deuxième session"));
        outcomes.Add(new("Ajourne", "Ajourné"));
        var s = Selected.Dto;
        var promotionId = Promotion.Value;
        var form = new FormViewModel($"Décision du jury — {s.FullName}",
            $"Calcul : {ProfileFormat.LmdLabel(s.Computed)} · {ProfileFormat.N(s.CreditsAcquired)}/{ProfileFormat.N(s.CreditsPlanned)} crédits. Le motif figure au procès-verbal.",
            async f =>
            {
                await Shell.Api.SendAsync(HttpMethod.Put, $"api/etablissement/lmd/promotions/{promotionId}/etudiants/{s.EnrollmentId}/jury",
                    new { outcome = f.ChoiceValue("decision"), reason = f.OptionalText("motif") });
                Shell.Notify("Décision du jury enregistrée.");
                return true;
            });
        form.AddChoice("decision", "Décision", outcomes, s.Jury ?? "");
        form.Add("motif", "Motif", value: s.JuryReason);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private Task TranscriptAsync() => Promotion is null || Selected is null ? Task.CompletedTask
        : Shell.Download($"api/etablissement/lmd/promotions/{Promotion.Value}/releves.pdf?inscription={Selected.Dto.EnrollmentId}" +
                         (Stage?.Value is { Length: > 0 } st ? $"&etape={st}" : ""), $"releve-{Selected.Dto.Matricule}.pdf");

    private Task TranscriptsAsync() => Promotion is null ? Task.CompletedTask
        : Shell.Download($"api/etablissement/lmd/promotions/{Promotion.Value}/releves.pdf{StageQuery}", "releves-promotion.pdf");

    private Task MinutesAsync() => Promotion is null ? Task.CompletedTask
        : Shell.Download($"api/etablissement/lmd/promotions/{Promotion.Value}/proces-verbal.pdf{StageQuery}", "proces-verbal.pdf");
}

// =====================================================================================
// LMD : dettes
// =====================================================================================

public sealed record LmdDebtRowVm(LmdDebtDto Dto, string Status, IBrush StatusBrush);

public sealed class LmdDebtsPage : PageViewModel
{
    private LmdDebtRowVm? selected;
    private string subtitle = string.Empty;

    public LmdDebtsPage(ShellViewModel shell) : base(shell)
    {
        Action("Apurer la dette", MaterialIconKind.CheckCircleOutline, ClearAsync, primary: true, row: true, open: true);
        ActionRefresh();
    }

    public override string Title => "Dettes académiques";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.BookAlertOutline;
    public ObservableCollection<LmdDebtRowVm> Items { get; } = [];
    public LmdDebtRowVm? Selected { get => selected; set => Set(ref selected, value); }

    public override async Task LoadAsync()
    {
        var list = await Shell.Api.GetAsync<List<LmdDebtDto>>("api/etablissement/lmd/dettes");
        Items.Clear();
        foreach (var d in list)
            Items.Add(new LmdDebtRowVm(d,
                d.ClearedAtUtc is { } at ? $"Apurée ({ProfileFormat.N(d.ClearedMark)}) le {at.ToLocalTime():dd/MM/yyyy}" : "À apurer",
                ThemeBrush.Get(d.ClearedAtUtc is null ? "ToneOrange" : "ToneGreen")));
        subtitle = $"{list.Count(d => d.ClearedAtUtc is null)} dette(s) à apurer · {list.Count} au total";
        Raise(nameof(Subtitle));
    }

    private async Task ClearAsync()
    {
        if (Selected?.Dto is not { } d) return;
        if (d.ClearedAtUtc is not null) { Shell.Notify("Cette dette est déjà apurée."); return; }
        var form = new FormViewModel($"Apurer {d.UeCode} — {d.FullName}",
            $"{d.UeName} ({ProfileFormat.N(d.Credits)} crédits, {d.Semester}, {d.OriginYear}). La note doit atteindre la validation d'une UE.",
            async f =>
            {
                await Shell.Api.SendAsync(HttpMethod.Put, $"api/etablissement/lmd/dettes/{d.Id}/apurement", new { mark = f.Amount("note") });
                Shell.Notify("Dette apurée.");
                return true;
            });
        form.Add("note", "Note obtenue", FieldKind.Amount, required: true);
        if (await Shell.Ask(form)) await RefreshAsync();
    }
}
