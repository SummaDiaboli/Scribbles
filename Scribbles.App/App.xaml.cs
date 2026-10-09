using System.IO;
using System.Windows;
using Scribbles.App.Services;
using Scribbles.Core;

namespace Scribbles.App;

/// <summary>Interaction logic for App.xaml</summary>
public partial class App : Application
{
    public static ThemeService Themes { get; } = new();

    public static AppSettings Settings { get; set; } = AppSettings.Default;

    protected override void OnStartup(StartupEventArgs e)
    {
        Settings = SettingsService.Load();
        base.OnStartup(e);
        Themes.Apply(Settings.Theme);
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Scribbles");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "crash.log"),
                    $"[{DateTime.Now:o}] {args.Exception}\n");
            }
            catch { /* logging must not throw */ }
            MessageBox.Show(
                $"Something went wrong:\n{args.Exception.GetBaseException().Message}\n\nDetails saved to %LocalAppData%\\Scribbles\\crash.log",
                "Scribbles", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown();
        };

        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && System.IO.File.Exists(args[1]))
        {
            var viewer = new MainWindow();
            viewer.Show();
            viewer.Open(args[1]);
        }
        else
        {
            new HomeWindow().Show();
        }
    }

    public static void SaveSettings() => SettingsService.Save(null, Settings);
}
