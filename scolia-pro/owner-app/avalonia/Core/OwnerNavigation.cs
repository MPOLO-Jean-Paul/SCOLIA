using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace JPServices.Owner;

public sealed partial class OwnerShell
{
    private readonly StackPanel _statusPanel = new() { Spacing = 8 };
    private readonly Dictionary<string, string> _drafts = new(StringComparer.Ordinal);
    private readonly List<(Page Page, Button Button, Border? Pill, TextBlock Label)> _navigation = [];
    private Grid? _headerGrid;
    private Button? _headerRefresh, _headerBack;
    private ProgressBar? _progress;
    private TextBlock? _headerSubtitle;
    private Page _schoolReturnPage = Page.Schools;
    private Page _menuReturnPage = Page.Home;
    private Vector _schoolReturnOffset;
    private TopLevel? _backHost;

    private void OnBackRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => e.Handled = Back();

    internal bool Back()
    {
        if (_page == Page.SchoolDetail) ReturnToSchools();
        else if (_page == Page.Menu) Navigate(_menuReturnPage);
        else if (_page == Page.Storage) Navigate(Page.Backups);
        else if (_page is Page.Turn or Page.Netlify) Navigate(Page.Relays);
        else if (_page != Page.Home) Navigate(Page.Home);
        else return false;
        return true;
    }

    private Page CurrentSection => _page == Page.SchoolDetail ? _schoolReturnPage : _page;
    private static string SectionIcon(Page page) => page == Page.Menu ? "AppsList" :
        Sections.First(x => x.Page == page).Icon;

    private Control BuildSidebar()
    {
        var brand = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(Text("SCOLIA Console", 18, true));
        brand.Children.Add(Text("JP-Services · Administration", 11, color: OwnerPalette.Muted));
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        heading.Children.Add(BrandMark());
        Grid.SetColumn(brand, 1);
        heading.Children.Add(brand);
        var stack = new StackPanel { Spacing = 22, Margin = new Thickness(12, 22) };
        stack.Children.Add(heading);
        stack.Children.Add(Text("Espace administrateur", 12, true, OwnerPalette.Muted));
        foreach (var item in Sections)
            _sidebarButtons.Children.Add(NavigationButton(item.Page, item.Name, mobile: false));
        stack.Children.Add(_sidebarButtons);
        var protection = new StackPanel { Spacing = 8 };
        protection.Children.Add(OwnerIcons.Color("ShieldCheckmark", 28));
        protection.Children.Add(Text("Accès protégé", 14, true));
        protection.Children.Add(Text("Licences et disponibilité des serveurs. Les dossiers scolaires restent privés.", 12, color: OwnerPalette.Muted));
        stack.Children.Add(Box(protection));
        return new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    private Button NavigationButton(Page page, string label, bool mobile)
    {
        var button = Button(label, () => Navigate(page), subtle: true);
        button.Classes.Add("ownernav");
        button.BorderThickness = new Thickness(0);
        button.Background = Brushes.Transparent;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var title = Text(label, mobile ? 11 : 14, bold: true);
        Border? pill = null;
        if (mobile)
        {
            title.FontSize = 11;
            title.LineHeight = 14;
            title.TextAlignment = TextAlignment.Center;
            title.Text = page == Page.Schools ? "Écoles" : label;
            pill = new Border { Child = OwnerIcons.Color(SectionIcon(page), 24), Width = 46, Height = 30, CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Center };
            button.Content = new StackPanel { Spacing = 5, Children = { pill, title } };
            button.Padding = new Thickness(2, 7);
            button.MinHeight = 64;
        }
        else
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
            row.Children.Add(OwnerIcons.Color(SectionIcon(page), 24));
            Grid.SetColumn(title, 1);
            row.Children.Add(title);
            button.Content = row;
            button.Padding = new Thickness(10, 9);
        }
        _navigation.Add((page, button, pill, title));
        return button;
    }

    private Button IconButton(string icon, string label, Action action)
    {
        var button = Button(label, action, subtle: true);
        button.Content = OwnerIcons.Line(icon);
        button.Width = 44;
        button.Height = 44;
        button.Padding = new Thickness(10);
        button.Background = Brushes.Transparent;
        button.BorderThickness = new Thickness(0);
        ToolTip.SetTip(button, label);
        return button;
    }

