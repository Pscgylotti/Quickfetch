using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Quickfetch;
using Quickfetch.Core;

internal static class PinnedDragChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static void Run(Action<bool, string> check)
    {
        var entries = new[] { new ShortcutEntry("First", "C:\\Pin1.lnk"), new ShortcutEntry("Second", "C:\\Pin2.lnk"), new ShortcutEntry("Third", "C:\\Pin3.lnk") };
        var settings = new Settings { PinnedApps = [entries[0].Path, "C:\\Missing.lnk", entries[1].Path, entries[2].Path] };
        var saves = 0; var main = new MainWindow(settings, _ => saves++);
        typeof(MainWindow).GetField("_entries", Private)!.SetValue(main, entries);
        typeof(MainWindow).GetField("_choosingFolder", Private)!.SetValue(main, true);
        try
        {
            main.ShowLauncher(); Pump(300); main.UpdateLayout();
            var rail = (StackPanel)main.FindName("PinnedItems");
            var first = (Button)rail.Children[0]; var touch = new Touch(main, 2101); var firstPoint = Center(first, main);
            touch.Down(firstPoint); Pump(480);
            check(Field<bool>(main, "_pinDragging") && touch.Captured == main, "Long press must start a drag and transfer capture to the stable window before reordering.");
            check(main.OwnedWindows.Count == 2 && main.OwnedWindows.Cast<Window>().All(w => !w.ShowActivated && !w.IsHitTestVisible), "The icon preview and top unpin strip must neither steal focus nor intercept captured touch.");
            var third = (Button)rail.Children[2]; var end = third.TranslatePoint(new Point(third.ActualWidth / 2, third.ActualHeight + 8), main);
            touch.Move(end); main.UpdateLayout();
            check(ReferenceEquals(rail.Children[2], first) && touch.Captured == main, "Live insertion must move the source to the end without losing touch capture.");
            check(settings.PinnedApps[0] == entries[0].Path && saves == 0, "Previewing an insertion must not write settings before release.");
            touch.Up(end); Pump(30);
            check(settings.PinnedApps.SequenceEqual(new[] { entries[1].Path, "C:\\Missing.lnk", entries[2].Path, entries[0].Path }) && saves == 1, "Release must save the new order once while retaining unavailable identities in their slots.");
            check(touch.Captured is null && main.OwnedWindows.Count == 0, "Release must destroy drag visuals and release capture.");
            first = (Button)rail.Children[0]; touch = new Touch(main, 2102); touch.Down(Center(first, main)); Pump(480);
            var next = (Button)rail.Children[2]; touch.Move(next.TranslatePoint(new Point(20, next.ActualHeight + 8), main));
            Call(main, "CancelPinnedDrag"); Pump(30);
            check(settings.PinnedApps[0] == entries[1].Path && (string)((Button)rail.Children[0]).Tag == entries[1].Path && saves == 1, "Cancelling must roll back the live preview without persisting a reorder.");
            touch.Up(Center((Button)rail.Children[0], main));
            first = (Button)rail.Children[0]; touch = new Touch(main, 2103); touch.Down(Center(first, main)); Pump(480);
            var monitor = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(main).Handle).Bounds;
            var remove = main.PointFromScreen(new Point(monitor.Left + monitor.Width / 2, monitor.Top + 20));
            touch.Move(remove);
            check(Field<PinnedDragVisuals>(main, "_pinVisuals").OverRemove, "The physical top strip must recognize a captured finger outside the launcher.");
            touch.Up(remove); Pump(30);
            check(!settings.PinnedApps.Contains(entries[1].Path) && saves == 2 && rail.Children.Count == 2, "Dropping in the top strip must unpin and persist exactly once.");
            CheckMenu(main, rail, check);
        }
        finally { main.DisposeLauncher(); main.Close(); }
        check(GestureActionRunner.HeldModifiers(key => key is 0x11 or 0x12).SequenceEqual(new[] { "Ctrl", "Alt" }), "Modifier diagnostics must identify actual reported keys without claiming physical input.");
        var reads = 0;
        Task.Run(() => GestureActionRunner.WaitForModifierRelease(() => ++reads < 4 ? new[] { "Alt" } : [], CancellationToken.None)).GetAwaiter().GetResult();
        check(reads == 4, "A previous synthetic modifier must be allowed to settle instead of immediately rejecting the next gesture.");
        try { Task.Run(() => GestureActionRunner.WaitForModifierRelease(() => new[] { "Shift" }, CancellationToken.None, 30)).GetAwaiter().GetResult(); check(false, "Held modifiers must eventually time out."); }
        catch (InvalidOperationException ex) { check(ex.Message.Contains("Shift"), "The bounded timeout must name the modifier still held."); }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { Task.Run(() => GestureActionRunner.WaitForModifierRelease(() => new[] { "Alt" }, cancellation.Token)).GetAwaiter().GetResult(); check(false, "Pending gestures must cancel."); }
        catch (OperationCanceledException) { check(true, "Pending gestures cancel on shutdown/reconfiguration."); }
        var notice = new GestureNoticeWindow(); notice.Display("Gesture status test"); Pump(100);
        check(notice.IsVisible && !notice.ShowActivated && !notice.ShowInTaskbar, "Gesture errors must use a non-activating local floating window.");
        Pump(4200); check(!notice.IsVisible, "The floating message must close after four seconds and its short fade.");
    }
    private static void CheckMenu(MainWindow main, StackPanel rail, Action<bool, string> check)
    {
        var resources = Application.Current.Resources;
        var oldInk = resources["Ink"]; var oldSurface = resources["TooltipSurface"];
        try
        {
            resources["Ink"] = new SolidColorBrush(Color.FromRgb(237, 242, 255)); resources["TooltipSurface"] = new SolidColorBrush(Color.FromRgb(32, 36, 43));
            var menu = ((Button)rail.Children[0]).ContextMenu; menu.PlacementTarget = (Button)rail.Children[0]; menu.IsOpen = true; Pump(50); menu.UpdateLayout();
            var border = Descendants<Border>(menu).First(); var item = (MenuItem)menu.Items[0];
            check(((SolidColorBrush)border.Background).Color.R < 80 && ((SolidColorBrush)item.Foreground).Color.R > 200, "The actual right-click popup must use a dark surface with legible light text.");
            check(item.ActualHeight >= 44 && menu.ActualWidth < 230, "The unpin menu must have a touch-sized row without the native icon gutter.");
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth), (int)Math.Ceiling(menu.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(menu);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = System.IO.File.Create("artifacts/pinned-context-menu.png")) png.Save(stream);
            menu.IsOpen = false;
        }
        finally { resources["Ink"] = oldInk; resources["TooltipSurface"] = oldSurface; }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T t) yield return t; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    private static T Field<T>(MainWindow main, string name) => (T)typeof(MainWindow).GetField(name, Private)!.GetValue(main)!;
    private static void Call(MainWindow main, string name) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(main, null);
    private static Point Center(Button button, Window window) => button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2), window);
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    private sealed class Touch(Window window, int id) : TouchDevice(id)
    {
        private Point _point; private TouchAction _action;
        public void Down(Point point) { _point = point; _action = TouchAction.Down; SetActiveSource(PresentationSource.FromVisual(window)); Activate(); ReportDown(); }
        public void Move(Point point) { _point = point; _action = TouchAction.Move; ReportMove(); }
        public void Up(Point point) { _point = point; _action = TouchAction.Up; ReportUp(); Deactivate(); }
        public override TouchPoint GetTouchPoint(IInputElement relativeTo)
        { var point = relativeTo is Visual visual ? window.TransformToVisual(visual).Transform(_point) : _point; return new(this, point, new Rect(point, new Size(2, 2)), _action); }
        public override TouchPointCollection GetIntermediateTouchPoints(IInputElement relativeTo) => new() { GetTouchPoint(relativeTo) };
    }
}
