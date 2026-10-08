using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

/// <summary>
/// TDD RED (2026-10-08): zoom preset stepping, label parsing, pinch math.
/// Must fail before ZoomLevels lands in Core.
/// </summary>
public sealed class ZoomLevelsTests
{
    [Fact]
    public void Next_Up_StepsThroughPresets()
    {
        ZoomLevels.Next(1.0, up: true).ShouldBe(1.25);
        ZoomLevels.Next(1.25, up: true).ShouldBe(1.5);
        ZoomLevels.Next(3.0, up: true).ShouldBeLessThanOrEqualTo(4.0);
    }

    [Fact]
    public void Next_Down_StepsThroughPresets()
    {
        ZoomLevels.Next(1.0, up: false).ShouldBe(0.75);
        ZoomLevels.Next(0.5, up: false).ShouldBeGreaterThanOrEqualTo(0.25);
    }

    [Theory]
    [InlineData("150%", 1.5)]
    [InlineData("75", 0.75)]
    [InlineData("1.5", 1.5)]
    [InlineData("100%", 1.0)]
    public void TryParseLabel_AcceptsForms(string label, double expected)
    {
        ZoomLevels.TryParseLabel(label, out double zoom).ShouldBeTrue();
        zoom.ShouldBe(expected, tolerance: 1e-9);
    }

    [Theory]
    [InlineData("banana")]
    [InlineData("10%")]
    [InlineData("500%")]
    public void TryParseLabel_RejectsGarbageAndRange(string label)
    {
        ZoomLevels.TryParseLabel(label, out _).ShouldBeFalse();
    }

    [Fact]
    public void ApplyPinch_ScalesFromStartDistance()
    {
        ZoomLevels.ApplyPinch(1.0, startDistance: 100, currentDistance: 150).ShouldBe(1.5, tolerance: 1e-9);
        ZoomLevels.ApplyPinch(1.0, startDistance: 100, currentDistance: 50).ShouldBe(0.5, tolerance: 1e-9);
    }

    [Fact]
    public void ApplyPinch_ClampsAndIgnoresDegenerate()
    {
        ZoomLevels.ApplyPinch(1.0, startDistance: 0, currentDistance: 500).ShouldBe(1.0);
        ZoomLevels.ApplyPinch(3.0, startDistance: 100, currentDistance: 500).ShouldBe(ZoomLevels.Max);
        ZoomLevels.ApplyPinch(0.5, startDistance: 500, currentDistance: 10).ShouldBe(ZoomLevels.Min);
    }
}
