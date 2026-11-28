using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Bandeaux de filtres du mobile : chaque champ, prévu pour une ligne d'ordinateur avec sa largeur fixe, prend
/// toute la largeur d'un téléphone, ou une part égale d'une tablette. Les boutons gardent leur taille et passent à la ligne.
/// </summary>
public static class PhoneFields
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(PhoneFields));

    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);

    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);

    static PhoneFields()
    {
        EnabledProperty.Changed.AddClassHandler<Control>((band, e) =>
        {
            if (e.NewValue is not true || !PhoneLayout.Active) return;
            band.AttachedToVisualTree += (_, _) =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => Spread(band), Avalonia.Threading.DispatcherPriority.Loaded);
        });
    }

    private static void Spread(Control band)
    {
        foreach (var wrap in band.GetVisualDescendants().OfType<WrapPanel>().ToList())
        {
            wrap.SizeChanged -= OnWrapSize;
            wrap.SizeChanged += OnWrapSize;
            Fit(wrap);
        }
        // Un champ de largeur fixe s'étire sur toute la largeur de son bloc.
        foreach (var input in band.GetVisualDescendants().OfType<Control>().Where(IsInput).ToList())
        {
            // Les minimums desktop ne doivent pas imposer 280 points sur un
            // téléphone de 320 points après les marges. Ne pas toucher aux
            // éditeurs internes des templates (date, liste, recherche).
            if (input.TemplatedParent is not null) continue;
            if (!double.IsNaN(input.Width))
                input.SetValue(Layoutable.WidthProperty, double.NaN, BindingPriority.Animation);
            if (input.MinWidth > 0)
                input.SetValue(Layoutable.MinWidthProperty, 0d, BindingPriority.Animation);
            if (input.HorizontalAlignment != HorizontalAlignment.Stretch)
                input.SetValue(Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Stretch, BindingPriority.Animation);
        }
    }

    private static void OnWrapSize(object? sender, SizeChangedEventArgs e)
    {
        if (sender is WrapPanel wrap && Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5) Fit(wrap);
    }

    /// <summary>
    /// Un champ par ligne sur un téléphone ; sur une tablette, deux ou trois champs côte à côte, à largeur égale.
    /// </summary>
    private static void Fit(WrapPanel wrap)
    {
        var width = wrap.Bounds.Width;
        if (width <= 0) return;
        var columns = width >= 900 ? 3 : width >= 560 ? 2 : 1;
        foreach (var child in wrap.Children.OfType<Control>())
        {
            if (child is not Panel || !child.GetVisualDescendants().OfType<Control>().Any(IsInput)) continue;
            var room = FieldWidth(width, columns, child.Margin.Left + child.Margin.Right);
            if (child.MinWidth > room)
                child.SetValue(Layoutable.MinWidthProperty, room, BindingPriority.Animation);
            if (double.IsNaN(child.Width) || Math.Abs(child.Width - room) > 0.5)
                child.SetValue(Layoutable.WidthProperty, room, BindingPriority.Animation);
        }
    }

    public static double FieldWidth(double width, int columns, double margins) =>
        !double.IsFinite(width) || columns <= 0 ? 0 : Math.Max(0, Math.Floor(width / columns) - Math.Max(0, margins));

    private static bool IsInput(Control c) =>
        c is TextBox or ComboBox or DatePicker or CalendarDatePicker or NumericUpDown or AutoCompleteBox;
}
