using System;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Quickfetch;

public partial class MainWindow
{
    private readonly DispatcherTimer _fileHold = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool _fileHoldMoved, _draggingFile;
    private Point _fileHoldPoint, _fileHoldScreen, _mouseFileStart;
    private FileItem? _mouseFileItem;
    private long _suppressFileGestureUntil;
    private void ShowFileFlyout(ILauncherItem item, Point point)
    {
        if (_choosingFolder || _draggingFile) return;
        _choosingFolder = true;
        try
        {
            var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var location = transform.Transform(point);
            var flyout = new FileFlyout(item.Name) { Owner = this, Left = location.X, Top = location.Y };
            flyout.Loaded += (_, _) =>
            {
                var work = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)point.X, (int)point.Y)).WorkingArea;
                var bounds = new MatrixTransform(transform).TransformBounds(new Rect(work.X, work.Y, work.Width, work.Height));
                flyout.Left = Math.Clamp(flyout.Left, bounds.Left, Math.Max(bounds.Left, bounds.Right - flyout.ActualWidth));
                flyout.Top = Math.Clamp(flyout.Top, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - flyout.ActualHeight));
            };
            flyout.ShowDialog();
            switch (flyout.Action)
            {
                case FileMenuAction.Open: Results.SelectedItem = item; LaunchSelected(); break;
                case FileMenuAction.OpenFolder: Results.SelectedItem = item; LaunchSelected(true); break;
                case FileMenuAction.CopyPath: Clipboard.SetText(item.Path); FocusSearch(); break;
                case FileMenuAction.Copy: ShellContextMenu.InvokeVerb(new WindowInteropHelper(this).Handle, item.Path, "copy"); FocusSearch(); break;
                case FileMenuAction.Cut: ShellContextMenu.InvokeVerb(new WindowInteropHelper(this).Handle, item.Path, "cut"); FocusSearch(); break;
                case FileMenuAction.Properties: ShellContextMenu.InvokeVerb(new WindowInteropHelper(this).Handle, item.Path, "properties"); break;
                case FileMenuAction.More:
                    var result = ShellContextMenu.Show(new WindowInteropHelper(this).Handle, item.Path, (int)point.X, (int)point.Y);
                    if (result.Invoked) Hide(); else FocusSearch(); break;
                default: FocusSearch(); break;
            }
        }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { SetMessage(Loc.T("无法完成文件操作：") + ex.Message); }
        finally { _choosingFolder = false; }
    }
    private void Results_FileMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_fileMode || _draggingFile || e.StylusDevice is not null) return;
        _mouseFileStart = e.GetPosition(Results);
        _mouseFileItem = (ItemsControl.ContainerFromElement(Results, e.OriginalSource as DependencyObject) as ListBoxItem)?.Content as FileItem;
    }
    private void Results_FileMouseMove(object sender, MouseEventArgs e)
    {
        if (!_fileMode || _draggingFile || e.StylusDevice is not null || e.LeftButton != MouseButtonState.Pressed || _mouseFileItem is not { } item) return;
        var delta = e.GetPosition(Results) - _mouseFileStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _mouseFileItem = null; e.Handled = true;
        StartFileDrag(item, Results.PointToScreen(e.GetPosition(Results)), touch: false);
    }
    private void Results_FileMouseUp(object sender, MouseButtonEventArgs e) => _mouseFileItem = null;
    internal static DataObject CreateFileDragData(string path)
    {
        var data = new DataObject(); data.SetFileDropList(new StringCollection { path });
        return data;
    }
    private void StartFileDrag(FileItem item, Point point, bool touch)
    {
        if (_draggingFile || !IsVisible) return;
        _fileHold.Stop(); _fileTouches.Reset(); _doubleTap.Reset(); _fileTouchItem = null;
        _draggingFile = _choosingFolder = true;
        Results.SelectedItem = item;
        var oldTopmost = Topmost;
        FileDragVisuals? visuals = null; TouchDragBridge? bridge = null;
        GlobalTouchGestures? temporaryListener = null;
        var deadline = Environment.TickCount64 + 60000;
        var dropped = DragDropEffects.None;
        var safety = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        var expired = false;
        QueryContinueDragEventHandler continueDrag = (_, e) =>
        {
            if (expired || e.EscapePressed || bridge?.Cancelled == true) { e.Action = DragAction.Cancel; e.Handled = true; return; }
            var released = touch ? bridge?.Released == true : (e.KeyStates & DragDropKeyStates.LeftMouseButton) == 0;
            if (touch || released && visuals?.PointerOverCancel() == true)
            {
                e.Action = released ? visuals?.PointerOverCancel() == true ? DragAction.Cancel : DragAction.Drop : DragAction.Continue;
                e.Handled = true;
            }
        };
        try
        {
            var data = CreateFileDragData(item.Path);
            visuals = new FileDragVisuals(this, point);
            if (touch)
            {
                var listener = _gestures;
                if (listener is null) listener = temporaryListener = new GlobalTouchGestures(new Quickfetch.Core.GestureSettings { DisplayDeviceName = _settings.Gestures.DisplayDeviceName }, (_, _, _) => { }, _ => { });
                bridge = new TouchDragBridge(listener, point);
            }
            PreviewQueryContinueDrag += continueDrag;
            safety.Tick += (_, _) =>
            {
                if (Environment.TickCount64 < deadline) return;
                expired = true;
                if (FileDragVisuals.TryPointer(out var cursor)) GestureActionRunner.MoveDragPointer((int)cursor.X, (int)cursor.Y);
            };
            safety.Start();
            visuals.Show();
            // A hidden source cannot obstruct the real window beneath it. The separate
            // blurred ghost is layered/click-through; only the top cancel strip accepts drops.
            Hide();
            dropped = DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { AppDiagnostics.Current.Fault("file-drag", ex); SetMessage(Loc.T("无法拖放文件：") + ex.Message); }
        finally
        {
            safety.Stop(); PreviewQueryContinueDrag -= continueDrag;
            bridge?.Dispose(); temporaryListener?.Dispose(); visuals?.Dispose();
            Topmost = oldTopmost; _suppressFileGestureUntil = Environment.TickCount64 + 500;
            _draggingFile = _choosingFolder = false;
            if (!_disposed && dropped == DragDropEffects.None) { Show(); Activate(); FocusSearch(); if (_fileMode) QueueFileSearch(); }
        }
    }
}
