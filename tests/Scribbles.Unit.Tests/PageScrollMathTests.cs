using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

public sealed class PageScrollMathTests
{
    [Fact]
    public void PageFromOffset_Top_IsOne()
    {
        PageScrollMath.PageFromOffset(0, 1000, 5).ShouldBe(1);
    }

    [Fact]
    public void PageFromOffset_MidPage_RoundsDown()
    {
        PageScrollMath.PageFromOffset(1999, 1000, 5).ShouldBe(2);
        PageScrollMath.PageFromOffset(2000, 1000, 5).ShouldBe(3);
    }

    [Fact]
    public void PageFromOffset_Clamps()
    {
        PageScrollMath.PageFromOffset(-50, 1000, 5).ShouldBe(1);
        PageScrollMath.PageFromOffset(99999, 1000, 5).ShouldBe(5);
    }

    [Fact]
    public void PageFromOffset_EmptyDoc_IsOne()
    {
        PageScrollMath.PageFromOffset(500, 1000, 0).ShouldBe(1);
    }

    [Fact]
    public void OffsetForPage_ScalesAndClamps()
    {
        PageScrollMath.OffsetForPage(3, 1000, 5).ShouldBe(2000);
        PageScrollMath.OffsetForPage(99, 1000, 5).ShouldBe(4000);
        PageScrollMath.OffsetForPage(0, 1000, 5).ShouldBe(0);
    }
}
