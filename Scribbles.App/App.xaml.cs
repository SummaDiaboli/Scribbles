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
    }

    public static void SaveSettings() => SettingsService.Save(null, Settings);
}
