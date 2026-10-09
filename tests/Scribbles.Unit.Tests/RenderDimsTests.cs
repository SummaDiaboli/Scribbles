using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

/// <summary>TDD RED: exact-scale raster targets so mapping needs no render.</summary>
public sealed class RenderDimsTests
{
    [Fact]
    public void TargetPixels_DoubleNominalDpi()
    {
        var dims = RenderDims.TargetPixels(612, 792, renderScale: 2.0);
        dims.Width.ShouldBe((int)(612 / 72.0 * 192));
        dims.Height.ShouldBe((int)(792 / 72.0 * 192));
    }

    [Fact]
    public void NominalBase_Is96Dpi()
    {
        var dims = RenderDims.NominalBaseDips(612, 792);
        dims.Width.ShouldBe(612.0 * 96 / 72, tolerance: 1e-9);
        dims.Height.ShouldBe(792.0 * 96 / 72, tolerance: 1e-9);
    }

    [Fact]
    public void BaseMatchesTarget_AtRenderScale()
    {
        // The whole point: base size known before rendering must equal
        // rendered pixels / 2, so ink mapping never drifts.
        var target = RenderDims.TargetPixels(595.276, 841.89, renderScale: 2.0);
        var nominal = RenderDims.NominalBaseDips(595.276, 841.89);
        (target.Width / 2.0).ShouldBe(nominal.Width, tolerance: 1.0);
        (target.Height / 2.0).ShouldBe(nominal.Height, tolerance: 1.0);
    }
}
