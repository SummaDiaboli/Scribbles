using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

public sealed class AutosaveRetentionTests
{
    private static string NewDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "scribbles-retention-" + Guid.NewGuid().ToString("N"));
        return Directory.CreateDirectory(dir).FullName;
    }

    private static void Backdate(string infoPath, DateTime utc) =>
        File.SetLastWriteTimeUtc(infoPath, utc);

    [Fact]
    public void Save_ThenDelete_RemovesPair()
    {
        var svc = new AutosaveService(NewDir());
        svc.Save("abc123", new byte[] { 1 }, "source.pdf");
        File.Exists(svc.PdfPathFor("abc123")).ShouldBeTrue();
        svc.Delete("abc123");
        File.Exists(svc.PdfPathFor("abc123")).ShouldBeFalse();
        File.Exists(svc.PdfPathFor("abc123") + ".info").ShouldBeFalse();
    }

    [Fact]
    public void Save_PrunesBeyondMaxCopies_KeepingNewest()
    {
        var svc = new AutosaveService(NewDir());
        for (int i = 0; i < AutosaveService.MaxCopies + 3; i++)
        {
            svc.Save($"key{i:00}", new byte[] { (byte)i }, $"doc{i}.pdf");
            Backdate(svc.PdfPathFor($"key{i:00}") + ".info",
                DateTime.UtcNow.AddMinutes(-(AutosaveService.MaxCopies + 3 - i)));
        }
        svc.Save("newest", new byte[] { 9 }, "newest.pdf");
        var snapshots = svc.ListSnapshots();
        snapshots.Count.ShouldBeLessThanOrEqualTo(AutosaveService.MaxCopies);
        snapshots[0].SourcePath.ShouldContain("newest.pdf");
        snapshots.Any(s => (s.SourcePath ?? "").Contains("doc0.pdf")).ShouldBeFalse("oldest must be pruned");
    }

    [Fact]
    public void Save_RemovesPairsOlderThanMaxAge()
    {
        var svc = new AutosaveService(NewDir());
        svc.Save("old", new byte[] { 1 }, "old.pdf");
        Backdate(svc.PdfPathFor("old") + ".info",
            DateTime.UtcNow - AutosaveService.MaxAge - TimeSpan.FromDays(1));
        svc.Save("fresh", new byte[] { 2 }, "fresh.pdf");
        File.Exists(svc.PdfPathFor("old")).ShouldBeFalse("stale snapshot must be pruned");
        File.Exists(svc.PdfPathFor("fresh")).ShouldBeTrue();
    }

    [Fact]
    public void ListSnapshots_ReturnsNewestFirst_WithSources()
    {
        var svc = new AutosaveService(NewDir());
        svc.Save("a", new byte[] { 1 }, @"C:\docs\alpha.pdf");
        svc.Save("b", new byte[] { 2 }, @"C:\docs\beta.pdf");
        var snapshots = svc.ListSnapshots();
        snapshots.Count.ShouldBe(2);
        snapshots[0].SourcePath.ShouldContain("beta.pdf");
        snapshots[1].SourcePath.ShouldContain("alpha.pdf");
        snapshots[0].SavedUtc.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(-5));
    }
}
