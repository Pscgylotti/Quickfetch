using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Quickfetch;

public partial class MainWindow
{
    private readonly DispatcherTimer _pinHold = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer _pinScroll = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private Button? _pinPressed;
    private ShortcutItem? _pinItem;
    private TouchDevice? _pinTouch;
    private Point _pinStart, _pinLast;
    private bool _pinMoved, _pinDragging, _pinnedMenuOpen;
    private PinnedDragVisuals? _pinVisuals;
    private void InitializePinnedDrag()
    {
        _pinScroll.Tick += (_, _) => { if (_pinDragging && _pinVisuals?.OverRemove != true) PreviewPinnedInsertion(_pinLast); };
        _pinHold.Tick += (_, _) => { _pinHold.Stop(); BeginPinnedDrag(); };
        PreviewTouchMove += (_, e) => { if (_pinTouch?.Id == e.TouchDevice.Id && e.TouchDevice.Captured == this) { e.Handled = true; MovePinnedPress(PointToScreen(e.GetTouchPoint(this).Position)); } };
        PreviewTouchUp += (_, e) => { if (_pinTouch?.Id == e.TouchDevice.Id && e.TouchDevice.Captured == this) { e.Handled = true; EndPinnedPress(PointToScreen(e.GetTouchPoint(this).Position)); } };
        PreviewMouseMove += (_, e) => { if (_pinDragging && _pinTouch is null && Mouse.Captured == this) { e.Handled = true; MovePinnedPress(PointToScreen(e.GetPosition(this))); } };
        PreviewMouseLeftButtonUp += (_, e) => { if (_pinDragging && _pinTouch is null && Mouse.Captured == this) { e.Handled = true; EndPinnedPress(PointToScreen(e.GetPosition(this))); } };
        LostTouchCapture += (_, e) => { if (_pinDragging && _pinTouch?.Id == e.TouchDevice.Id && e.TouchDevice.Captured != this) CancelPinnedDrag(); };
        LostMouseCapture += (_, _) => { if (_pinDragging && _pinTouch is null && Mouse.Captured != this) CancelPinnedDrag(); };
    }
    private void AttachPinnedInput(Button button, ShortcutItem item)
    {
        // Disable WPF's press-and-hold right-click so it cannot compete with our long press.
        Stylus.SetIsPressAndHoldEnabled(button, false); Stylus.SetIsFlicksEnabled(button, false);
        button.Click += (_, _) => LaunchResult(item); // Accessibility / keyboard activation.
        button.PreviewTouchDown += (_, e) =>
        {
            e.Handled = true; if (_pinPressed is not null) return;
            StartPinnedPress(button, item, PointToScreen(e.GetTouchPoint(this).Position)); _pinTouch = e.TouchDevice;
            if (!e.TouchDevice.Capture(button)) CancelPinnedDrag();
        };
        button.PreviewTouchMove += (_, e) =>
        {
            if (_pinTouch?.Id != e.TouchDevice.Id) return;
            e.Handled = true; MovePinnedPress(PointToScreen(e.GetTouchPoint(this).Position));
        };
        button.PreviewTouchUp += (_, e) =>
        {
            if (_pinTouch?.Id != e.TouchDevice.Id) return;
            e.Handled = true; EndPinnedPress(PointToScreen(e.GetTouchPoint(this).Position));
        };
        button.LostTouchCapture += (_, e) => { if (!_pinDragging && _pinTouch?.Id == e.TouchDevice.Id) CancelPinnedDrag(); };
        button.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.StylusDevice is not null) { e.Handled = true; return; }
            e.Handled = true; if (_pinPressed is not null) return;
            StartPinnedPress(button, item, PointToScreen(e.GetPosition(this))); button.CaptureMouse();
        };
        button.PreviewMouseMove += (_, e) =>
        {
            if (_pinPressed != button || _pinTouch is not null || e.LeftButton != MouseButtonState.Pressed) return;
            e.Handled = true; var point = PointToScreen(e.GetPosition(this));
            if (!_pinDragging && (point - _pinStart).Length > 6) BeginPinnedDrag();
            MovePinnedPress(point);
        };
        button.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (e.StylusDevice is not null) { e.Handled = true; return; }
            if (_pinPressed != button || _pinTouch is not null) return;
            e.Handled = true; EndPinnedPress(PointToScreen(e.GetPosition(this)));
        };
        button.LostMouseCapture += (_, _) => { if (!_pinDragging && _pinPressed == button && _pinTouch is null) CancelPinnedDrag(); };
    }
    private void StartPinnedPress(Button button, ShortcutItem item, Point point)
    {
        _pinPressed = button; _pinItem = item; _pinStart = _pinLast = point; _pinMoved = false;
        _pinHold.Start(); ToolTipService.SetIsEnabled(button, false);
    }
    private void BeginPinnedDrag()
    {
        if (_pinPressed is null || _pinItem is null || !IsVisible || _pinDragging) return;
        _pinHold.Stop(); _pinDragging = true; _pinMoved = true;
        _pinPressed.ContextMenu.IsOpen = false;
        _suppressFileGestureUntil = Environment.TickCount64 + 500;
        if (_pinTouch is not null) { if (!_pinTouch.Capture(this)) { CancelPinnedDrag(); return; } }
        else if (!CaptureMouse()) { CancelPinnedDrag(); return; }
        PinnedRail.SetResourceReference(Border.BorderBrushProperty, "Accent");
        try { _pinVisuals = new PinnedDragVisuals(this, _pinItem, _pinLast); _pinPressed.Opacity = .28; _pinScroll.Start(); }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { CancelPinnedDrag(); SetMessage(Loc.T("无法开始拖动：") + ex.Message); }
    }
    private void MovePinnedPress(Point point)
    {
        var previous = _pinLast; _pinLast = point;
        if (_pinDragging)
        {
            _pinVisuals?.Move(point);
            if (_pinVisuals?.OverRemove != true) PreviewPinnedInsertion(point);
            return;
        }
        var dpi = VisualTreeHelper.GetDpi(this);
        if ((point - _pinStart).Length > 12 * dpi.DpiScaleY)
        {
            _pinMoved = true; _pinHold.Stop();
            if (_pinTouch is not null && PinnedItems.Parent is ScrollViewer scroll) scroll.ScrollToVerticalOffset(scroll.VerticalOffset + (previous.Y - point.Y) / dpi.DpiScaleY);
        }
    }
    internal void PreviewPinnedInsertion(Point screenPoint)
    {
        if (_pinPressed is null) return;
        var local = PinnedRail.PointFromScreen(screenPoint);
        if (local.X < -24 || local.X > PinnedRail.ActualWidth + 48 || local.Y < 0 || local.Y > PinnedRail.ActualHeight) return;
        if (PinnedItems.Parent is ScrollViewer scroll)
        {
            if (local.Y < 36) scroll.ScrollToVerticalOffset(scroll.VerticalOffset - 16);
            else if (local.Y > PinnedRail.ActualHeight - 36) scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 16);
        }
        var buttons = PinnedItems.Children.Cast<Button>().ToArray();
        var others = buttons.Where(button => button != _pinPressed).ToArray();
        var target = others.Count(button => screenPoint.Y > button.PointToScreen(new Point(0, button.ActualHeight / 2)).Y - (button.RenderTransform is TranslateTransform animation ? animation.Y * VisualTreeHelper.GetDpi(this).DpiScaleY : 0));
        if (PinnedItems.Children.IndexOf(_pinPressed) == target) return;
        var positions = buttons.ToDictionary(button => button, button => button.TranslatePoint(new Point(), PinnedItems).Y);
        foreach (var button in buttons) if (button.RenderTransform is TranslateTransform active) { active.BeginAnimation(TranslateTransform.YProperty, null); active.Y = 0; }
        PinnedItems.Children.Remove(_pinPressed); PinnedItems.Children.Insert(target, _pinPressed); PinnedItems.UpdateLayout();
        foreach (var button in buttons)
        {
            var shift = positions[button] - button.TranslatePoint(new Point(), PinnedItems).Y;
            if (button == _pinPressed || Math.Abs(shift) < .1) continue;
            var transform = button.RenderTransform as TranslateTransform ?? new TranslateTransform(); button.RenderTransform = transform;
            transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(shift, 0, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        }
    }
    private void EndPinnedPress(Point point)
    {
        if (_pinPressed is null) return;
        var item = _pinItem; var launch = !_pinMoved && !_pinDragging;
        if (_pinDragging)
        {
            _pinVisuals?.Move(point);
            var unpin = _pinVisuals?.OverRemove == true;
            var local = PinnedRail.PointFromScreen(point);
            if (unpin || (local.X >= -24 && local.X <= PinnedRail.ActualWidth + 48 && local.Y >= 0 && local.Y <= PinnedRail.ActualHeight))
                SavePinnedOrder(unpin ? item?.Path : null);
        }
        CancelPinnedDrag();
        if (launch && item is not null) LaunchResult(item);
    }
    internal void SavePinnedOrder(string? remove = null)
    {
        var old = _settings.PinnedApps ?? [];
        var visible = PinnedItems.Children.Cast<Button>().Select(button => (string)button.Tag).ToArray();
        // Keep temporarily unavailable saved identities in their slots.
        var ordered = new System.Collections.Generic.Queue<string>(visible.Where(path => !path.Equals(remove, StringComparison.OrdinalIgnoreCase)));
        _settings.PinnedApps = old.Where(path => !string.IsNullOrWhiteSpace(path) && !path.Equals(remove, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => visible.Contains(path, StringComparer.OrdinalIgnoreCase) ? ordered.Dequeue() : path).ToArray();
        try { _saveSettings(_settings); }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { _settings.PinnedApps = old; SetMessage(Loc.T("保存固定应用失败：") + ex.Message); }
    }
    private void CancelPinnedDrag()
    {
        _pinHold.Stop(); _pinScroll.Stop(); var button = _pinPressed; var touch = _pinTouch;
        _pinPressed = null; _pinTouch = null; _pinItem = null; _pinDragging = false;
        _pinVisuals?.Dispose(); _pinVisuals = null;
        PinnedRail.SetResourceReference(Border.BorderBrushProperty, "Line");
        if (button is not null) _suppressFileGestureUntil = Environment.TickCount64 + 300;
        if (button is not null) { button.Opacity = 1; ToolTipService.SetIsEnabled(button, true); if (touch?.Captured == button || touch?.Captured == this) touch?.Capture(null); if (Mouse.Captured == button || Mouse.Captured == this) Mouse.Capture(null); }
        if (button is not null) { _pinnedPaths = null; RefreshPinnedApps(); }
    }
}
