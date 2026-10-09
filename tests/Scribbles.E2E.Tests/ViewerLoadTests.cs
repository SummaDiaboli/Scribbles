using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Shouldly;
using static Scribbles.E2E.Tests.WpfTestHost;

namespace Scribbles.E2E.Tests;

/// <summary>
/// Regression for the 2026-10-09 startup-white-screen: opening any document
/// crashed in Loaded because the pen dock was never created (NullReference
/// in SyncPenControls). Drives construct + load path with no windows shown.
/// </summary>
public sealed class ViewerLoadTests
{
    private static List<T> FindLogical<T>(DependencyObject root) where T : DependencyObject
    {
        var found = new List<T>();
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is T match)
                found.Add(match);
            foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
                queue.Enqueue(child);
        }
        return found;
    }

    [Fact]
    public void MainWindow_LoadPath_DoesNotThrow() => Sta(() =>
    {
        EnsureThemedApp("Dark");
        var window = new Scribbles.App.MainWindow();
        try
        {
            window.OnLoadedInternal();
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Sidebar_ColumnOwnsWidth_SplitterResizes_ThumbsFluid() => Sta(() =>
    {
        EnsureThemedApp("Dark");
        var window = new Scribbles.App.MainWindow();
        try
        {
            var grid = FindLogical<Grid>(window).First(g => g.Name == "CenterGrid");
            var column = grid.ColumnDefinitions[0];
            column.Width.Value.ShouldBe(168, "sidebar starts at 168px");
            column.MinWidth.ShouldBe(36);
            column.MaxWidth.ShouldBe(340);

            var splitter = FindLogical<GridSplitter>(window).FirstOrDefault();
            splitter.ShouldNotBeNull("sidebar drag splitter should exist");
            splitter!.ResizeBehavior.ShouldBe(GridResizeBehavior.PreviousAndNext);

            // The panel itself must stretch (an explicit Width froze it before,
            // so drags shoved the block instead of resizing content).
            var sidebar = FindLogical<Border>(window).First(b => b.Name == "Sidebar");
            sidebar.ReadLocalValue(FrameworkElement.WidthProperty)
                .ShouldBe(DependencyProperty.UnsetValue, "column owns the width, not the panel");

            var toggle = FindLogical<Button>(window).First(b => b.Name == "SidebarToggle");
            var thumbs = FindLogical<ScrollViewer>(window).First(s => s.Name == "ThumbScroll");
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            column.Width.Value.ShouldBe(36);
            thumbs.Visibility.ShouldBe(Visibility.Collapsed);
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            column.Width.Value.ShouldBe(168);
            thumbs.Visibility.ShouldBe(Visibility.Visible);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Toolbar_MovesBetweenTopAndBottomTrays() => Sta(() =>
    {
        EnsureThemedApp("Dark");
        var window = new Scribbles.App.MainWindow();
        try
        {
            window.Measure(new Size(1400, 900));
            window.Arrange(new Rect(0, 0, 1400, 900));
            window.UpdateLayout();

            var trays = FindLogical<ToolBarTray>(window);
            trays.Count.ShouldBeGreaterThanOrEqualTo(2, "top and bottom trays should exist");
            var toolbar = FindLogical<ToolBar>(window).FirstOrDefault(t => t.Name == "MainToolbar");
            toolbar.ShouldNotBeNull("the main toolbar should exist");
            trays[0].ToolBars.ShouldContain(toolbar!);

            // Simulate the drag: reparent to the bottom tray and back.
            trays[0].ToolBars.Remove(toolbar!);
            trays[1].ToolBars.Add(toolbar!);
            window.UpdateLayout();
            trays[1].ToolBars.ShouldContain(toolbar!);
            trays[0].ToolBars.ShouldNotContain(toolbar!);
        }
        finally
        {
            window.Close();
        }
    });
}
