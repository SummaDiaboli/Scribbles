using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

public sealed class NotebookFilesTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void List_ReturnsPdfsNewestFirst()
    {
        string dir = TempDir();
        string a = Path.Combine(dir, "a.pdf");
        string b = Path.Combine(dir, "b.pdf");
        File.WriteAllBytes(a, new byte[] { 1 });
        Thread.Sleep(50);
        File.WriteAllBytes(b, new byte[] { 1, 2 });

        var list = NotebookFiles.List(dir);
        list.Select(d => d.Name).ShouldBe(new[] { "b.pdf", "a.pdf" });
        list.All(d => d.Kind == HomeDocumentKind.Notebook).ShouldBeTrue();
    }

    [Fact]
    public void List_MissingDir_IsEmpty()
    {
        NotebookFiles.List(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()))
            .ShouldBeEmpty();
    }

    [Fact]
    public void DefaultDirectory_LivesUnderLocalAppData()
    {
        NotebookFiles.DefaultDirectory.ShouldStartWith(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }
}
