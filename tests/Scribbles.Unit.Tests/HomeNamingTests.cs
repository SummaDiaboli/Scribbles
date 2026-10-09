using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

public sealed class HomeNamingTests
{
    [Fact]
    public void NextName_First_IsNotebook1()
    {
        NotebookNaming.NextName(new[] { "Other.pdf" }).ShouldBe("Notebook 1");
    }

    [Fact]
    public void NextName_SkipsTaken()
    {
        NotebookNaming.NextName(new[] { "Notebook 1.pdf", "Notebook 2.pdf" }).ShouldBe("Notebook 3");
    }

    [Fact]
    public void NextName_FillsGaps_FromMax()
    {
        NotebookNaming.NextName(new[] { "Notebook 1.pdf", "Notebook 5.pdf" }).ShouldBe("Notebook 6");
    }

    [Fact]
    public void ChooseBackend_PrefersPowerPoint()
    {
        SlideImport.ChooseBackend(officeAvailable: true, libreOfficeAvailable: true)
            .ShouldBe(SlideBackend.PowerPoint);
    }

    [Fact]
    public void ChooseBackend_FallsBackToLibreOffice()
    {
        SlideImport.ChooseBackend(officeAvailable: false, libreOfficeAvailable: true)
            .ShouldBe(SlideBackend.LibreOffice);
    }

    [Fact]
    public void ChooseBackend_Manual_WhenNeither()
    {
        SlideImport.ChooseBackend(officeAvailable: false, libreOfficeAvailable: false)
            .ShouldBe(SlideBackend.ManualPdf);
    }
}
