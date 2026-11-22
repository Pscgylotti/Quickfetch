using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Quickfetch;

public partial class MainWindow
{
    private readonly DispatcherTimer _wheelHold = new() { Interval = TimeSpan.FromMilliseconds(320) };
    private TouchDevice? _wheelFinger;
    private bool _wheelPressed;
    private Point _wheelStart;
    private Popup? _radialPopup;
    private ExtensionWheel? _wheel;

    private void InitializeExtensionGesture()
    {
        Stylus.SetIsPressAndHoldEnabled(ExtensionsButton, false);
        _wheelHold.Tick += (_, _) => ShowExtensionWheel();
        ExtensionsButton.PreviewTouchDown += (_, e) =>
        {
            e.Handled = true;
            if (_wheelPressed || _choosingFolder) return;
            if (!e.TouchDevice.Capture(ExtensionsButton)) return;
            _wheelFinger = e.TouchDevice;
            BeginExtensionPress(e.GetTouchPoint(ExtensionsButton).Position);
        };
        ExtensionsButton.PreviewTouchMove += (_, e) =>
        {
            if (e.TouchDevice != _wheelFinger) return;
            e.Handled = true;
            UpdateExtensionPress(e.GetTouchPoint(ExtensionsButton).Position);
        };
        ExtensionsButton.PreviewTouchUp += (_, e) =>
        {
            if (e.TouchDevice != _wheelFinger) return;
            e.Handled = true;
            EndExtensionPress(e.GetTouchPoint(ExtensionsButton).Position);
        };
        ExtensionsButton.LostTouchCapture += (_, e) => { if (e.TouchDevice == _wheelFinger) CancelExtensionPress(); };
        ExtensionsButton.PreviewMouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (_wheelPressed || _choosingFolder || e.StylusDevice?.TabletDevice.Type == TabletDeviceType.Touch) return;
            if (ExtensionsButton.CaptureMouse()) BeginExtensionPress(e.GetPosition(ExtensionsButton));
        };
        ExtensionsButton.PreviewMouseMove += (_, e) =>
        {
            if (_wheelPressed && _wheelFinger == null) UpdateExtensionPress(e.GetPosition(ExtensionsButton));
        };
        ExtensionsButton.PreviewMouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_wheelPressed && _wheelFinger == null) EndExtensionPress(e.GetPosition(ExtensionsButton));
        };
        ExtensionsButton.LostMouseCapture += (_, _) => { if (_wheelFinger == null && _wheelPressed) CancelExtensionPress(); };
    }

    private void BeginExtensionPress(Point point)
    {
        _wheelPressed = true;
        _wheelStart = point;
        _wheelHold.Start();
    }

    private void ShowExtensionWheel()
    {
        _wheelHold.Stop();
        if (!_wheelPressed || _radialPopup?.IsOpen == true) return;
        CloseQuickToggles();
        ExtensionsPanel.Visibility = Visibility.Collapsed;
        SetToggled(ExtensionsButton, true);
        var buttons = ExtensionEntries.Children.OfType<Button>().ToArray();
        _wheel = new ExtensionWheel(buttons.Select(AutomationProperties.GetName).ToArray(),
            (Brush)FindResource("TooltipSurface"), (Brush)FindResource("Ink"), (Brush)FindResource("Accent"), (Brush)FindResource("OnAccent"), (Brush)FindResource("Line"));
        _radialPopup = new Popup { PlacementTarget = ExtensionsButton, Placement = PlacementMode.Custom,
            CustomPopupPlacementCallback = (popup, target, _) => [new CustomPopupPlacement(
                new Point((target.Width - popup.Width) / 2, (target.Height - popup.Height) / 2), PopupPrimaryAxis.None)],
            AllowsTransparency = true,
            IsHitTestVisible = false, Focusable = false, StaysOpen = true, Child = _wheel, IsOpen = true };
    }

    private void UpdateExtensionPress(Point point)
    {
        if ((point - _wheelStart).Length >= 9) ShowExtensionWheel();
        if (_wheel != null && PresentationSource.FromVisual(_wheel) != null)
            _wheel.SelectAt(_wheel.PointFromScreen(ExtensionsButton.PointToScreen(point)));
    }

    private void EndExtensionPress(Point point)
    {
        UpdateExtensionPress(point);
        var wheel = _wheel;
        var tap = wheel == null && new Rect(ExtensionsButton.RenderSize).Contains(point);
        var selected = wheel?.Selected ?? -1;
        CancelExtensionPress();
        if (tap) ExtensionsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        else if (selected >= 0) ExtensionEntries.Children.OfType<Button>().ElementAt(selected).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private void CancelExtensionPress()
    {
        _wheelHold.Stop();
        var finger = _wheelFinger;
        _wheelPressed = false;
        _wheelFinger = null;
        if (finger?.Captured == ExtensionsButton) finger.Capture(null);
        if (ExtensionsButton.IsMouseCaptured) ExtensionsButton.ReleaseMouseCapture();
        if (_radialPopup != null)
        {
            _radialPopup.IsOpen = false;
            _radialPopup.Child = null;
            _radialPopup = null;
            SetToggled(ExtensionsButton, false);
        }
        _wheel = null;
    }
}

