using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;

namespace Scribbles.E2E.Tests;

/// <summary>
/// Runs bodies on a single dedicated STA thread with its own Dispatcher.
/// No windows are shown and focus is never taken. A single thread (instead
/// of one per test) keeps the shared WPF Application thread-safe when xUnit
/// runs test classes in parallel.
/// </summary>
internal static class WpfTestHost
{
    private static readonly object InitLock = new();
    private static Dispatcher? _dispatcher;

    private static Dispatcher TestDispatcher()
    {
        lock (InitLock)
        {
            if (_dispatcher is null)
            {
                using var ready = new ManualResetEventSlim();
                var thread = new Thread(() =>
                {
                    _dispatcher = Dispatcher.CurrentDispatcher;
                    ready.Set();
                    Dispatcher.Run();
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();
                if (!ready.Wait(TimeSpan.FromSeconds(30)))
                    throw new TimeoutException("STA dispatcher did not start");
            }
            return _dispatcher!;
        }
    }

    public static void Sta(Action body)
    {
        ExceptionDispatchInfo? captured = null;
        TestDispatcher().Invoke(() =>
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
        captured?.Throw();
    }

    public static void Sta(Func<Task> asyncBody)
    {
        ExceptionDispatchInfo? captured = null;
        Func<Task> work = async () =>
        {
            try
            {
                await asyncBody().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
        };
        TestDispatcher().InvokeAsync(work).Task.Unwrap().Wait(TimeSpan.FromMinutes(2));
        captured?.Throw();
    }

    public static void EnsureThemedApp(string theme)
    {
        Dispatcher.CurrentDispatcher.VerifyAccess();
        var app = Application.Current ?? new Application();
        // Stray windows must never kill the shared test app.
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
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
