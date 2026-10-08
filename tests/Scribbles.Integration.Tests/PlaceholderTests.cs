using Scribbles.Core;
using Shouldly;

namespace Scribbles.Integration.Tests;

public sealed class PlaceholderTests
{
    [Fact]
    public void FixturesFolder_Exists()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures");
        Directory.Exists(Path.GetFullPath(dir)).ShouldBeTrue();
    }

    [Fact]
    public void DocumentModel_PageCount()
    {
        var doc = new OpenDocument("sample.pdf", new[]
        {
            new DocumentPage(1, 612, 792),
            new DocumentPage(2, 612, 792),
        });
        doc.PageCount.ShouldBe(2);
    }
}