internal sealed class ExtensionWheel : FrameworkElement
{
    internal const double Radius = 118;
    private const double Hole = 25;
    private readonly string[] _labels;
    private readonly Brush _surface, _ink, _accent, _onAccent, _line;
    internal int Selected { get; private set; } = -1;
    internal ExtensionWheel(string[] labels, Brush surface, Brush ink, Brush accent, Brush onAccent, Brush line)
    {
        _labels = labels; _surface = surface; _ink = ink; _accent = accent; _onAccent = onAccent; _line = line;
        Width = Height = Radius * 2;
    }
    internal void SelectAt(Point point)
    {
        var x = point.X - Radius; var y = point.Y - Radius;
        var distance = Math.Sqrt(x * x + y * y);
        var sector = 2 * Math.PI / _labels.Length;
        var next = distance < Hole || distance > Radius + 20 ? -1 :
            (int)Math.Floor((Math.Atan2(y, x) + Math.PI / 2 + sector / 2 + Math.PI * 2) % (Math.PI * 2) / sector);
        if (next != Selected) { Selected = next; InvalidateVisual(); }
    }
    private static Point At(double a, double r) => new(Radius + Math.Cos(a) * r, Radius + Math.Sin(a) * r);
    protected override void OnRender(DrawingContext dc)
    {
        var span = Math.PI * 2 / _labels.Length;
        string[] glyphs = ["Ω", "\uE722", "\uE896"];
        for (int i = 0; i < _labels.Length; i++)
        {
            var mid = -Math.PI / 2 + i * span;
            var a = mid - span / 2 + .015; var b = mid + span / 2 - .015;
            var shape = new StreamGeometry();
            using (var ctx = shape.Open())
            {
                ctx.BeginFigure(At(a, Radius - 1), true, true);
                ctx.ArcTo(At(b, Radius - 1), new Size(Radius - 1, Radius - 1), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(At(b, Hole), true, false);
                ctx.ArcTo(At(a, Hole), new Size(Hole, Hole), 0, false, SweepDirection.Counterclockwise, true, false);
            }
            dc.DrawGeometry(i == Selected ? _accent : _surface, new Pen(_line, 1), shape);
            var pos = At(mid, 72);
            DrawText(dc, glyphs[i], i == 0 ? "Segoe UI" : "Segoe Fluent Icons, Segoe MDL2 Assets", 21,
                new Point(pos.X, pos.Y - 10), i == Selected ? _onAccent : _accent);
            DrawText(dc, _labels[i], "Microsoft YaHei UI", 10, new Point(pos.X, pos.Y + 13), i == Selected ? _onAccent : _ink);
        }
    }
    private void DrawText(DrawingContext dc, string text, string font, double size, Point center, Brush brush)
    {
        var label = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(font), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(label, new Point(center.X - label.Width / 2, center.Y - label.Height / 2));
    }
}
