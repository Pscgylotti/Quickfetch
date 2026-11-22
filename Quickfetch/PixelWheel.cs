using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Quickfetch;

internal static class PixelWheel
{
    internal static void Attach(ListBox list, double pixelsPerNotch)
    {
        bool pending = false;
        double target = 0;
        list.PreviewMouseWheel += (_, e) =>
        {
            if (FindScrollViewer(list) is not { ScrollableHeight: > 0 } scroll) return;
            e.Handled = true;
            if (!pending) target = scroll.VerticalOffset;
            target = Math.Clamp(target - e.Delta * pixelsPerNotch / 120, 0, scroll.ScrollableHeight);
            if (pending) return;
            pending = true;
            var items = list.ItemsSource;
            list.Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
            {
                pending = false;
                if (list.IsVisible && ReferenceEquals(items, list.ItemsSource)) scroll.ScrollToVerticalOffset(target);
            });
        };
    }
    internal static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer scroll) return scroll;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(parent, i)) is { } found) return found;
        return null;
    }
}
