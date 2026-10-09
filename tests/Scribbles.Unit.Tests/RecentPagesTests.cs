using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

/// <summary>TDD RED: per-document last-page memory. Must fail first.</summary>
public sealed class RecentPagesTests
{
    [Fact]
    public void ForPath_Unknown_ReturnsOne()
    {
        RecentPages.ForPath(new List<LastPage>(), "x.pdf").ShouldBe(1);
    }

    [Fact]
    public void Record_ThenForPath_RoundTrips()
    {
        var entries = RecentPages.Record(new List<LastPage>(), "C:\\Docs\\a.pdf", 7);
        RecentPages.ForPath(entries, "c:\\docs\\A.PDF").ShouldBe(7);
    }

    [Fact]
    public void Record_SamePath_UpdatesInPlace()
    {
        var entries = RecentPages.Record(new List<LastPage>(), "a.pdf", 3);
        entries = RecentPages.Record(entries, "a.pdf", 9);
        entries.Count.ShouldBe(1);
        RecentPages.ForPath(entries, "a.pdf").ShouldBe(9);
    }

    [Fact]
    public void Record_ClampsBelowOne()
    {
        var entries = RecentPages.Record(new List<LastPage>(), "a.pdf", -5);
        RecentPages.ForPath(entries, "a.pdf").ShouldBe(1);
    }

    [Fact]
    public void Record_CapsEntries()
    {
        var entries = new List<LastPage>();
        for (int i = 0; i < 70; i++)
            entries = RecentPages.Record(entries, $"f{i}.pdf", 1).ToList();
        entries.Count.ShouldBeLessThanOrEqualTo(50);
        RecentPages.ForPath(entries, "f69.pdf").ShouldBe(1);
    }
}
