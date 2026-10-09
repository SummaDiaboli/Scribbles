using System;
using Scribbles.Core;
using Shouldly;
using static Scribbles.E2E.Tests.WpfTestHost;

namespace Scribbles.E2E.Tests;

/// <summary>Home window constructs; slide import degrades gracefully.</summary>
public sealed class HomeWindowTests
{
    [Fact]
    public void MergeEntries_NotebooksFirst_DedupesByPath()
    {
        var notebooks = new[]
        {
            (new HomeDocument("a.pdf", "C:\\Docs\\a.pdf", HomeDocumentKind.Notebook, DateTime.UtcNow), "n"),
        };
        var recents = new[]
        {
            (new HomeDocument("a.pdf", "c:\\docs\\A.PDF", HomeDocumentKind.Pdf, DateTime.UtcNow), "r"),
            (new HomeDocument("b.pdf", "C:\\Docs\\b.pdf", HomeDocumentKind.Pdf, DateTime.UtcNow), "r"),
        };
        var merged = Scribbles.App.HomeWindow.MergeEntries(notebooks, recents);
        merged.Select(e => e.Doc.Path).ShouldBe(new[] { "C:\\Docs\\a.pdf", "C:\\Docs\\b.pdf" });
    }
    [Fact]
    public void HomeWindow_Constructs_WithVersionTitle() => Sta(() =>
    {
        EnsureThemedApp("Dark");
        var home = new Scribbles.App.HomeWindow();
        try
        {
            home.Title.ShouldStartWith("Scribbles ");
        }
        finally
        {
            home.Close();
        }
    });

    [Fact]
    public void SlideImport_MissingFile_ReturnsNull()
    {
        // Deterministic everywhere: no source file means every backend fails.
        Scribbles.App.Services.SlideImporter.TryImport(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid() + ".pptx"),
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString()))
            .ShouldBeNull();
    }
}
