using System.Collections.ObjectModel;
using System.Globalization;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Ligne d'observation maternelle ou de cote scolaire, toujours distincte.</summary>
public sealed record SchoolBulletinLineVm(string Title, string Value, string? Comment, bool IsObservation)
{
    public bool HasComment => !string.IsNullOrWhiteSpace(Comment);
}

public sealed record SchoolBulletinPeriodVm(
    string Title, string EntryHeading, IReadOnlyList<SchoolBulletinLineVm> Lines)
{
    public bool HasLines => Lines.Count > 0;
}

public sealed record SchoolBulletinTermVm(
    string Label, IReadOnlyList<SchoolBulletinPeriodVm> Periods);

/// <summary>
/// Consultation seulement : aucun PDF officiel, aucune délibération inventée.
/// Cette page est réservée aux profils scolaires une fois activés ; le
/// secrétariat ITM ne reçoit pas de raccourci et conserve ses propres bulletins.
/// </summary>
public sealed class SchoolBulletinPage : PageViewModel
{
    private readonly Guid enrollmentId;
    private string subtitle = string.Empty;
    private string notice = string.Empty;
    private string documentTitle = "Aperçu pédagogique";

    public SchoolBulletinPage(ShellViewModel shell, Guid enrollmentId) : base(shell)
    {
        this.enrollmentId = enrollmentId;
        ActionRefresh();
    }

    public override string Title => "Carnet / bulletin provisoire";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.FileDocumentOutline;

    public string Notice { get => notice; private set => Set(ref notice, value); }
    public string DocumentTitle { get => documentTitle; private set => Set(ref documentTitle, value); }
    public ObservableCollection<SchoolBulletinTermVm> Terms { get; } = [];

    public override async Task LoadAsync()
    {
        var preview = await Shell.Api.GetAsync<SchoolBulletinPreviewDto>(
            $"api/etablissement/cycles/evaluations/{enrollmentId}/apercu");
        if (preview.IsOfficial)
            throw new InvalidOperationException(
                "Cette page n'affiche que des aperçus non officiels. Utilisez le document validé pour les bulletins définitifs.");

        subtitle = $"{preview.Cycle} · {preview.Promotion} · {preview.AcademicYear}";
        Raise(nameof(Subtitle));
        Notice = preview.Notice;
        DocumentTitle = string.IsNullOrWhiteSpace(preview.DocumentTitle)
            ? "Aperçu pédagogique" : preview.DocumentTitle;
        Terms.Clear();

        foreach (var term in preview.Terms ?? [])
        {
            var periods = term.Periods.Select(period =>
            {
                var lines = period.Entries.Select(e =>
                {
                    var value = e.Mode == "Observation"
                        ? e.Observation ?? "Observation non renseignée"
                        : e.Score is { } score && e.MaxScore is { } max
                            ? $"{score.ToString("0.##", CultureInfo.CurrentCulture)} / {max.ToString("0.##", CultureInfo.CurrentCulture)}"
                            : "Non coté";
                    return new SchoolBulletinLineVm(e.Title, value, e.Comment,
                        e.Mode == "Observation");
                }).ToArray();
                return new SchoolBulletinPeriodVm(
                    $"{period.Code} · {period.Name}", preview.EntryHeading, lines);
            }).ToArray();
            Terms.Add(new SchoolBulletinTermVm(term.Label, periods));
        }
    }
}
