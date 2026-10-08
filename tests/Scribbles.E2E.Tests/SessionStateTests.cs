using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scribbles.App;
using Scribbles.App.Services;
using Scribbles.App.ViewModels;
using Shouldly;
using static Scribbles.E2E.Tests.WpfTestHost;

namespace Scribbles.E2E.Tests;

/// <summary>
/// Proves the exact complaint: edits flip undo/redo availability and raise
/// StateChanged (which the toolbar buttons and menu items bind to).
/// Real InkCanvases, never shown.
/// </summary>
public sealed class SessionStateTests
{
    private static (DocumentSession Session, InkCanvas Page) CreateSession()
    {
        var converter = new InkModelConverter();
        var canvas = new InkCanvas
        {
            DataContext = new PageViewModel { Number = 1 },
            DefaultDrawingAttributes = new DrawingAttributes
            {
                Color = Colors.Black,
                Width = 3.5,
                Height = 3.5,
            },
        };
        var inks = new List<InkCanvas> { canvas };
        var session = new DocumentSession(
            new Scribbles.Pdf.ScribblesPdfStore(),
            converter,
            new StrokeHistory(converter),
            new PenToolbarController(),
            () => inks);
        session.HookCanvases();
        return (session, canvas);
    }

    private static Stroke MakeStroke() => new(
        new StylusPointCollection(new[]
        {
            new StylusPoint(10, 10),
            new StylusPoint(50, 50),
        }));

    [Fact]
    public void AddingStroke_RaisesStateChanged_AndEnablesUndo() => Sta(() =>
    {
        var (session, canvas) = CreateSession();
        int events = 0;
        session.StateChanged += (_, _) => events++;

        session.CanUndoAny().ShouldBeFalse();
        session.CanRedoAny().ShouldBeFalse();

        canvas.Strokes.Add(MakeStroke());

        events.ShouldBeGreaterThanOrEqualTo(1, "StateChanged must fire so buttons refresh");
        session.CanUndoAny().ShouldBeTrue();
        session.CanRedoAny().ShouldBeFalse();
    });

    [Fact]
    public void Undo_AfterStroke_EnablesRedo() => Sta(() =>
    {
        var (session, canvas) = CreateSession();
        canvas.Strokes.Add(MakeStroke());

        session.Undo(new[] { 1 });

        canvas.Strokes.ShouldBeEmpty("undo should remove the stroke");
        session.CanUndoAny().ShouldBeFalse();
        session.CanRedoAny().ShouldBeTrue("redo must become available after undo");

        session.Redo(new[] { 1 });
        canvas.Strokes.Count.ShouldBe(1, "redo should restore the stroke");
        session.CanUndoAny().ShouldBeTrue();
    });

    [Fact]
    public void DisabledIconButton_RendersDimmed() => Sta(() =>
    {
        EnsureThemedApp("Dark");
        var style = (Style)Application.Current.FindResource("IconButton");

        var enabled = new Button { Style = style, IsEnabled = true, Content = "X", Width = 32, Height = 32 };
        var disabled = new Button { Style = style, IsEnabled = false, Content = "X", Width = 32, Height = 32 };

        double MeanLuminance(FrameworkElement el)
        {
            el.Measure(new System.Windows.Size(32, 32));
            el.Arrange(new Rect(0, 0, 32, 32));
            el.UpdateLayout();
            var bmp = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(el);
            byte[] pixels = new byte[32 * 32 * 4];
            bmp.CopyPixels(pixels, 32 * 4, 0);
            double sum = 0;
            int n = 0;
            for (int i = 0; i < 32 * 32; i++)
            {
                if (pixels[i * 4 + 3] < 16)
                    continue; // transparent
                sum += (pixels[i * 4 + 2] + pixels[i * 4 + 1] + pixels[i * 4]) / 3.0;
                n++;
            }
            return n == 0 ? 0 : sum / n;
        }

        double on = MeanLuminance(enabled);
        double off = MeanLuminance(disabled);
        off.ShouldBeLessThan(on * 0.7, "disabled toolbar button must look dimmed");
    });
}
