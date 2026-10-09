using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Scribbles.Core;
using Scribbles.Pdf;
using Shouldly;

namespace Scribbles.Integration.Tests;

/// <summary>M4 exotic fixture set: rotated, mixed sizes, encrypted, forms, 300 pages.</summary>
public sealed class ExoticPdfsTests
{
    private static byte[] SaveDoc(PdfDocument doc)
    {
        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    private static InkStroke Stroke(string id, int page) => new(
        id, page, ToolKind.Pen, 0xFF000000, 2.0,
        new[] { new StrokePoint(50, 100), new StrokePoint(150, 120) });

    [Fact]
    public void RotatedPage_RoundTrips()
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        page.Rotation = PdfSharp.PageRotation.Rotate90DegreesRight;
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawLine(XPens.Black, 10, 10, 200, 10);

        var store = new ScribblesPdfStore();
        var model = new NoteDocument("", new[]
        {
            new NotePage(1, 842, 595, new[] { Stroke("r", 1) }, Array.Empty<TextBoxItem>()),
        });
        var loaded = store.Load(store.Save(SaveDoc(doc), model));
        loaded.HashesMatch.ShouldBeTrue();
        loaded.Pages[0].Rotation.ShouldBe(90);
        loaded.Model!.Pages[0].Strokes.Count.ShouldBe(1);
    }

    [Fact]
    public void MixedSizes_RoundTrip()
    {
        using var doc = new PdfDocument();
        var a4 = doc.AddPage();
        a4.Size = PdfSharp.PageSize.A4;
        var letter = doc.AddPage();
        letter.Size = PdfSharp.PageSize.Letter;
        var custom = doc.AddPage();
        custom.Width = 400;
        custom.Height = 400;

        var store = new ScribblesPdfStore();
        var model = new NoteDocument("", new[]
        {
            new NotePage(1, 595, 842, new[] { Stroke("a", 1) }, Array.Empty<TextBoxItem>()),
            new NotePage(2, 612, 792, new[] { Stroke("b", 2) }, Array.Empty<TextBoxItem>()),
            new NotePage(3, 400, 400, Array.Empty<InkStroke>(), Array.Empty<TextBoxItem>()),
        });
        var loaded = store.Load(store.Save(SaveDoc(doc), model));
        loaded.HashesMatch.ShouldBeTrue();
        loaded.Pages.Count.ShouldBe(3);
        loaded.Model!.Pages[1].Strokes[0].Id.ShouldBe("b");
    }

    [Fact]
    public void EncryptedPdf_FailsFast_WithReadableError()
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        doc.SecuritySettings.UserPassword = "secret";
        byte[] encrypted = SaveDoc(doc);

        var store = new ScribblesPdfStore();
        var ex = Should.Throw<Exception>(() => store.Load(encrypted));
        ex.Message.ShouldNotBeNullOrWhiteSpace();
    }

    private static byte[] BuildFormPdf()
    {
        var objs = new[]
        {
            "<</Type/Catalog/Pages 2 0 R/AcroForm 6 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Annots[5 0 R]>>",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
            "<</Type/Annot/Subtype/Widget/Rect[50 700 200 720]/FT/Tx/T(Name)/V(Ada)/DA(/Helv 12 Tf 0 g)/DR<</Font<</Helv 4 0 R>>>>/P 3 0 R>>",
            "<</Fields[5 0 R]/DR<</Font<</Helv 4 0 R>>>>/DA(/Helv 0 Tf 0 g)>>",
        };
        var sb = new System.Text.StringBuilder();
        sb.Append("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objs.Length; i++)
        {
            offsets.Add(sb.Length);
            sb.Append($"{i + 1} 0 obj\n{objs[i]}\nendobj\n");
        }
        int xref = sb.Length;
        sb.Append($"xref\n0 {objs.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
            sb.Append($"{offset:D10} 00000 n \n");
        sb.Append($"trailer\n<</Size {objs.Length + 1}/Root 1 0 R>>\nstartxref\n{xref}\n%%EOF");
        return System.Text.Encoding.ASCII.GetBytes(sb.ToString());
    }

    [Fact]
    public void FormField_SurvivesSave()
    {
        PdfSharp.Fonts.GlobalFontSettings.FontResolver ??= TestFontResolver.Instance;
        var store = new ScribblesPdfStore();
        var model = new NoteDocument("", new[]
        {
            new NotePage(1, 595, 842, new[] { Stroke("f", 1) }, Array.Empty<TextBoxItem>()),
        });
        byte[] saved = store.Save(BuildFormPdf(), model);
        var loaded = store.Load(saved);
        loaded.HashesMatch.ShouldBeTrue();

        using var stream = new MemoryStream(saved);
        using var reloaded = PdfSharp.Pdf.IO.PdfReader.Open(stream, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        var field = reloaded.AcroForm.Fields["Name"] as PdfSharp.Pdf.AcroForms.PdfTextField;
        field.ShouldNotBeNull();
        field!.Text.ShouldBe("Ada");
    }

    [Fact]
    public void ThreeHundredPages_RoundTrip()
    {
        using var doc = new PdfDocument();
        for (int i = 0; i < 300; i++)
        {
            var page = doc.AddPage();
            page.Size = PdfSharp.PageSize.A5;
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawLine(XPens.Gray, 10, 10, 100, 10);
        }

        var store = new ScribblesPdfStore();
        var pages = Enumerable.Range(1, 300).Select(n => new NotePage(
            n, 420, 595,
            n is 1 or 150 or 300 ? new[] { Stroke($"s{n}", n) } : Array.Empty<InkStroke>(),
            Array.Empty<TextBoxItem>())).ToList();
        var loaded = store.Load(store.Save(SaveDoc(doc), new NoteDocument("", pages)));
        loaded.HashesMatch.ShouldBeTrue();
        loaded.Pages.Count.ShouldBe(300);
        loaded.Model!.Pages[0].Strokes.Count.ShouldBe(1);
        loaded.Model.Pages[149].Strokes[0].Id.ShouldBe("s150");
        loaded.Model.Pages[299].Strokes.Count.ShouldBe(1);
        loaded.Model.Pages[1].Strokes.ShouldBeEmpty();
    }
}
