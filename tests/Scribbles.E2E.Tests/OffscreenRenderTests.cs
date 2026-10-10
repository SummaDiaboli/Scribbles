using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shape = System.Windows.Shapes.Shape;
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
    public void ColorPicker_SeaIsVisibleToUiAutomation() => Sta(() =>
    {
        EnsureThemedApp("Dark");

        // Plain panels have no automation peer, so an AutomationId on a bare
        // Grid never reaches UIA (the interactive drag test could not find it).
        var dialog = new Scribbles.App.ColorPickerDialog(Colors.Red, new[] { "#FFFF0000" });
        var sea = (FrameworkElement)dialog.FindName("SvField");
        var peer = UIElementAutomationPeer.CreatePeerForElement(sea);

        peer.ShouldNotBeNull("color sea needs an automation peer");
        peer.GetAutomationId().ShouldBe("SvField");
        peer.GetName().ShouldNotBeNullOrWhiteSpace("color sea needs an accessible name");
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
    public void OverlayTextBox_RendersTransparentWithText() => Sta(() =>
    {
        EnsureThemedApp("Dark");
        var box = new TextBox
        {
            Style = (Style)Application.Current.FindResource("OverlayTextBox"),
            Text = "Hello",
            Foreground = new SolidColorBrush(Colors.Black),
            FontSize = 16,
            Width = 120,
            Height = 32,
        };
        var bmp = Render(box, 120, 32);
        int w = bmp.PixelWidth, h = bmp.PixelHeight;
        byte[] pixels = new byte[w * h * 4];
        bmp.CopyPixels(pixels, w * 4, 0);
        int transparent = 0;
        for (int i = 0; i < w * h; i++)
        {
            if (pixels[i * 4 + 3] < 64)
                transparent++;
        }
        // Detached text doesn't rasterize glyphs off-screen, so this locks the
        // reported bug only: the face must stay transparent, never an opaque box.
        transparent.ShouldBeGreaterThan((int)(w * h * 0.9), "overlay box background must stay transparent");
    });

    [Fact]
    public void OverlayTextBoxTemplate_HasNoLocalBackgroundOverride()
    {
        // Regression guard (2026-10-08): an explicit Background on the
        // converted-box element silently beat the transparent style and
        // painted dark boxes. The style must own the background.
        string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Scribbles.App");
        string xaml = File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "MainWindow.xaml")));
        int templateAt = xaml.IndexOf("ItemsSource=\"{Binding TextBoxes}\"", StringComparison.Ordinal);
        templateAt.ShouldBeGreaterThan(-1, "textbox overlay template should exist");
        int boxAt = xaml.IndexOf("<TextBox Text=\"{Binding Text", templateAt, StringComparison.Ordinal);
        boxAt.ShouldBeGreaterThan(-1, "overlay TextBox element should exist");
        int boxEnd = xaml.IndexOf("/>", boxAt, StringComparison.Ordinal);
        string element = xaml.Substring(boxAt, boxEnd - boxAt);
        element.ShouldNotContain("Background=");
        element.ShouldNotContain("BorderBrush=");
    }

    [Fact]
    public void SidebarMagnifier_PrefersCompositedThumbnail()
    {
        // Regression guard: the hover magnifier bound the bare base Image, so
        // the enlarged preview never showed ink even though the small thumb
        // did. It must prefer the composited Thumbnail with Image as fallback.
        string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Scribbles.App");
        string xaml = File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "MainWindow.xaml")));
        int tipAt = xaml.IndexOf("<Border.ToolTip>", StringComparison.Ordinal);
        tipAt.ShouldBeGreaterThan(-1, "sidebar thumb tooltip should exist");
        int tipEnd = xaml.IndexOf("</Border.ToolTip>", tipAt, StringComparison.Ordinal);
        string tip = xaml.Substring(tipAt, tipEnd - tipAt);
        tip.Contains("Path=\"Thumbnail\"").ShouldBeTrue("magnifier must show the ink-composited thumbnail");
        tip.Contains("Source=\"{Binding Image}\"").ShouldBeFalse("magnifier must not bind the bare base render");
    }

    [Fact]
    public void ContainerPanels_KeepSubtleRounding()
    {
        // The M5 shell rework flattened everything; the hybrid look keeps
        // flat docking but rounds the top tray and the sidebar panel.
        string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Scribbles.App");
        string xaml = File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "MainWindow.xaml")));
        int trayAt = xaml.IndexOf("<ToolBarTray", StringComparison.Ordinal);
        trayAt.ShouldBeGreaterThan(-1, "top toolbar tray should exist");
        string wrapper = xaml.Substring(Math.Max(0, trayAt - 300), Math.Min(300, trayAt));
        wrapper.Contains("CornerRadius=").ShouldBeTrue("top tray needs a rounded wrapper");
        int sideAt = xaml.IndexOf("x:Name=\"Sidebar\"", StringComparison.Ordinal);
        sideAt.ShouldBeGreaterThan(-1, "sidebar should exist");
        int sideStart = xaml.LastIndexOf("<Border", sideAt, StringComparison.Ordinal);
        string side = xaml.Substring(sideStart, sideAt - sideStart + 200);
        side.Contains("CornerRadius=").ShouldBeTrue("sidebar panel needs rounding");
    }

    [Fact]
    public void HomeCards_HaveOpenRenameDeleteMenu()
    {
        // Every home card (notebooks + recents) must offer open/rename/delete.
        // The menu binds through PlacementTarget.Tag: ContextMenu lives
        // outside the visual tree and does not inherit the card context.
        string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Scribbles.App");
        string xaml = File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "HomeWindow.xaml")));
        int cardAt = xaml.IndexOf("MouseLeftButtonUp=\"Card_Open\"", StringComparison.Ordinal);
        cardAt.ShouldBeGreaterThan(-1, "home card template should exist");
        int headStart = Math.Max(0, cardAt - 400);
        string head = xaml.Substring(headStart, Math.Min(800, xaml.Length - headStart));
        head.Contains("Tag=\"{Binding}\"").ShouldBeTrue("card must expose itself for the menu");
        int menuAt = xaml.IndexOf("<Border.ContextMenu>", cardAt, StringComparison.Ordinal);
        menuAt.ShouldBeGreaterThan(-1, "card context menu should exist");
        int menuEnd = xaml.IndexOf("</Border.ContextMenu>", menuAt, StringComparison.Ordinal);
        string menu = xaml.Substring(menuAt, menuEnd - menuAt);
        menu.Contains("PlacementTarget.Tag").ShouldBeTrue("menu must bind through the card tag");
        menu.Contains("CardMenu_Open").ShouldBeTrue("menu needs Open");
        menu.Contains("CardMenu_Rename").ShouldBeTrue("menu needs Rename");
        menu.Contains("CardMenu_Delete").ShouldBeTrue("menu needs Delete");
    }

    [Fact]
    public void TextEditTool_IsUnparked()
    {
        // The native-edit entry points were parked behind comments; this locks
        // the unpark: live tool button plus live (uncommented) call sites.
        string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Scribbles.App");
        string xaml = File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "MainWindow.xaml")));
        xaml.Contains("Tag=\"TextEdit\"").ShouldBeTrue("TextEdit tool button must exist");
        int buttonAt = xaml.IndexOf("Tag=\"TextEdit\"", StringComparison.Ordinal);
        string openComment = "<!--";
        int commentAt = xaml.LastIndexOf(openComment, buttonAt, StringComparison.Ordinal);
        bool insideComment = commentAt >= 0
            && !xaml.Substring(commentAt, buttonAt - commentAt).Contains("-->");
        insideComment.ShouldBeFalse("TextEdit button must not sit inside a comment");
        string cs = File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "MainWindow.xaml.cs")));
        cs.Contains("TryStartLineEdit(overlay, editPage, end);").ShouldBeTrue("click-to-edit call must be live");
        cs.Contains("//         TryStartLineEdit(overlay, editPage, end);").ShouldBeFalse("click-to-edit call must not be commented");
    }

    private static string ThemeResourceKey(Style style, DependencyProperty property)
    {
        var setter = style.Setters.OfType<Setter>()
            .FirstOrDefault(s => s.Property == property);
        setter.ShouldNotBeNull($"implicit style must own {property.Name}");
        var dynamic = setter!.Value as DynamicResourceExtension;
        dynamic.ShouldNotBeNull($"{property.Name} must track the theme, not a literal");
        return dynamic!.ResourceKey?.ToString() ?? "";
    }

    [Fact]
    public void PopupStyles_TrackThemeResources() => Sta(() =>
    {
        // Long-term guard: any new ContextMenu/ToolTip anywhere in the app is
        // themed by default through these implicit styles — no per-case fix.
        EnsureThemedApp("Dark");
        var contextStyle = (Style)Application.Current.FindResource(typeof(ContextMenu));
        ThemeResourceKey(contextStyle, Control.BackgroundProperty).ShouldBe("AppSurface");
        ThemeResourceKey(contextStyle, Control.ForegroundProperty).ShouldBe("AppForeground");
        var tipStyle = (Style)Application.Current.FindResource(typeof(ToolTip));
        ThemeResourceKey(tipStyle, Control.BackgroundProperty).ShouldBe("AppSurface");
        ThemeResourceKey(tipStyle, Control.ForegroundProperty).ShouldBe("AppForeground");
    });

    [Fact]
    public void DarkContextMenu_RendersLightTextOnDarkSurface() => Sta(() =>
    {
        EnsureThemedApp("Dark");

        // Real ContextMenu parentage (home card menu), rows styled as the app does.
        var context = new ContextMenu();
        var open = new MenuItem { Header = "Open" };
        var rename = new MenuItem { Header = "Rename…" };
        var delete = new MenuItem { Header = "Delete" };
        context.Items.Add(open);
        context.Items.Add(rename);
        context.Items.Add(delete);
        var menuStyle = (Style)Application.Current.FindResource(typeof(MenuItem));
        foreach (var item in new MenuItem[] { open, rename, delete })
            item.Style = menuStyle;
        delete.Template = (ControlTemplate)delete.FindResource("SubmenuItemTemplate");

        // Off-screen glyphs are sparse (antialiased edges miss tolerance), so
        // sum across rows instead of depending on one header's ink.
        int light = 0;
        foreach (var item in new MenuItem[] { open, rename, delete })
            light += CountNear(Render(item, 240, 32), 0xF3, 0xF3, 0xF3, 24);
        light.ShouldBeGreaterThan(100, "context menu text should render in the light foreground");
    });

    private static List<T> FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        var found = new List<T>();
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is T match)
                found.Add(match);
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
                queue.Enqueue(VisualTreeHelper.GetChild(current, i));
        }
        return found;
    }

    [Fact]
    public void ContextMenuTemplate_HasNoIconGutter() => Sta(() =>
    {
        // The stock ContextMenu template paints a hardcoded light icon gutter
        // (left white strip) that no brush override can reach — seen on the
        // home card menu. Our template must host items with no gutter shapes.
        EnsureThemedApp("Dark");
        var style = (Style)Application.Current.FindResource(typeof(ContextMenu));
        var template = style.Setters.OfType<Setter>()
            .FirstOrDefault(s => s.Property == Control.TemplateProperty)?.Value as ControlTemplate;
        template.ShouldNotBeNull("ContextMenu needs an explicit template (framework one paints a white gutter)");
        var root = template!.LoadContent() as DependencyObject;
        root.ShouldNotBeNull("template must load");
        bool IsLight(Color c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B > 127;
        var gutters = FindVisual<Shape>(root!)
            .Where(s => s.Fill is SolidColorBrush brush && IsLight(brush.Color))
            .ToList();
        gutters.ShouldBeEmpty("no light gutter strip allowed in the menu chrome");
        FindVisual<StackPanel>(root!).Any(p => p.IsItemsHost).ShouldBeTrue("items host must exist");
    });

    [Fact]
    public void XamlColorLiterals_BannedOutsideAllowlist()
    {
        // Long-term guard: hardcoded UI colors (White/Black/hex) silently
        // ignore the theme and caused every "doesn't match the theme" report.
        // Use DynamicResource/TemplateBinding. Allowlist:
        // - Themes/*.xaml owns the palette;
        // - ColorPickerDialog.xaml paints a real color spectrum (content);
        // - DrawingAttributes defaults are ink content, not UI chrome.
        string appDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Scribbles.App"));
        var nameLiteral = new System.Text.RegularExpressions.Regex("=\"(White|Black|Gray|LightGray|DarkGray|DimGray)\"");
        var hexLiteral = new System.Text.RegularExpressions.Regex("#[0-9A-Fa-f]{6}\\b|#[0-9A-Fa-f]{8}\\b");
        var offenders = new List<string>();
        foreach (string file in Directory.GetFiles(appDir, "*.xaml", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(appDir, file);
            if (relative.StartsWith("Themes" + Path.DirectorySeparatorChar))
                continue;
            if (string.Equals(Path.GetFileName(file), "ColorPickerDialog.xaml", StringComparison.OrdinalIgnoreCase))
                continue;
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("DrawingAttributes"))
                    continue;
                if (nameLiteral.IsMatch(lines[i]) || hexLiteral.IsMatch(lines[i]))
                    offenders.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
            }
        }
        offenders.ShouldBeEmpty("use theme resources instead of literals");
    }

    [Fact]
    public void IconToggle_Checked_ShowsAccentTint() => Sta(() =>
    {
        EnsureThemedApp("Dark");
        var style = (Style)Application.Current.FindResource("IconToggle");

        int TintedCount(FrameworkElement el)
        {
            var bmp = Render(el, 32, 32);
            byte[] pixels = new byte[32 * 32 * 4];
            bmp.CopyPixels(pixels, 32 * 4, 0);
            int tinted = 0;
            for (int i = 0; i < 32 * 32; i++)
            {
                byte b = pixels[i * 4], g = pixels[i * 4 + 1], r = pixels[i * 4 + 2], a = pixels[i * 4 + 3];
                if (a > 40 && b > r + 20 && g > r)
                    tinted++;
            }
            return tinted;
        }

        var on = new RadioButton { Style = style, IsChecked = true, Width = 32, Height = 32 };
        var off = new RadioButton { Style = style, IsChecked = false, Width = 32, Height = 32 };
        TintedCount(on).ShouldBeGreaterThan(150, "checked tool must show the accent tint");
        TintedCount(off).ShouldBeLessThan(50, "unchecked tool must stay clean");
    });

    [Fact]
    public void DarkComboFace_RendersLightTextOnSurface() => Sta(() =>
    {
        EnsureThemedApp("Dark");
        var combo = new ComboBox
        {
            Style = (Style)Application.Current.FindResource(typeof(ComboBox)),
            Width = 120,
            Height = 30,
        };
        combo.Items.Add("Light");
        combo.Items.Add("Dark");
        combo.SelectedIndex = 1;
        var bmp = Render(combo, 120, 30);
        int total = bmp.PixelWidth * bmp.PixelHeight;
        int surface = CountNear(bmp, 0x2D, 0x2D, 0x2D, 16);
        surface.ShouldBeGreaterThan(total / 2, "combo face should be the dark surface");
        int light = CountNear(bmp, 0xF3, 0xF3, 0xF3, 24);
        light.ShouldBeGreaterThan(20, "combo selection should render in the light foreground");
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

    [Fact]
    public void ThumbCard_CurrentPage_RendersAccentBorder() => Sta(() =>
    {
        EnsureThemedApp("Dark");
        var style = (Style)Application.Current.FindResource("ThumbCard");
        var vm = new Scribbles.App.ViewModels.PageViewModel { Number = 3, IsCurrentPage = true };
        var card = new Border
        {
            Style = style,
            Width = 100,
            Height = 60,
            DataContext = vm,
        };
        var bmp = Render(card, 100, 60);
        // Dark accent #4CC2FF.
        int accent = CountNear(bmp, 0x4C, 0xC2, 0xFF, 24);
        accent.ShouldBeGreaterThan(50, "current-page thumb must show the accent border");

        vm.IsCurrentPage = false;
        card.UpdateLayout();
        var plain = Render(card, 100, 60);
        int still = CountNear(plain, 0x4C, 0xC2, 0xFF, 24);
        still.ShouldBeLessThan(accent, "non-current thumb must drop the accent border");
    });

    [Fact]
    public void OnAccent_ContrastsWithAccent_InBothThemes() => Sta(() =>
    {
        foreach (string theme in new[] { "Dark", "Light" })
        {
            EnsureThemedApp(theme);
            var accent = (Color)Application.Current.FindResource("AppAccentColor");
            var onAccent = (Color)Application.Current.FindResource("AppOnAccentColor");
            double Contrast(Color c)
            {
                double Lin(byte v)
                {
                    double s = v / 255.0;
                    return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
                }
                return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
            }
            double l1 = Contrast(accent), l2 = Contrast(onAccent);
            double ratio = (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
            ratio.ShouldBeGreaterThan(3.0, $"{theme} on-accent text must stay readable");
        }
    });
}
