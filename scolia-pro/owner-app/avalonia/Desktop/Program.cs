using Avalonia;

namespace JPServices.Owner.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        OwnerVault.Current = new OwnerVault(new WindowsProtector(), Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JP-Services", "SCOLIA Console"));
        AppBuilder.Configure<JPServices.Owner.OwnerApp>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
    }
}
