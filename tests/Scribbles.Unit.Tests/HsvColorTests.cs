using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

/// <summary>
/// TDD RED: HSV conversions + SV-field mapping for the color-sea picker.
/// Types don't exist yet — must fail before HsvColor lands.
/// </summary>
public sealed class HsvColorTests
{
    [Fact]
    public void FromRgb_Red_IsZeroHueFullSatFullVal()
    {
        var hsv = HsvColor.FromRgb(255, 0, 0);
        hsv.H.ShouldBe(0, tolerance: 0.5);
        hsv.S.ShouldBe(1, tolerance: 0.001);
        hsv.V.ShouldBe(1, tolerance: 0.001);
    }

    [Fact]
    public void FromRgb_White_HasNoSaturation()
    {
        var hsv = HsvColor.FromRgb(255, 255, 255);
        hsv.S.ShouldBe(0, tolerance: 0.001);
        hsv.V.ShouldBe(1, tolerance: 0.001);
    }

    [Fact]
    public void FromRgb_Black_HasNoValue()
    {
        HsvColor.FromRgb(0, 0, 0).V.ShouldBe(0, tolerance: 0.001);
    }

    [Theory]
    [InlineData(0, 0, 255)]   // blue
    [InlineData(0, 255, 0)]   // green
    [InlineData(255, 255, 0)] // yellow
    [InlineData(128, 64, 200)]
    public void RoundTrip_PreservesColor(byte r, byte g, byte b)
    {
        var (r2, g2, b2) = HsvColor.FromRgb(r, g, b).ToRgb();
        Math.Abs(r2 - r).ShouldBeLessThanOrEqualTo(2);
        Math.Abs(g2 - g).ShouldBeLessThanOrEqualTo(2);
        Math.Abs(b2 - b).ShouldBeLessThanOrEqualTo(2);
    }

    [Fact]
    public void FieldPosition_MapsSandV_ToUnitSquare()
    {
        var hsv = new HsvColor(120, 0.25, 0.75);
        var (x, y) = hsv.FieldPosition();
        x.ShouldBe(0.25, tolerance: 1e-9);
        y.ShouldBe(0.25, tolerance: 1e-9); // y = 1 - V
    }

    [Fact]
    public void FromField_InvertsFieldPosition()
    {
        var hsv = HsvColor.FromField(hue: 200, x: 0.4, y: 0.7);
        hsv.H.ShouldBe(200, tolerance: 1e-9);
        hsv.S.ShouldBe(0.4, tolerance: 1e-9);
        hsv.V.ShouldBe(0.3, tolerance: 1e-9);
    }

    [Fact]
    public void FromField_ClampsOutOfRange()
    {
        var hsv = HsvColor.FromField(hue: -30, x: 2.0, y: -1.0);
        hsv.H.ShouldBe(330, tolerance: 1e-9);
        hsv.S.ShouldBe(1, tolerance: 1e-9);
        hsv.V.ShouldBe(1, tolerance: 1e-9);
    }

    [Fact]
    public void Presets_AreValidDistinctColors()
    {
        var presets = ColorPalette.Presets;
        presets.Length.ShouldBe(40);
        presets.Distinct().Count().ShouldBe(40);
        foreach (string hex in presets)
            ColorPalette.TryNormalize(hex, out _).ShouldBeTrue();
    }
}
