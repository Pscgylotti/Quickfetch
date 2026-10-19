using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Quickfetch;

// Own the touch contact before WPF can promote it to mouse/panning input.
public static class TouchScrollBar
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(TouchScrollBar), new PropertyMetadata(false, Changed));
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.RegisterAttached("Active", typeof(bool), typeof(TouchScrollBar), new PropertyMetadata(false));
    public static bool GetActive(DependencyObject obj) => (bool)obj.GetValue(ActiveProperty);
    public static void SetActive(DependencyObject obj, bool value) => obj.SetValue(ActiveProperty, value);
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(State), typeof(TouchScrollBar));
    private sealed record State(int Contact, double StartY, double StartOffset);
    public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);
    private static void Changed(DependencyObject obj, DependencyPropertyChangedEventArgs e)
    {
        if (obj is not ScrollBar bar) return;
        if ((bool)e.NewValue)
        {
            bar.PreviewTouchDown += Down; bar.PreviewTouchMove += Move; bar.PreviewTouchUp += Up;
            bar.LostTouchCapture += Lost;
        }
        else
        {
            bar.PreviewTouchDown -= Down; bar.PreviewTouchMove -= Move; bar.PreviewTouchUp -= Up;
            bar.LostTouchCapture -= Lost; bar.ClearValue(StateProperty);
        }
    }
    internal static ScrollBar? Find(DependencyObject? obj)
    {
        while (obj is not null)
        {
            if (obj is ScrollBar bar) return bar;
            obj = obj is Visual ? VisualTreeHelper.GetParent(obj) : LogicalTreeHelper.GetParent(obj);
        }
        return null;
    }
    private static ScrollViewer? Owner(DependencyObject obj)
    {
        for (var parent = VisualTreeHelper.GetParent(obj); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ScrollViewer scroll) return scroll;
        return null;
    }
    internal static double Offset(double start, double delta, double range, double travel) =>
        Math.Clamp(start + delta * range / Math.Max(1, travel), 0, Math.Max(0, range));
    private static void Down(object? sender, TouchEventArgs e)
    {
        var bar = (ScrollBar)sender!;
        if (bar.Orientation != Orientation.Vertical || Owner(bar) is not { } scroll) return;
        e.Handled = true;
        if (bar.GetValue(StateProperty) is State) return;
        bar.ApplyTemplate();
        if (bar.Template.FindName("PART_Track", bar) is not Track track) return;
        var y = e.GetTouchPoint(track).Position.Y;
        var thumbTop = track.Thumb.TranslatePoint(new Point(), track).Y;
        var thumbHeight = track.Thumb.ActualHeight;
        if (y < thumbTop || y > thumbTop + thumbHeight)
        {
            scroll.ScrollToVerticalOffset(Offset(0, y - thumbHeight / 2, scroll.ScrollableHeight, track.ActualHeight - thumbHeight));
            scroll.UpdateLayout();
        }
        bar.SetValue(StateProperty, new State(e.TouchDevice.Id, y, scroll.VerticalOffset));
        SetActive(bar, true);
        e.TouchDevice.Capture(bar);
    }
    private static void Move(object? sender, TouchEventArgs e)
    {
        var bar = (ScrollBar)sender!;
        if (bar.GetValue(StateProperty) is not State state) return;
        e.Handled = true;
        if (state.Contact != e.TouchDevice.Id || Owner(bar) is not { } scroll || bar.Template.FindName("PART_Track", bar) is not Track track) return;
        var y = e.GetTouchPoint(track).Position.Y;
        scroll.ScrollToVerticalOffset(Offset(state.StartOffset, y - state.StartY, scroll.ScrollableHeight, track.ActualHeight - track.Thumb.ActualHeight));
    }
    private static void Up(object? sender, TouchEventArgs e)
    {
        var bar = (ScrollBar)sender!;
        if (bar.GetValue(StateProperty) is not State state) return;
        e.Handled = true;
        if (state.Contact != e.TouchDevice.Id) return;
        Move(sender, e); bar.ClearValue(StateProperty); SetActive(bar, false); e.TouchDevice.Capture(null);
    }
    private static void Lost(object? sender, TouchEventArgs e)
    {
        var bar = (ScrollBar)sender!;
        if (bar.GetValue(StateProperty) is State state && state.Contact == e.TouchDevice.Id) { bar.ClearValue(StateProperty); SetActive(bar, false); }
    }
}
