using UIKit;

namespace Scolia.Phone.iOS;

/// <summary>Point d'entrée natif iPhone et iPad.</summary>
public static class Program
{
    public static void Main(string[] args) =>
        UIApplication.Main(args, null, typeof(AppDelegate));
}
