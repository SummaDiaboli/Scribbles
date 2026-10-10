using System.Drawing;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.UIA3;
using Shouldly;

namespace Scribbles.E2E.Tests;

/// <summary>
/// Visual proof + interaction regression: screenshots for human review,
/// and clicking OK after a sea-drag (mouse-capture trap regression).
/// Screenshots land in %TEMP%\opencode.
/// </summary>
[Collection(DesktopCollection.Name)]
public sealed class VisualProofTests
{
    private static string ShotDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "opencode");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string FindExe()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            foreach (string config in new[] { "Release", "Debug" })
            {
                string exe = Path.Combine(dir.FullName, "Scribbles.App", "bin", config,
                    "net8.0-windows10.0.19041", "publish", "Scribbles.App.exe");
                if (File.Exists(exe))
                    return exe;
            }
        }
        throw new FileNotFoundException("Published Scribbles.App.exe not found. Publish first.");
    }

    private static string FindFixture()
    {
        // A document argument opens the viewer (no args lands on home).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "tests", "fixtures", "sample-text.pdf");
            if (File.Exists(candidate))
                return candidate;
            candidate = Path.Combine(dir.FullName, "fixtures", "sample-text.pdf");
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException("sample-text.pdf fixture not found.");
    }

    private static void KillStale()
    {
        foreach (var stale in System.Diagnostics.Process.GetProcessesByName("Scribbles.App"))
        {
            try { stale.Kill(); stale.WaitForExit(3000); } catch { /* gone */ }
        }
    }

    private static void Capture(dynamic rect, string file)
    {
        int x = Math.Max(0, (int)rect.X), y = Math.Max(0, (int)rect.Y);
        int w = Math.Max(1, (int)rect.Width), h = Math.Max(1, (int)rect.Height);
        using var bmp = new Bitmap(w, h);
        using (var gfx = Graphics.FromImage(bmp))
            gfx.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
        bmp.Save(file);
    }

    private static bool InteractiveEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("SCRIBBLES_INTERACTIVE_E2E"), "1",
            StringComparison.OrdinalIgnoreCase);

    private static T? Retry<T>(Func<T?> find, int attempts = 40) where T : class
    {
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                var result = find();
                if (result is not null)
                    return result;
            }
            catch { /* tree still settling */ }
            Thread.Sleep(250);
        }
        return null;
    }

    [Fact]
    public void FileMenu_Screenshot_ForThemeReview()
    {
        if (!InteractiveEnabled())
            return; // needs a dedicated desktop: drives mouse + focus (see ci.yml)
        KillStale();
        using var app = Application.Launch(FindExe(), FindFixture());
        try
        {
            using var automation = new UIA3Automation();
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(15));
            window.ShouldNotBeNull();
            window.Focus();

            var fileMenu = Retry(() => window.FindFirstDescendant(cf => cf.ByName("File"))?.AsMenuItem());
            fileMenu.ShouldNotBeNull();
            fileMenu!.Click();
            Thread.Sleep(1000);
            Capture(window.BoundingRectangle, Path.Combine(ShotDir(), "menu.png"));

            var settingsItem = Retry(() => window.FindFirstDescendant(cf => cf.ByName("Settings…"))?.AsMenuItem(), attempts: 8);
            settingsItem.ShouldNotBeNull();
            var rect = settingsItem!.BoundingRectangle;
            rect.Inflate(60, 120);
            Capture(rect, Path.Combine(ShotDir(), "menu.png"));
            Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);
        }
        finally
        {
            try { app.Close(); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ColorPicker_DragSea_ThenOkStillWorks()
    {
        if (!InteractiveEnabled())
            return; // needs a dedicated desktop: drives mouse + focus (see ci.yml)
        KillStale();
        using var app = Application.Launch(FindExe(), FindFixture());
        try
        {
            using var automation = new UIA3Automation();
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(15));
            window.ShouldNotBeNull();
            window.Focus();

            var pickerButton = Retry(() => window.FindFirstDescendant(cf => cf.ByName("Custom color…"))?.AsButton());
            pickerButton.ShouldNotBeNull("palette picker button should exist");
            pickerButton!.Click();

            var dialog = Retry(() => window.FindFirstChild(
                cf => cf.ByControlType(ControlType.Window).And(cf.ByName("Pick a color")))?.AsWindow());
            dialog.ShouldNotBeNull("color picker should open");
            dialog!.Focus();

            var sea = Retry(() => dialog.FindFirstDescendant(cf => cf.ByAutomationId("SvField")));
            if (sea is null)
            {
                var tree = string.Join(" | ", dialog.FindAllDescendants().Take(60)
                    .Select(e => $"{e.Properties.ControlType.ValueOrDefault}:{e.Properties.Name.ValueOrDefault}[{e.Properties.AutomationId.ValueOrDefault}]"));
                throw new Xunit.Sdk.XunitException("SV sea should be automatable. Tree: " + tree);
            }
            var r = sea!.BoundingRectangle;
            var start = new System.Drawing.Point((int)(r.X + r.Width * 0.2), (int)(r.Y + r.Height * 0.8));
            var end = new System.Drawing.Point((int)(r.X + r.Width * 0.8), (int)(r.Y + r.Height * 0.2));
            Mouse.MoveTo(start);
            Mouse.Down();
            Mouse.MoveTo(end);
            Mouse.Up();

            Capture(dialog.BoundingRectangle, Path.Combine(ShotDir(), "picker.png"));

            // The regression: a stuck mouse capture used to swallow this click.
            var ok = Retry(() => dialog.FindFirstDescendant(cf => cf.ByName("OK"))?.AsButton());
            ok.ShouldNotBeNull("OK button should exist");
            ok!.Click();

            var gone = Retry(() => window.FindFirstChild(
                cf => cf.ByControlType(ControlType.Window).And(cf.ByName("Pick a color"))), attempts: 8);
            gone.ShouldBeNull("dialog should close after OK");
        }
        finally
        {
            try { app.Close(); } catch { /* best effort */ }
        }
    }
}
