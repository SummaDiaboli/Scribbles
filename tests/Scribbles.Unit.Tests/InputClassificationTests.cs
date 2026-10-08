using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

public sealed class InputClassificationTests
{
    [Theory]
    [InlineData("Stylus", InputKind.Pen)]
    [InlineData("Touch", InputKind.Touch)]
    [InlineData("Mouse", InputKind.Mouse)]
    [InlineData(null, InputKind.Unknown)]
    [InlineData("Bogus", InputKind.Unknown)]
    public void Classify_TabletDeviceType(string? deviceType, InputKind expected)
    {
        InputClassification.Classify(deviceType).ShouldBe(expected);
    }

    [Fact]
    public void Classify_LargeTouchContact_IsPalm()
    {
        InputClassification.Classify("Touch", contactWidthMm: 25).ShouldBe(InputKind.Palm);
    }

    [Fact]
    public void OnlyPenDraws_TouchAndPalmScroll()
    {
        InputClassification.ShouldDraw(InputKind.Pen).ShouldBeTrue();
        InputClassification.ShouldDraw(InputKind.Touch).ShouldBeFalse();
        InputClassification.ShouldScroll(InputKind.Touch).ShouldBeTrue();
        InputClassification.ShouldScroll(InputKind.Palm).ShouldBeTrue();
        InputClassification.ShouldScroll(InputKind.Pen).ShouldBeFalse();
    }
}
