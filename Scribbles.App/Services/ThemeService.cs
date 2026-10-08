using System.Windows;
using Scribbles.Core;

namespace Scribbles.App.Services;

/// <summary>Light/dark/system theme manager. The choice itself is persisted
/// in AppSettings (see SettingsService); this class only applies resources.</summary>
public sealed class ThemeService
{
    public AppTheme Current { get; private set; } = AppTheme.System;

    public event EventHandler<AppTheme>? ThemeChanged;

    public void Apply(AppTheme theme)
    {
        Current = theme;
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        dictionaries.Clear();
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"/Scribbles.App;component/Themes/{Resolve(theme)}Theme.xaml", UriKind.Relative)
        });
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Scribbles.App;component/Themes/Icons.xaml", UriKind.Relative)
        });
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Scribbles.App;component/Themes/SystemBrushes.xaml", UriKind.Relative)
        });
        ThemeChanged?.Invoke(this, theme);
    }

    private static string Resolve(AppTheme theme)
    {
        if (theme == AppTheme.System)
        {
            // Follow Windows app theme approx: default to Light unless OS dark mode registry says otherwise.
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var light = key?.GetValue("AppsUseLightTheme");
                if (light is int i && i == 0)
                    return "Dark";
            }
            catch { }
            return "Light";
        }
        return theme.ToString();
    }
}
