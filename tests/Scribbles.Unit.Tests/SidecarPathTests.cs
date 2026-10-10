using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

public sealed class SidecarPathTests
{
    [Fact]
    public void ForPdf_SwapsExtension()
    {
        SidecarPath.ForPdf(@"C:\docs\notes.pdf").ShouldBe(@"C:\docs\notes.scribbles.json");
    }

    [Fact]
    public void IsSidecar_Detects()
    {
        SidecarPath.IsSidecar(@"C:\docs\notes.scribbles.json").ShouldBeTrue();
        SidecarPath.IsSidecar(@"C:\docs\notes.pdf").ShouldBeFalse();
        SidecarPath.IsSidecar(@"C:\docs\NOTES.SCRIBBLES.JSON").ShouldBeTrue();
    }
}
