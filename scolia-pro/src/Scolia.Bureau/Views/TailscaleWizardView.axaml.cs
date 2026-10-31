using Avalonia.Controls;
using Avalonia.Input.Platform;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.Views;

/// <summary>Assistant Tailscale commun : il se lie toujours à l'assistant unique de l'application.</summary>
public partial class TailscaleWizardView : UserControl
{
    public TailscaleWizardView()
    {
        InitializeComponent();
        DataContext = TailscaleInstaller.Instance;
        AttachedToVisualTree += (_, _) =>
        {
            // Copie de l'adresse privée dans le presse-papiers du système.
            TailscaleInstaller.Instance.CopyHandler ??= async text =>
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
            };
        };
    }
}
