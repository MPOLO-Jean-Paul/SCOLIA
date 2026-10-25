using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Bandeaux de filtres du téléphone : chaque champ, prévu pour une ligne d'ordinateur avec sa largeur fixe,
/// prend toute la largeur de l'écran, l'un sous l'autre. Les boutons gardent leur taille et passent à la ligne.
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
            if (double.IsNaN(input.Width)) continue;
            input.SetValue(Layoutable.WidthProperty, double.NaN, BindingPriority.Animation);
            input.SetValue(Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Stretch, BindingPriority.Animation);
        }
    }

    private static void OnWrapSize(object? sender, SizeChangedEventArgs e)
    {
        if (sender is WrapPanel wrap && Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5) Fit(wrap);
    }

    private static void Fit(WrapPanel wrap)
    {
        var width = wrap.Bounds.Width;
        if (width <= 0) return;
        foreach (var child in wrap.Children.OfType<Control>())
        {
            if (child is not Panel || !child.GetVisualDescendants().OfType<Control>().Any(IsInput)) continue;
            var room = Math.Max(0, width - child.Margin.Left - child.Margin.Right);
            child.SetValue(Layoutable.WidthProperty, room, BindingPriority.Animation);
        }
    }

    private static bool IsInput(Control c) =>
        c is TextBox or ComboBox or DatePicker or CalendarDatePicker or NumericUpDown or AutoCompleteBox;
}
