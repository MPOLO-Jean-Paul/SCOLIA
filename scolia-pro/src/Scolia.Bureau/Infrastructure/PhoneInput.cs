using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Champ de téléphone : seuls les chiffres s'écrivent, et pas plus de 10.
/// Une lettre ou un signe tapé est ignoré ; un numéro collé est nettoyé, et un ancien numéro +243 est ramené à 10 chiffres.
/// </summary>
public static class PhoneInput
{
    public const int Length = 10;

    public static readonly AttachedProperty<bool> DigitsProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("Digits", typeof(PhoneInput));

    public static bool GetDigits(TextBox box) => box.GetValue(DigitsProperty);
    public static void SetDigits(TextBox box, bool value) => box.SetValue(DigitsProperty, value);

    static PhoneInput()
    {
        DigitsProperty.Changed.AddClassHandler<TextBox>((box, e) =>
        {
            box.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
            box.TextChanged -= OnTextChanged;
            if (e.NewValue is not true) return;
            box.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
            box.TextChanged += OnTextChanged;
            Sanitize(box);
        });
    }

    /// <summary>Ne garde que les chiffres, 10 au plus. Un numéro +243 812 345 678 devient 0812345678.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        if (text.TrimStart().StartsWith('+') && digits.StartsWith("243", StringComparison.Ordinal) && digits.Length == 12) digits = "0" + digits[3..];
        return digits.Length > Length ? digits[..Length] : digits;
    }

    private static void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (sender is not TextBox box || string.IsNullOrEmpty(e.Text)) return;
        var typed = new string(e.Text.Where(char.IsAsciiDigit).ToArray());
        var current = box.Text?.Length ?? 0;
        var selected = Math.Abs(box.SelectionEnd - box.SelectionStart);
        var room = Length - (current - selected);
        if (typed.Length == 0 || room <= 0) { e.Handled = true; return; }
        if (typed.Length > room) typed = typed[..room];
        if (typed != e.Text) e.Text = typed;
    }

    private static void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box) Sanitize(box);
    }

    private static void Sanitize(TextBox box)
    {
        var text = box.Text ?? string.Empty;
        var clean = Clean(text);
        if (clean == text) return;
        var caret = box.CaretIndex;
        box.Text = clean;
        box.CaretIndex = Math.Min(caret, clean.Length);
    }
}
