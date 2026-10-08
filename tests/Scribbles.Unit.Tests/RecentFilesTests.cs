using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

/// <summary>
/// TDD RED (2026-10-08): recent-files list rules.
/// Type doesn't exist yet — must fail before RecentFiles lands.
/// </summary>
public sealed class RecentFilesTests
{
    [Fact]
    public void Add_PutsFirst_Dedupes()
    {
        var files = RecentFiles.Add(new[] { "b.pdf" }, "a.pdf");
        files[0].ShouldEndWith("a.pdf");
        files = RecentFiles.Add(files, "b.pdf");
        files[0].ShouldEndWith("b.pdf");
        files.Length.ShouldBe(2);
    }

    [Fact]
    public void Add_CapsAtEight()
    {
        string[] files = Array.Empty<string>();
        for (int i = 0; i < 12; i++)
            files = RecentFiles.Add(files, $"f{i}.pdf");
        files.Length.ShouldBe(8);
        files[0].ShouldEndWith("f11.pdf");
    }

    [Fact]
    public void Add_IgnoresBlank()
    {
        RecentFiles.Add(Array.Empty<string>(), "  ").ShouldBeEmpty();
    }

    [Fact]
    public void PruneMissing_DropsGoneFiles()
    {
        string real = Path.GetTempFileName();
        try
        {
            var files = RecentFiles.PruneMissing(new[] { real, Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf") });
            files.ShouldBe(new[] { real });
        }
        finally
        {
            File.Delete(real);
        }
    }
}
