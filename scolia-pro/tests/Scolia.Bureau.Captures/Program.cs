using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Scolia.Bureau;
using Scolia.Bureau.Api;
using Scolia.Bureau.ViewModels;

// Usage : dotnet run -- <dossier_de_sortie> <adresse_du_serveur> <mot_de_passe>
var output = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var server = new Uri(args.Length > 1 ? args[1] : "http://localhost:5293/");
var password = args.Length > 2 ? args[2] : "Scolia2026!";
Directory.CreateDirectory(output);

Scolia.Bureau.Program.BuildAvaloniaApp()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

// Attend une tâche tout en faisant tourner la boucle d'affichage de l'interface.
static T Result<T>(Task<T> task)
{
    while (!task.IsCompleted)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(10);
    }
    return task.Result;
}

static void Wait(Task task)
{
    while (!task.IsCompleted)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(10);
    }
    task.GetAwaiter().GetResult();
}

void Save(Avalonia.Controls.Window window, string name)
{
    // Plusieurs cycles de rendu : la mise en page des listes se termine après le premier.
    for (var i = 0; i < 5; i++)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(30);
    }
    var frame = window.CaptureRenderedFrame();
    if (frame is null) throw new InvalidOperationException($"Rendu indisponible pour {name}.");
    frame.Save(Path.Combine(output, name));
    Console.WriteLine($"capturé : {name} ({frame.PixelSize.Width}x{frame.PixelSize.Height})");
}

if (Environment.GetEnvironmentVariable("SCOLIA_DIAG") == "1")
{
    var diag = new Avalonia.Controls.Window { Width = 300, Height = 200, Content = new Material.Icons.Avalonia.MaterialIcon { Kind = Material.Icons.MaterialIconKind.Home, Width = 120, Height = 120, Foreground = Avalonia.Media.Brushes.Red } };
    diag.Show();
    Save(diag, "diag-icone.png");
    return;
}
var api = new ScoliaClient(server);
var shell = new ShellViewModel(api);
var window = new MainWindow { DataContext = shell, Width = 1280, Height = 800 };
window.Show();

Save(window, "native-01-connexion.png");

shell.Login.UserName = "admin";
shell.Login.Password = password;
var session = Result(api.LoginAsync("admin", password));
Wait(shell.OpenSessionAsync(session));
Save(window, "native-02-tableau-de-bord.png");

Wait(shell.ShowAsync(shell.Students!));
Save(window, "native-03-apprenants.png");

Wait(shell.ShowAsync(shell.Receipts!));
Save(window, "native-04-journal-des-recus.png");

window.Close();
Console.WriteLine("terminé");
