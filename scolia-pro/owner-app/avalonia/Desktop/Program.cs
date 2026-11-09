using Avalonia;

namespace JPServices.Owner.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppBuilder.Configure<JPServices.Owner.OwnerApp>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
    }
}
