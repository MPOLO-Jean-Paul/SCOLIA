using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace JPServices.Owner;

public sealed partial class OwnerShell
{
    private StackPanel PageContainer() => new()
    {
        Margin = new Thickness(_compact ? 16 : 24, 16, _compact ? 16 : 24, 28),
        Spacing = 18, MaxWidth = 1200, HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static Control Banner(string text, IBrush fill, IBrush ink) =>
        Box(Text(text, 13, color: ink), fill, padding: new Thickness(14, 12));

    private Control PageTitle(string title, string subtitle, bool refresh = true)
    {
        var heading = Text(title, 28, true);
        heading.Classes.Add("ownerpagetitle");
        if (title == _heading.Text) heading.Classes.Add("ownerduplicate");
        return new StackPanel
        {
            Spacing = 6,
            Children = { heading, Text(subtitle, 13, color: OwnerPalette.Muted) }
        };
    }

    private Control WelcomePanel()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14 };
        grid.Children.Add(new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(12), Background = OwnerPalette.BlueWash,
            Child = OwnerIcons.Color("ShieldCheckmark", 32), VerticalAlignment = VerticalAlignment.Top
        });
        var body = new StackPanel { Spacing = 7 };
        var title = Text("Votre espace administrateur", 24, true);
        title.Classes.Add("ownerwelcome");
        body.Children.Add(title);
        body.Children.Add(Text(_credentials is { IsComplete: true }
            ? "Établissements, licences et continuité de service, au même endroit."
            : "Reliez votre accès JP-Services pour administrer vos établissements.", 13, color: OwnerPalette.Muted));
        if (_credentials is not { IsComplete: true })
        {
            var configure = Button("Configurer l'accès", () => Navigate(Page.Account), primary: true);
            configure.HorizontalAlignment = HorizontalAlignment.Left;
            configure.Margin = new Thickness(0, 7, 0, 0);
            body.Children.Add(configure);
        }
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        return Box(grid);
    }

    private Control HomePage()
    {
        var stack = PageContainer();
        stack.Children.Add(WelcomePanel());
        if (_credentials is { IsComplete: true })
        {
            stack.Children.Add(Text("En bref", 17, true));
            var data = _summary ?? default;
            var metrics = new OwnerTiles { MinimumWidth = 148, MaximumColumns = 4 };
            metrics.Children.Add(Metric("Établissements", Count(data, "schools"), $"{Count(data, "active_schools_30d")} actifs sur 30 jours", Page.Schools));
            metrics.Children.Add(Metric("Lecture seule", Count(data, "suspended"), "Licences échues ou bloquées", Page.Licenses));
            metrics.Children.Add(Metric("Serveurs", Count(data, "server_devices"), "Appareils enregistrés", Page.Servers));
            metrics.Children.Add(Metric("Sauvegardes", Count(data, "cloud_enabled"), $"{Count(data, "cloud_backup_late")} en retard", Page.Backups));
            stack.Children.Add(metrics);
        }
        stack.Children.Add(Text("Outils d'administration", 17, true));
        var tools = new OwnerTiles { MinimumWidth = 340, MaximumColumns = 2 };
        foreach (var section in Sections.Where(s => s.Page != Page.Home))
            tools.Children.Add(CompactAction(section.Name, Description(section.Page), section.Page));
        stack.Children.Add(tools);
        return stack;
    }

    private static string Count(JsonElement summary, string name) =>
        OwnerText.Number(summary, name)?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";

    private Control Metric(string label, string value, string detail, Page page)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        header.Children.Add(Text(label, 13, color: OwnerPalette.Muted));
        var icon = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Background = OwnerPalette.Background, Child = OwnerIcons.Color(SectionIcon(page), 24) };
        Grid.SetColumn(icon, 1);
        header.Children.Add(icon);
        var number = Text(value, 24, true);
        number.Classes.Add("ownermetric");
        number.ClearValue(TextBlock.FontSizeProperty);
        var pane = new StackPanel { Spacing = 8, Children = { header, number, Text(detail, 13, color: OwnerPalette.Muted) } };
        var button = Button(label, () => Navigate(page), subtle: true);
        button.Classes.Add("ownermetriccard");
        button.Content = pane;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.VerticalContentAlignment = VerticalAlignment.Top;
        button.Background = OwnerPalette.Paper;
        button.Padding = new Thickness(16);
        button.MinHeight = 140;
        return button;
    }

    private Control CompactAction(string title, string detail, Page next)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        row.Children.Add(new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Background = OwnerPalette.Background, Child = OwnerIcons.Color(SectionIcon(next), 26), VerticalAlignment = VerticalAlignment.Center });
        var info = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(Text(title, 14, true));
        info.Children.Add(Text(detail, 13, color: OwnerPalette.Muted));
        Grid.SetColumn(info, 1);
        row.Children.Add(info);
        var arrow = OwnerIcons.Line("Chevron", 18);
        arrow.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(arrow, 2);
        row.Children.Add(arrow);
        var button = Button(title, () => Navigate(next), subtle: true);
        button.Content = row;
        button.Classes.Add("ownerrow");
        button.Background = OwnerPalette.Paper;
        button.BorderBrush = OwnerPalette.Border;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Padding = new Thickness(16, 14);
        return button;
    }

    private Control Row(string title, string detail, (string Label, LicenseTone Tone)? badge = null, Action? open = null)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12, RowSpacing = 10 };
        var info = new StackPanel { Spacing = 6, Children = { Text(title, 14, true), Text(detail, 13, color: OwnerPalette.Muted) } };
        grid.Children.Add(info);
        if (badge is { } b)
        {
            var (fill, ink) = b.Tone switch
            {
                LicenseTone.Good => (OwnerPalette.GreenWash, OwnerPalette.Teal),
                LicenseTone.Warning => (OwnerPalette.AmberWash, OwnerPalette.Amber),
                LicenseTone.Bad => (OwnerPalette.RedWash, OwnerPalette.Red),
                _ => (OwnerPalette.BlueWash, OwnerPalette.Blue)
            };
            var chip = Box(Text(b.Label, 12, true, ink), fill, 8, new Thickness(9, 5));
            chip.BorderThickness = new Thickness(0);
            chip.HorizontalAlignment = HorizontalAlignment.Left;
            chip.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(chip, 1);
            grid.Children.Add(chip);
        }
        OwnerLayout.StackBelow(grid, 520);
        if (open is null) return Box(grid);
        var button = Button(title, open, subtle: true);
        button.Content = grid;
        button.Classes.Add("ownerrow");
        button.Background = OwnerPalette.Paper;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Padding = new Thickness(16);
        return button;
    }

    private static Control Empty(string text) => Box(Text(text, 13, color: OwnerPalette.Muted));

    private Control AppearancePanel()
    {
        var pane = new StackPanel { Spacing = 10 };
        pane.Children.Add(Text("Apparence", 17, true));
        pane.Children.Add(Text("Choisissez une présentation claire, sombre ou identique à celle de l'appareil.", 13, color: OwnerPalette.Muted));
        var options = new WrapPanel();
        foreach (var (label, theme) in new[] { ("Système", ThemeVariant.Default), ("Clair", ThemeVariant.Light), ("Sombre", ThemeVariant.Dark) })
        {
            var option = new RadioButton { Content = label, GroupName = "owner-appearance", IsChecked = Application.Current!.RequestedThemeVariant == theme, MinHeight = 44, Margin = new Thickness(0, 0, 16, 0) };
            AutomationProperties.SetName(option, "Thème " + label.ToLowerInvariant());
            option.IsCheckedChanged += (_, _) =>
            {
                if (option.IsChecked == true) Application.Current.RequestedThemeVariant = theme;
            };
            options.Children.Add(option);
        }
        pane.Children.Add(options);
        return Box(pane);
    }
}