    private Control BuildHeader()
    {
        _headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), ColumnSpacing = 8, RowSpacing = 4, Margin = new Thickness(16, 8) };
        _headerBack = IconButton("Back", "Revenir", () => { Back(); });
        _headerGrid.Children.Add(_headerBack);
        Grid.SetColumn(_heading, 1);
        _headerGrid.Children.Add(_heading);
        _headerRefresh = IconButton("Refresh", "Actualiser", () => _ = LoadAsync(_page, force: true));
        Grid.SetColumn(_headerRefresh, 2);
        _headerGrid.Children.Add(_headerRefresh);
        var web = IconButton("Open", "Ouvrir la console web", () => _ = OpenConsoleAsync());
        Grid.SetColumn(web, 3);
        _headerGrid.Children.Add(web);
        var subtitle = _headerSubtitle = Text("JP-Services · Console administrateur", 11, color: OwnerPalette.Muted);
        Grid.SetRow(subtitle, 1);
        Grid.SetColumn(subtitle, 1);
        Grid.SetColumnSpan(subtitle, 3);
        _headerGrid.Children.Add(subtitle);
        _progress = new ProgressBar { IsIndeterminate = true, Height = 2, MinHeight = 2, MinWidth = 0, IsVisible = false };
        Grid.SetRow(_progress, 2);
        Grid.SetColumnSpan(_progress, 4);
        _headerGrid.Children.Add(_progress);
        foreach (var page in new[] { Page.Home, Page.Schools, Page.Licenses, Page.Activity, Page.Menu })
        {
            var label = page == Page.Menu ? "Menu" : Sections.First(s => s.Page == page).Name;
            var nav = NavigationButton(page, label, mobile: true);
            Grid.SetColumn(nav, _bottomButtons.Children.Count);
            _bottomButtons.Children.Add(nav);
        }
        return _headerGrid;
    }

    private void ApplyResponsive()
    {
        Classes.Set("compact", _compact);
        _layout.ColumnDefinitions[0].Width = new GridLength(_compact ? 0 : 250);
        _layout.RowDefinitions[2].Height = GridLength.Auto;
        _sidebar.IsVisible = !_compact;
        _bottomNav.IsVisible = _compact;
        _headerBack!.IsVisible = _page is Page.SchoolDetail or Page.Menu or Page.Storage or Page.Turn or Page.Netlify;
        _headerRefresh!.IsVisible = _page is not (Page.Account or Page.Menu) && _credentials is { IsComplete: true };
        var fullMobileTitle = _compact && !_short;
        Grid.SetRow(_heading, fullMobileTitle ? 1 : 0);
        Grid.SetColumn(_heading, fullMobileTitle ? 0 : 1);
        Grid.SetColumnSpan(_heading, fullMobileTitle ? 4 : 1);
        _heading.FontSize = _compact && _short ? 17 : 20;
        _heading.MaxLines = _compact && _short ? 1 : 3;
        _heading.TextTrimming = _compact && _short ? TextTrimming.CharacterEllipsis : TextTrimming.None;
        ToolTip.SetTip(_heading, _heading.Text);
        _headerSubtitle!.IsVisible = !_compact || !_short;
        Grid.SetRow(_headerSubtitle, _compact ? 0 : 1);
        Grid.SetColumnSpan(_headerSubtitle, _compact ? 1 : 3);
        foreach (var item in _navigation)
        {
            var selected = item.Page == CurrentSection || item.Page == Page.Menu && CurrentSection is Page.Servers or Page.Relays or Page.Backups or Page.Account;
            item.Button.Background = item.Pill is null && selected ? OwnerPalette.BlueWash : Brushes.Transparent;
            if (item.Pill is { } pill) pill.Background = selected ? OwnerPalette.BlueWash : Brushes.Transparent;
            item.Label.Foreground = selected ? OwnerPalette.Blue : OwnerPalette.Ink;
            AutomationProperties.SetHelpText(item.Button, selected ? "Page sélectionnée" : "Ouvrir la page");
        }
        if (_scroll.Content is StackPanel body)
            body.Margin = new Thickness(_compact ? 16 : 24, 16, _compact ? 16 : 24, 28);
    }

    private void Navigate(Page page)
    {
        if (page == Page.Menu && _page != Page.Menu) _menuReturnPage = _page == Page.SchoolDetail ? _schoolReturnPage : _page;
        _page = page;
        _error = null;
        _notice = null;
        if (page is not (Page.Schools or Page.SchoolDetail)) _invitation = null;
        ShowPage();
        _ = LoadAsync(page);
    }

    private void OpenSchool(string id)
    {
        if (_page != Page.SchoolDetail)
        {
            _schoolReturnPage = _page;
            _schoolReturnOffset = _scroll.Offset;
        }
        _selectedSchool = id;
        _school = null;
        _notice = _error = null;
        _page = Page.SchoolDetail;
        ShowPage();
        _ = LoadAsync(Page.SchoolDetail);
    }

    private void ReturnToSchools()
    {
        Navigate(_schoolReturnPage);
        _scroll.Offset = _schoolReturnOffset;
    }

    private Control MenuPage()
    {
        var stack = PageContainer();
        stack.Children.Add(PageTitle("Tous les outils", "Administration de SCOLIA", false));
        foreach (var section in Sections)
            stack.Children.Add(CompactAction(section.Name, Description(section.Page), section.Page));
        return stack;
    }

    private static string Description(Page page) => page switch
    {
        Page.Home => "Vue d'ensemble et points à suivre",
        Page.Schools => "Établissements et invitations de liaison",
        Page.Licenses => "Renouvellements, essais et accès",
        Page.Servers => "Serveurs enregistrés et derniers signaux",
        Page.Relays => "Connexions Internet et fournisseurs WSS",
        Page.Activity => "Historique des opérations administratives",
        Page.Backups => "État des sauvegardes chiffrées",
        Page.Storage => "Configuration et vérification du stockage R2",
        Page.Turn => "Catalogue des fournisseurs Cloudflare TURN",
        Page.Netlify => "Configuration et test du site Netlify de secours",
        _ => "Accès sécurisé, apparence et mises à jour"
    };

    private void ShowStatus()
    {
        _statusPanel.Children.Clear();
        _statusPanel.Margin = _error is null && _notice is null ? default : new Thickness(_compact ? 16 : 24, 8, _compact ? 16 : 24, 0);
        if (_error is not null) _statusPanel.Children.Add(Banner(_error, OwnerPalette.RedWash, OwnerPalette.Red));
        else if (_notice is not null) _statusPanel.Children.Add(Banner(_notice, OwnerPalette.GreenWash, OwnerPalette.Teal));
        if (_progress is not null) _progress.IsVisible = _loading || _writing;
        if (_headerRefresh is not null) _headerRefresh.IsEnabled = !_loading && !_writing;
    }

    private TextBox Field(string key, string label, string initial = "", string? hint = null)
    {
        var field = new TextBox { Text = _drafts.GetValueOrDefault(key, initial), PlaceholderText = hint, MaxLength = 200 };
        AutomationProperties.SetName(field, label);
        field.TextChanged += (_, _) => _drafts[key] = field.Text ?? "";
        return field;
    }
}
