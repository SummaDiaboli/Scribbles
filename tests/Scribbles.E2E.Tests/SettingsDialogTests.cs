using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Shouldly;

namespace Scribbles.E2E.Tests;

/// <summary>First real E2E: the app opens and File → Settings shows the dialog.</summary>
public sealed class SettingsDialogTests
{
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
        // A document argument opens the viewer (no args lands on home, which
        // has no File menu since M5).
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

    [Fact]
    public void SettingsDialog_OpensFromFileMenu_AndCancels()
    {
        if (!InteractiveEnabled())
            return; // needs a dedicated desktop: drives mouse + focus (see ci.yml)
        // A stale instance (e.g. from an aborted run) steals focus and
        // auto-closes open menus, so always start clean.
        foreach (var stale in System.Diagnostics.Process.GetProcessesByName("Scribbles.App"))
        {
            try { stale.Kill(); stale.WaitForExit(3000); } catch { /* gone */ }
        }

        using var app = Application.Launch(FindExe(), FindFixture());
        try
        {
            using var automation = new UIA3Automation();
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(15));
            window.ShouldNotBeNull();
            window.Focus();

            var fileMenu = Retry(() => window.FindFirstDescendant(cf => cf.ByName("File"))?.AsMenuItem());
            fileMenu.ShouldNotBeNull("File menu should exist");
            fileMenu!.Click();

            var settingsItem = Retry(() => window.FindFirstDescendant(cf => cf.ByName("Settings…"))?.AsMenuItem(), attempts: 40);
            settingsItem.ShouldNotBeNull("Settings menu item should exist");
            settingsItem!.Click();

            var dialog = Retry(() => window.FindFirstChild(cf => cf.ByControlType(ControlType.Window).And(cf.ByName("Settings")))?.AsWindow(), attempts: 40);
            if (dialog is null)
            {
                var tree = string.Join(" | ", window.FindAllDescendants().Take(60)
                    .Select(e => $"{e.ControlType}:{e.Name}"));
                throw new Xunit.Sdk.XunitException("Settings dialog should open. Tree: " + tree);
            }
            dialog!.Close();
        }
        finally
        {
            try { app.Close(); } catch { /* best effort */ }
        }
    }

    private static bool InteractiveEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("SCRIBBLES_INTERACTIVE_E2E"), "1",
            StringComparison.OrdinalIgnoreCase);

    private static T? Retry<T>(Func<T?> find, int attempts = 20) where T : class
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
}
