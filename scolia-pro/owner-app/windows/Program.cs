using System;
using System.Windows.Forms;

namespace JPServices.Owner;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new OwnerForm());
    }
}
