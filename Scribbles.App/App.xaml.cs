using System.IO;
using System.Windows;
using Velopack;
using Scribbles.App.Services;
using Scribbles.Core;

namespace Scribbles.App;

/// <summary>Interaction logic for App.xaml</summary>
public partial class App : Application
{
    public static ThemeService Themes { get; } = new();

    public static AppSettings Settings { get; set; } = AppSettings.Default;

    public static AppUpdater Updater { get; } = new();

    /// <summary>Downloaded update waiting for a restart (null when up to date).</summary>
    public static Velopack.UpdateInfo? PendingUpdate { get; private set; }

    /// <summary>Raised on the UI thread when an update finishes downloading.</summary>
    public static event Action? UpdateStaged;

    [STAThread]
    private static void Main(string[] args)
    {
        // First, always: install/update/uninstall hooks run and exit here.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ =>
            {
                if (Environment.ProcessPath is not null)
                    FileAssociationEntries.Remove(Environment.ProcessPath);
            })
            .Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

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

        // Open-With presence (never the default handler): installed app only,
        // re-applied each launch so the path stays current across updates.
        if (Environment.ProcessPath is not null && AppUpdater.IsInstalled(Settings.UpdateChannel))
            FileAssociationEntries.Apply(Environment.ProcessPath);

        CheckForUpdatesInBackground();
    }

    /// <summary>
    /// Silent background update: check, download, then stage for restart.
    /// Dev builds (not installed) and offline machines skip without a sound.
    /// </summary>
    private static async void CheckForUpdatesInBackground()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            var update = await Updater.CheckAsync(Settings.UpdateChannel).ConfigureAwait(false);
            if (update is null)
                return;
            if (!await Updater.DownloadAsync(update, Settings.UpdateChannel).ConfigureAwait(false))
                return;
            PendingUpdate = update;
            await Current.Dispatcher.InvokeAsync(() => UpdateStaged?.Invoke());
        }
        catch
        {
            // Update checks must never disturb the app.
        }
    }

    public static void SaveSettings() => SettingsService.Save(null, Settings);
}
