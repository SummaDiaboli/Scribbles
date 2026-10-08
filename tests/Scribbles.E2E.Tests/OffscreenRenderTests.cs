using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shouldly;
using static Scribbles.E2E.Tests.WpfTestHost;

namespace Scribbles.E2E.Tests;

/// <summary>
/// Off-screen render proof (own STA thread, no windows shown, no focus
/// stolen): expands the real templates, paints to bitmaps, and asserts
/// theme colors pixel-by-pixel.
/// </summary>
public sealed class OffscreenRenderTests
{

    private static BitmapSource Render(FrameworkElement element, double width, double height)
    {
        element.Measure(new System.Windows.Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(element);
        return bmp;
    }

    private static int CountNear(BitmapSource bmp, byte r, byte g, byte b, int tolerance)
    {
        int w = bmp.PixelWidth, h = bmp.PixelHeight;
        byte[] pixels = new byte[w * h * 4];
        bmp.CopyPixels(pixels, w * 4, 0);
        int count = 0;
        for (int i = 0; i < w * h; i++)
        {
            if (Math.Abs(pixels[i * 4 + 2] - r) <= tolerance
                && Math.Abs(pixels[i * 4 + 1] - g) <= tolerance
                && Math.Abs(pixels[i * 4] - b) <= tolerance)
                count++;
        }
        return count;
    }

    [Fact]
    public void DarkMenuDropdown_RendersLightTextOnDarkSurface() => Sta(() =>
    {
        EnsureThemedApp("Dark");

        // Parent linkage gives Roles without any visible window:
        // menu > file (TopLevelHeader) > rows (SubmenuItem).
        var menu = new Menu();
        var file = new MenuItem { Header = "_File" };
        var open = new MenuItem { Header = "_Open PDF…" };
        var settings = new MenuItem { Header = "_Settings…" };
        file.Items.Add(open);
        file.Items.Add(new Separator());
        file.Items.Add(settings);
        menu.Items.Add(file);

        // Assign styles after parenting so Role triggers evaluate correctly.
        var menuStyle = (Style)Application.Current.FindResource(typeof(MenuItem));
        foreach (var item in new MenuItem[] { file, open, settings })
            item.Style = menuStyle;

        // Bypass Role triggers (they need a live tree): pin the exact
        // templates the roles resolve to in the running app.
        file.Template = (ControlTemplate)file.FindResource("TopMenuItemTemplate");
        open.Template = (ControlTemplate)open.FindResource("SubmenuItemTemplate");

        // Dropdown panel background (the reported bug: unreadable dropdown).
        file.ApplyTemplate();
        var popup = file.Template?.FindName("Dropdown", file) as Popup;
        popup.ShouldNotBeNull("top-level template should carry the dropdown popup");
        var panel = (Border)popup!.Child;
        var panelBmp = Render(panel, 260, 200);
        int total = panelBmp.PixelWidth * panelBmp.PixelHeight;
        int dark = CountNear(panelBmp, 0x2D, 0x2D, 0x2D, 12);
        dark.ShouldBeGreaterThan(total / 2, "popup background should be the dark surface");

        // Submenu row: light text painted (separate elements need their own layout).
        var rowBmp = Render(open, 240, 32);
        int light = CountNear(rowBmp, 0xF3, 0xF3, 0xF3, 24);
        light.ShouldBeGreaterThan(60, "menu text should render in the light foreground");
    });

    [Fact]
    public void MenuRoleTriggers_ResolveTopAndSubmenuTemplates() => Sta(() =>
    {
        EnsureThemedApp("Dark");

        // Forced layout (never shown): runs full parenting so Role triggers
        // evaluate, without any window that could shut the test app down.
        var menu = new Menu { Width = 800, Height = 600 };
        var file = new MenuItem { Header = "_File" };
        var open = new MenuItem { Header = "_Open PDF…" };
        file.Items.Add(open);
        menu.Items.Add(file);

        menu.Measure(new Size(800, 600));
        menu.Arrange(new Rect(0, 0, 800, 600));
        menu.UpdateLayout();
        file.ApplyTemplate();
        open.ApplyTemplate();
        file.Template.FindName("Dropdown", file).ShouldNotBeNull("File should use the top-level template");
        open.Template.FindName("Submenu", open).ShouldNotBeNull("Open should use the submenu template");
    });

    [Fact]
    public void ColorPicker_RendersSeaAndPreview() => Sta(() =>
    {
        EnsureThemedApp("Dark");

        // Constructed but never shown: no focus stolen.
        var dialog = new Scribbles.App.ColorPickerDialog(Colors.Red, new[] { "#FFFF0000" });
        var root = (FrameworkElement)dialog.Content;
        var bmp = Render(root, 560, 480);

        // Sea region (left column): must contain saturated color, not gray mush.
        int w = bmp.PixelWidth, h = bmp.PixelHeight;
        byte[] pixels = new byte[w * h * 4];
        bmp.CopyPixels(pixels, w * 4, 0);
        int vivid = 0;
        for (int y = 16; y < 236; y++)
        {
            for (int x = 16; x < 236; x++)
            {
                int i = (y * w + x) * 4;
                int spread = Math.Max(pixels[i + 2], Math.Max(pixels[i + 1], pixels[i]))
                    - Math.Min(pixels[i + 2], Math.Min(pixels[i + 1], pixels[i]));
                if (spread > 100)
                    vivid++;
            }
        }
        vivid.ShouldBeGreaterThan(5000, "color sea should render saturated color");

        // Preview rect shows the selected color (red): sample its middle.
        int previewRed = 0;
        for (int y = 250; y < 280; y++)
        {
            for (int x = 16; x < 80; x++)
            {
                int i = (y * w + x) * 4;
                if (pixels[i + 2] > 180 && pixels[i + 1] < 80 && pixels[i] < 80)
                    previewRed++;
            }
        }
        previewRed.ShouldBeGreaterThan(200, "preview should show the picked red");

        // Dialog labels must render in the theme foreground (the reported bug).
        int labelLight = CountNear(bmp, 0xF3, 0xF3, 0xF3, 24);
        labelLight.ShouldBeGreaterThan(300, "dialog text should render in the light foreground");

        // Hue rail (below the sea): must show a spread of hues, not a white bar.
        var hues = new HashSet<int>();
        for (int x = 24; x < 228; x += 6)
        {
            int i = (255 * w + x) * 4;
            byte r = pixels[i + 2], g = pixels[i + 1], b = pixels[i];
            int spread = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
            if (spread < 80)
                continue; // thumb / edges
            var hsv = Scribbles.Core.HsvColor.FromRgb(r, g, b);
            hues.Add((int)(hsv.H / 30));
        }
        hues.Count.ShouldBeGreaterThanOrEqualTo(5, "hue rail should span distinct hues");
    });

    [Fact]
    public void DarkButton_RendersLightTextOnDarkFace() => Sta(() =>
    {
        EnsureThemedApp("Dark");
        var button = new Button { Content = "Add to palette", Padding = new Thickness(8, 2, 8, 2) };
        var bmp = Render(button, 140, 32);
        int total = bmp.PixelWidth * bmp.PixelHeight;
        // Face follows the dark surface, not the OS light chrome.
        int dark = CountNear(bmp, 0x2D, 0x2D, 0x2D, 12);
        dark.ShouldBeGreaterThan(total / 2, "button face should be the dark surface");
        int light = CountNear(bmp, 0xF3, 0xF3, 0xF3, 24);
        light.ShouldBeGreaterThan(60, "button text should render in the light foreground");
    });

    [Fact]
    public void DarkComboRow_RendersLightTextOnDarkSurface() => Sta(() =>
    {
        EnsureThemedApp("Dark");

        var row = new ComboBoxItem
        {
            Content = "100%",
            Style = (Style)Application.Current.FindResource(typeof(ComboBoxItem)),
        };
        var bmp = Render(row, 120, 28);
        int total = bmp.PixelWidth * bmp.PixelHeight;
        int dark = CountNear(bmp, 0x2D, 0x2D, 0x2D, 16) + CountNear(bmp, 0x00, 0x00, 0x00, 8);
        dark.ShouldBeLessThan(total, "row must paint something");
        int light = CountNear(bmp, 0xF3, 0xF3, 0xF3, 24);
        light.ShouldBeGreaterThan(20, "combo text should render in the light foreground");
    });
}
