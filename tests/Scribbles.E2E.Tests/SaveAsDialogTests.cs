using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Scribbles.Pdf;
using Shouldly;
using static Scribbles.E2E.Tests.WpfTestHost;

namespace Scribbles.E2E.Tests;

/// <summary>
/// End-to-end: File → Save As must raise a native Save dialog — never
/// silently overwrite the original (regression: the CurrentPath fallback
/// defeated Save As and overwrote the source file).
/// </summary>
[Collection(DesktopCollection.Name)]
public sealed class SaveAsDialogTests
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_CLOSE = 0x0010;

    [Fact]
    public void SaveAs_RaisesNativeDialog() => Sta(async () =>
    {
        EnsureThemedApp("Dark");
        // PdfSharp-valid doc (the hand-rolled text fixture is PdfPig-only).
        // Temp paths throughout: a regression must never touch the repo.
        string copy = Path.Combine(Path.GetTempPath(), $"scribbles-saveas-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(copy, NotebookDocuments.CreateBlank(new[] { (595.0, 842.0) }));
        try
        {
            var window = new Scribbles.App.MainWindow();
            try
            {
                window.Measure(new Size(1400, 900));
                window.Arrange(new Rect(0, 0, 1400, 900));
                window.UpdateLayout();
                window.OnLoadedInternal();
                window.Open(copy);

                // The runner is slower than dev: wait for the open to land
                // (status names the page count) instead of a fixed delay.
                var vmField = typeof(Scribbles.App.MainWindow).GetField(
                    "_vm", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                string Status()
                {
                    dynamic? vm = vmField?.GetValue(window);
                    return vm?.Status as string ?? "";
                }
                bool opened = false;
                for (int i = 0; i < 600 && !opened; i++)
                {
                    await Task.Delay(100);
                    opened = Status().Contains("page(s)");
                }
                opened.ShouldBeTrue($"document must finish opening (status was: {Status()})");

                var items = new List<MenuItem>();
                var queue = new Queue<DependencyObject>();
                queue.Enqueue(window);
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (current is MenuItem mi)
                        items.Add(mi);
                    foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
                        queue.Enqueue(child);
                }
                var saveAs = items.First(i => (i.Header as string)?.Contains("Save _As") == true);

                // Raise on a pool thread: the modal dialog blocks the STA thread.
                var click = Task.Run(() => saveAs.Dispatcher.Invoke(() =>
                    saveAs.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent))));

                IntPtr dlg = IntPtr.Zero;
                for (int i = 0; i < 300 && dlg == IntPtr.Zero; i++)
                {
                    await Task.Delay(100);
                    dlg = FindWindow("#32770", "Save As");
                }
                try
                {
                    dlg.ShouldNotBe(IntPtr.Zero, "Save As must raise the native save dialog");
                }
                finally
                {
                    if (dlg != IntPtr.Zero)
                        PostMessage(dlg, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    await click.WaitAsync(TimeSpan.FromSeconds(10));
                }
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            File.Delete(copy);
        }
    });
}
