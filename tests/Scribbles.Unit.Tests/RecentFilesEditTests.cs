using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

public sealed class RecentFilesEditTests
{
    [Fact]
    public void Remove_DropsMatchingPathCaseInsensitively()
    {
        string[] files = new[] { @"C:\a\one.pdf", @"C:\a\two.pdf" };
        RecentFiles.Remove(files, @"c:\A\TWO.pdf").ShouldBe(new[] { @"C:\a\one.pdf" });
    }

    [Fact]
    public void Remove_MissingPath_ReturnsUnchanged()
    {
        string[] files = new[] { @"C:\a\one.pdf" };
        RecentFiles.Remove(files, @"C:\a\gone.pdf").ShouldBe(files);
    }

    [Fact]
    public void Rename_ReplacesInPlace_PreservingOrder()
    {
        string[] files = new[] { @"C:\a\one.pdf", @"C:\a\two.pdf", @"C:\a\three.pdf" };
        RecentFiles.Rename(files, @"C:\a\two.pdf", @"C:\a\two-v2.pdf")
            .ShouldBe(new[] { @"C:\a\one.pdf", @"C:\a\two-v2.pdf", @"C:\a\three.pdf" });
    }

    [Fact]
    public void Rename_MissingOld_AddsNew()
    {
        string[] files = new[] { @"C:\a\one.pdf" };
        RecentFiles.Rename(files, @"C:\a\gone.pdf", @"C:\a\new.pdf").ShouldBe(
            new[] { @"C:\a\new.pdf", @"C:\a\one.pdf" });
    }

    [Fact]
    public void Rename_BlankNew_RemovesOld()
    {
        string[] files = new[] { @"C:\a\one.pdf", @"C:\a\two.pdf" };
        RecentFiles.Rename(files, @"C:\a\one.pdf", "  ").ShouldBe(new[] { @"C:\a\two.pdf" });
    }
}
