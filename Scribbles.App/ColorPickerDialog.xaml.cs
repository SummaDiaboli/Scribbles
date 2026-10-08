using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Scribbles.Core;

namespace Scribbles.App;

/// <summary>Preset grid + draggable color-sea dot + live preview.
/// Returns the picked color and the (possibly edited) palette on OK.</summary>
public partial class ColorPickerDialog : Window
{
    public Color Selected { get; private set; }
    public string[] Palette { get; private set; }

    private double _hue;
    private bool _syncing;

    public ColorPickerDialog(Color current, string[] palette)
    {
        InitializeComponent();
        Selected = current;
        Palette = palette;
        var hsv = HsvColor.FromRgb(current.R, current.G, current.B);
        _hue = hsv.H;
        BuildPresets();
        RebuildPalette();
        SetFromHsv(hsv);
        HueSlider.Value = _hue;
    }

    // ---------- color sea ----------

    private void SvField_Press(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;
        var pos = e.GetPosition(SvField);
        double x = Math.Clamp(pos.X / SvField.ActualWidth, 0, 1);
        double y = Math.Clamp(pos.Y / SvField.ActualHeight, 0, 1);
        SetFromHsv(HsvColor.FromField(_hue, x, y));
        SvField.CaptureMouse();
    }

    private void SvField_Release(object sender, MouseButtonEventArgs e)
    {
        // Never hold capture past the drag — a stuck capture swallows every
        // other click in the dialog (OK/Cancel/close included).
        if (SvField.IsMouseCaptured)
            SvField.ReleaseMouseCapture();
    }

    private void HueSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // May fire during InitializeComponent before fields exist.
        if (HueSlider is null || HueBase is null)
            return;
        _hue = HueSlider.Value;
        var (_, s, v) = CurrentHsv();
        SetFromHsv(new HsvColor(_hue, s, v));
    }

    private (double H, double S, double V) CurrentHsv()
    {
        var hsv = HsvColor.FromRgb(Selected.R, Selected.G, Selected.B);
        return (hsv.H, hsv.S, hsv.V);
    }

    private void SetFromHsv(HsvColor hsv)
    {
        var (r, g, b) = hsv.ToRgb();
        Selected = Color.FromRgb(r, g, b);
        var (bx, by, bz) = new HsvColor(hsv.H, 1, 1).ToRgb();
        HueBase.Color = Color.FromRgb(bx, by, bz);
        var (fx, fy) = hsv.FieldPosition();
        // Keep the whole dot inside the field (half the dot may overhang otherwise).
        double half = SvDot.Width / 2;
        double w = SvField.ActualWidth > 0 ? SvField.ActualWidth : SvField.Width;
        double h = SvField.ActualHeight > 0 ? SvField.ActualHeight : SvField.Height;
        Canvas.SetLeft(SvDot, Math.Clamp(fx * w, half, w - half) - half);
        Canvas.SetTop(SvDot, Math.Clamp(fy * h, half, h - half) - half);
        Preview.Background = new SolidColorBrush(Selected);
        _syncing = true;
        HexBox.Text = $"#{r:X2}{g:X2}{b:X2}";
        _syncing = false;
        if (Math.Abs(HueSlider.Value - hsv.H) > 0.5)
            HueSlider.Value = hsv.H;
        _hue = hsv.H;
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing)
            return;
        if (ColorPalette.TryNormalize(HexBox.Text, out string? hex) && hex is not null)
        {
            uint v = Convert.ToUInt32(hex.TrimStart('#'), 16);
            var color = Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
            SetFromHsv(HsvColor.FromRgb(color.R, color.G, color.B));
        }
    }

    // ---------- presets + palette ----------

    private static (byte A, byte R, byte G, byte B) Parse(string normalizedHex)
    {
        uint v = Convert.ToUInt32(normalizedHex.TrimStart('#'), 16);
        return ((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    private void BuildPresets()
    {
        PresetGrid.Children.Clear();
        foreach (string hex in ColorPalette.Presets)
        {
            var (a, r, g, b) = Parse(hex);
            var swatch = new Button
            {
                Style = (Style)FindResource("Swatch"),
                Background = new SolidColorBrush(Color.FromArgb(a, r, g, b)),
                ToolTip = hex,
            };
            swatch.Click += (_, _) => SetFromHsv(HsvColor.FromRgb(r, g, b));
            PresetGrid.Children.Add(swatch);
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        Palette = ColorPalette.Add(Palette, $"#{Selected.R:X2}{Selected.G:X2}{Selected.B:X2}");
        RebuildPalette();
    }

    private void RebuildPalette()
    {
        PalettePanel.Children.Clear();
        foreach (string hex in Palette)
        {
            var (a, r, g, b) = Parse(hex);
            var pick = new Button
            {
                Style = (Style)FindResource("Swatch"),
                Background = new SolidColorBrush(Color.FromArgb(a, r, g, b)),
                ToolTip = hex,
            };
            pick.Click += (_, _) => SetFromHsv(HsvColor.FromRgb(r, g, b));
            PalettePanel.Children.Add(pick);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
