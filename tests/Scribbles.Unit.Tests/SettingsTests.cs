using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

/// <summary>
/// TDD RED (2026-10-08): settings persistence, palette rules, window
/// placement clamping, pen-profile planning. Types don't exist yet —
/// these MUST fail to compile/run before the implementation lands.
/// </summary>
public sealed class SettingsTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");

    [Fact]
    public void Settings_Defaults_AreSane()
    {
        var defaults = AppSettings.Default;
        defaults.Theme.ShouldBe(AppTheme.System);
        defaults.PenSizeDip.ShouldBe(3.5);
        defaults.AutosaveSeconds.ShouldBe(30);
        defaults.Palette.Length.ShouldBeGreaterThan(0);
        defaults.Window.ShouldNotBeNull();
    }

    [Fact]
    public void SettingsService_RoundTrip_PreservesValues()
    {
        string path = TempPath();
        var settings = AppSettings.Default with
        {
            Theme = AppTheme.Dark,
            PenColorArgb = 0xFFD13438,
            PenType = PenKind.Fountain,
            AutosaveSeconds = 60,
        };
        SettingsService.Save(path, settings);
        var loaded = SettingsService.Load(path);
        loaded.ShouldBe(settings);
    }

    [Fact]
    public void SettingsService_LegacyFile_MissingFieldsFallBackToDefaults()
    {
        // Real crash (2026-10-08): a settings.json written before RecentFiles
        // existed deserializes with null arrays and kills startup.
        string path = TempPath();
        File.WriteAllText(path, """{"theme":1,"penColorArgb":4278190080}""");
        var loaded = SettingsService.Load(path);
        loaded.Theme.ShouldBe(AppTheme.Dark);
        loaded.Palette.ShouldNotBeNull();
        loaded.Palette.ShouldNotBeEmpty();
        loaded.RecentFiles.ShouldNotBeNull();
        loaded.RecentFiles.ShouldBeEmpty();
    }

    [Fact]
    public void SettingsService_MissingFile_ReturnsDefaults()
    {
        SettingsService.Load(TempPath()).ShouldBe(AppSettings.Default);
    }

    [Fact]
    public void SettingsService_CorruptFile_ReturnsDefaults()
    {
        string path = TempPath();
        File.WriteAllText(path, "{ this is not json");
        SettingsService.Load(path).ShouldBe(AppSettings.Default);
    }

    [Fact]
    public void SettingsService_CreatesDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "settings.json");
        SettingsService.Save(path, AppSettings.Default);
        File.Exists(path).ShouldBeTrue();
    }

    [Fact]
    public void Palette_Add_PutsFirst_Dedupes_CapsAtTwelve()
    {
        var palette = Array.Empty<string>();
        palette = ColorPalette.Add(palette, "#FF0000");
        palette = ColorPalette.Add(palette, "#00FF00");
        palette = ColorPalette.Add(palette, "#FF0000"); // duplicate moves to front
        palette[0].ShouldBe("#FFFF0000");
        palette.Length.ShouldBe(2);
        for (int i = 0; i < 20; i++)
            palette = ColorPalette.Add(palette, $"#FF{i:00}0000");
        palette.Length.ShouldBe(12);
        palette[0].ShouldBe(ColorPalette.Normalize("#FF190000"));
    }

    [Fact]
    public void Palette_Normalize_HandlesShortHex_AndRejectsGarbage()
    {
        ColorPalette.Normalize("#f00").ShouldBe("#FFFF0000");
        ColorPalette.Normalize("FF005FB8").ShouldBe("#FF005FB8");
        ColorPalette.TryNormalize("banana", out _).ShouldBeFalse();
    }

    [Fact]
    public void Palette_Remove_DropsColor()
    {
        var palette = new[] { "#FFFF0000", "#FF00FF00" };
        ColorPalette.Remove(palette, "#FFFF0000").ShouldBe(new[] { "#FF00FF00" });
    }

    [Fact]
    public void WindowPlacement_OffScreen_ClampedToVisible()
    {
        var screen = new ScreenBounds(0, 0, 1920, 1080);
        var lost = new WindowPlacement(-5000, -5000, 1000, 700, false);
        var fixed_ = WindowPlacement.ClampToVisible(lost, screen);
        fixed_.Left.ShouldBeGreaterThanOrEqualTo(screen.Left);
        fixed_.Top.ShouldBeGreaterThanOrEqualTo(screen.Top);
        fixed_.Left.ShouldBeLessThan(screen.Left + screen.Width);
        fixed_.Top.ShouldBeLessThan(screen.Top + screen.Height);
    }

    [Fact]
    public void WindowPlacement_Oversized_ShrinksToScreen()
    {
        var screen = new ScreenBounds(0, 0, 1920, 1080);
        var huge = new WindowPlacement(0, 0, 5000, 4000, false);
        var fixed_ = WindowPlacement.ClampToVisible(huge, screen);
        fixed_.Width.ShouldBeLessThanOrEqualTo(screen.Width);
        fixed_.Height.ShouldBeLessThanOrEqualTo(screen.Height);
    }

    [Fact]
    public void WindowPlacement_Valid_Untouched()
    {
        var screen = new ScreenBounds(0, 0, 1920, 1080);
        var good = new WindowPlacement(100, 100, 1000, 700, false);
        WindowPlacement.ClampToVisible(good, screen).ShouldBe(good);
    }

    [Theory]
    [InlineData(PenKind.Ballpoint, false, true)]
    [InlineData(PenKind.Fountain, false, false)]
    [InlineData(PenKind.Highlighter, true, false)]
    public void PenProfile_PlansAttributes(PenKind kind, bool isHighlighter, bool ignorePressure)
    {
        var spec = PenProfile.Plan(kind);
        spec.IsHighlighter.ShouldBe(isHighlighter);
        spec.IgnorePressure.ShouldBe(ignorePressure);
    }

    [Fact]
    public void PenProfile_Highlighter_IsTranslucentRectangle()
    {
        var spec = PenProfile.Plan(PenKind.Highlighter);
        spec.RectangularTip.ShouldBeTrue();
        spec.Opacity.ShouldBeLessThan(1.0);
    }
}
