using Material.Icons;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>
/// Onglet d'écran de la barre du haut, comme les onglets de documents d'Acrobat : l'écran garde sa saisie
/// et sa position quand on passe d'un onglet à l'autre. L'onglet Accueil ne se ferme pas.
/// </summary>
public sealed class PageTabVm : Observable
{
    private PageViewModel page;
    private bool isActive;

    public PageTabVm(PageViewModel page, bool canClose, Action<PageTabVm> select, Action<PageTabVm> close)
    {
        this.page = page;
        CanClose = canClose;
        Select = new RelayCommand(() => select(this));
        Close = new RelayCommand(() => close(this));
    }

    public PageViewModel Page
    {
        get => page;
        set
        {
            if (!Set(ref page, value)) return;
            Raise(nameof(Title));
            Raise(nameof(Glyph));
        }
    }

    public string Title => page.Title;
    public MaterialIconKind Glyph => page.Glyph;
    public bool CanClose { get; }
    public bool IsActive { get => isActive; set => Set(ref isActive, value); }
    public RelayCommand Select { get; }
    public RelayCommand Close { get; }
}
