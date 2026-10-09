using System.IO;
using Scribbles.Core;
using Scribbles.Pdf;

namespace Scribbles.App.Services;

/// <summary>Notebook files: create/list under %LocalAppData%/Scribbles/notebooks.</summary>
public sealed class NotebookService
{
    private readonly string _directory;

    public NotebookService(string? directory = null)
    {
        _directory = directory ?? NotebookFiles.DefaultDirectory;
    }

    public string CreateNotebook()
    {
        Directory.CreateDirectory(_directory);
        string name = NotebookNaming.NextName(
            Directory.GetFiles(_directory, "*.pdf")
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()) + ".pdf";
        string path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, NotebookDocuments.CreateBlank(new[]
        {
            (StandardPageSizes.A4WidthPt, StandardPageSizes.A4HeightPt),
        }));
        return path;
    }
}
