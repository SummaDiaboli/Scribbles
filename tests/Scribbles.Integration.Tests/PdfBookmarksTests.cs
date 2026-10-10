using Scribbles.Core;
using Scribbles.Pdf;
using Shouldly;

namespace Scribbles.Integration.Tests;

/// <summary>Outline extraction against a generated bookmarked PDF.</summary>
public sealed class PdfBookmarksTests
{
    private static byte[] BookmarkedPdf()
    {
        using var doc = new PdfSharp.Pdf.PdfDocument();
        var p1 = doc.AddPage(); p1.Size = PdfSharp.PageSize.A4;
        var p2 = doc.AddPage(); p2.Size = PdfSharp.PageSize.A4;
        var p3 = doc.AddPage(); p3.Size = PdfSharp.PageSize.A4;
        var chapter = doc.Outlines.Add("Chapter 1", p1);
        chapter.Outlines.Add("Section 1.1", p2);
        doc.Outlines.Add("Chapter 2", p3);
        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Read_MapsTitlesPagesAndNesting()
    {
        var content = PdfPageContentReader.Read(BookmarkedPdf());
        content.Bookmarks.Count.ShouldBe(2);
        content.Bookmarks[0].Title.ShouldBe("Chapter 1");
        content.Bookmarks[0].PageNumber.ShouldBe(1);
        content.Bookmarks[0].Children.Count.ShouldBe(1);
        content.Bookmarks[0].Children[0].Title.ShouldBe("Section 1.1");
        content.Bookmarks[0].Children[0].PageNumber.ShouldBe(2);
        content.Bookmarks[1].Title.ShouldBe("Chapter 2");
        content.Bookmarks[1].PageNumber.ShouldBe(3);
    }

    [Fact]
    public void Read_PlainPdf_HasNoBookmarks()
    {
        using var doc = new PdfSharp.Pdf.PdfDocument();
        doc.AddPage();
        using var ms = new MemoryStream();
        doc.Save(ms);
        PdfPageContentReader.Read(ms.ToArray()).Bookmarks.ShouldBeEmpty();
    }
}
