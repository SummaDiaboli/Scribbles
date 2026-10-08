using System.Runtime.ExceptionServices;
using System.Windows;

namespace Scribbles.E2E.Tests;

/// <summary>Runs a body on its own STA thread with a fresh Application.
/// No windows are shown and focus is never taken.</summary>
internal static class WpfTestHost
{
    public static void Sta(Action body)
    {
        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(2)))
            throw new TimeoutException("STA test timed out");
        captured?.Throw();
    }

    public static void EnsureThemedApp(string theme)
    {
        var app = Application.Current ?? new Application();
        app.Resources.MergedDictionaries.Clear();
        foreach (string file in new[] { $"{theme}Theme.xaml", "Icons.xaml", "SystemBrushes.xaml" })
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/Scribbles.App;component/Themes/{file}", UriKind.Relative)
            });
        }
    }
}
