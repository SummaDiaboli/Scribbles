using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

public sealed class HomeListingTests
{
    private static readonly HomeDocument Notes =
        new("Zebra notes", @"C:\n\notes.pdf", HomeDocumentKind.Notebook, new DateTime(2026, 10, 1));
    private static readonly HomeDocument Report =
        new("Annual report", @"C:\d\report.pdf", HomeDocumentKind.Pdf, new DateTime(2026, 10, 9));
    private static readonly HomeDocument Old =
        new("Old notes", @"C:\n\old.pdf", HomeDocumentKind.Notebook, new DateTime(2026, 9, 1));

    private static readonly HomeDocument[] Docs = new[] { Notes, Report, Old };

    [Fact]
    public void DefaultSort_IsNewestFirst()
    {
        HomeListing.Apply(Docs, HomeSort.Newest, HomeKindFilter.All, null)
            .ShouldBe(new[] { Report, Notes, Old });
    }

    [Fact]
    public void NameSort_IsAlphabetical()
    {
        HomeListing.Apply(Docs, HomeSort.Name, HomeKindFilter.All, null)
            .ShouldBe(new[] { Report, Old, Notes });
    }

    [Fact]
    public void KindFilter_Selects()
    {
        HomeListing.Apply(Docs, HomeSort.Newest, HomeKindFilter.Notebooks, null)
            .ShouldBe(new[] { Notes, Old });
        HomeListing.Apply(Docs, HomeSort.Newest, HomeKindFilter.Pdfs, null)
            .ShouldBe(new[] { Report });
    }

    [Fact]
    public void Query_MatchesNameSubstring_CaseInsensitive()
    {
        HomeListing.Apply(Docs, HomeSort.Newest, HomeKindFilter.All, "notes")
            .ShouldBe(new[] { Notes, Old });
        HomeListing.Apply(Docs, HomeSort.Newest, HomeKindFilter.All, "  REPORT  ")
            .ShouldBe(new[] { Report });
        HomeListing.Apply(Docs, HomeSort.Newest, HomeKindFilter.All, "zzz")
            .ShouldBeEmpty();
    }
}
