using System.Windows;
using System.Windows.Controls;
using Scribbles.App.ViewModels;

namespace Scribbles.App;

/// <summary>Visual-tree helpers for the page viewer (one InkCanvas per page).</summary>
internal static class CanvasExplorer
{
    public static IEnumerable<InkCanvas> Find(DependencyObject? root)
    {
        if (root is null)
            yield break;
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(current, i);
                if (child is InkCanvas ink)
                    yield return ink;
                else
                    stack.Push(child);
            }
        }
    }

    public static int GetPageNumber(InkCanvas ink) =>
        (ink.DataContext as PageViewModel)?.Number ?? 0;
}
