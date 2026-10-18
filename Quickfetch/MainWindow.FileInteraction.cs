using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Quickfetch.Core;

namespace Quickfetch;

public partial class MainWindow
{
    public static readonly DependencyProperty FileNameWidthProperty = DependencyProperty.Register(nameof(FileNameWidth), typeof(GridLength), typeof(MainWindow), new PropertyMetadata(new GridLength(2.2, GridUnitType.Star)));
    public static readonly DependencyProperty FilePathWidthProperty = DependencyProperty.Register(nameof(FilePathWidth), typeof(GridLength), typeof(MainWindow), new PropertyMetadata(new GridLength(3, GridUnitType.Star)));
    public static readonly DependencyProperty FileSizeWidthProperty = DependencyProperty.Register(nameof(FileSizeWidth), typeof(GridLength), typeof(MainWindow), new PropertyMetadata(new GridLength(.9, GridUnitType.Star)));
    public static readonly DependencyProperty FileDateWidthProperty = DependencyProperty.Register(nameof(FileDateWidth), typeof(GridLength), typeof(MainWindow), new PropertyMetadata(new GridLength(1.6, GridUnitType.Star)));
    public static readonly DependencyProperty FileRowsWidthProperty = DependencyProperty.Register(nameof(FileRowsWidth), typeof(double), typeof(MainWindow), new PropertyMetadata(double.NaN));
    public GridLength FileNameWidth { get => (GridLength)GetValue(FileNameWidthProperty); set => SetValue(FileNameWidthProperty, value); }
    public GridLength FilePathWidth { get => (GridLength)GetValue(FilePathWidthProperty); set => SetValue(FilePathWidthProperty, value); }
    public GridLength FileSizeWidth { get => (GridLength)GetValue(FileSizeWidthProperty); set => SetValue(FileSizeWidthProperty, value); }
    public GridLength FileDateWidth { get => (GridLength)GetValue(FileDateWidthProperty); set => SetValue(FileDateWidthProperty, value); }
    public double FileRowsWidth { get => (double)GetValue(FileRowsWidthProperty); set => SetValue(FileRowsWidthProperty, value); }
    private readonly FileTouchTracker _fileTouches = new();
    private ILauncherItem? _fileTouchItem;
    private int? _columnTouch;
    private double _columnTouchX;
    private void InitializeFileColumns()
    {
        var weights = _settings.FileColumnWeights;
        if (weights is not { Length: 4 } || weights.Any(w => !double.IsFinite(w) || w <= 0)) weights = [2.2, 3, .9, 1.6];
        SetColumnWeights(weights);
        _fileHold.Tick += (_, _) =>
        {
            _fileHold.Stop();
            if (_fileTouches.Active && _fileTouchItem is FileItem item && Results.Items.Contains(item) && !_fileHoldMoved)
                StartFileDrag(item, _fileHoldScreen, touch: true);
        };
    }
    private void SetColumnWeights(double[] weights)
    {
        FileNameWidth = new(weights[0], GridUnitType.Star); FilePathWidth = new(weights[1], GridUnitType.Star);
        FileSizeWidth = new(weights[2], GridUnitType.Star); FileDateWidth = new(weights[3], GridUnitType.Star);
    }
    private void FileHeader_SizeChanged(object sender, SizeChangedEventArgs e) => FileRowsWidth = e.NewSize.Width;
    internal void ResizeFileColumns(int index, double delta)
    {
        if (index is < 0 or > 2 || !double.IsFinite(delta)) return;
        var widths = FileSortHeader.ColumnDefinitions.Select(c => c.ActualWidth).ToArray();
        double[] minimum = [96, 112, 64, 108];
        if (widths[index] + widths[index + 1] < minimum[index] + minimum[index + 1]) return;
        delta = Math.Clamp(delta, minimum[index] - widths[index], widths[index + 1] - minimum[index + 1]);
        widths[index] += delta; widths[index + 1] -= delta;
        SetColumnWeights(widths);
        FileSortHeader.UpdateLayout();
    }
    private void SaveFileColumns()
    {
        _settings.FileColumnWeights = [FileNameWidth.Value, FilePathWidth.Value, FileSizeWidth.Value, FileDateWidth.Value];
        try { _settings.Save(); } catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { SetMessage("列宽已调整，保存失败：" + ex.Message); }
    }
    private void FileColumn_DragDelta(object sender, DragDeltaEventArgs e) { ResizeFileColumns(int.Parse((string)((Thumb)sender).Tag), e.HorizontalChange); e.Handled = true; }
    private void FileColumn_DragCompleted(object sender, DragCompletedEventArgs e) => SaveFileColumns();
    private void FileColumn_TouchDown(object sender, TouchEventArgs e)
    {
        e.Handled = true;
        if (_columnTouch is not null) return;
        _columnTouch = e.TouchDevice.Id; _columnTouchX = e.GetTouchPoint(FileSortHeader).Position.X;
        e.TouchDevice.Capture((Thumb)sender);
    }
    private void FileColumn_TouchMove(object sender, TouchEventArgs e)
    {
        e.Handled = true; if (_columnTouch != e.TouchDevice.Id) return;
        var x = e.GetTouchPoint(FileSortHeader).Position.X;
        ResizeFileColumns(int.Parse((string)((Thumb)sender).Tag), x - _columnTouchX); _columnTouchX = x;
    }
    private void FileColumn_TouchUp(object sender, TouchEventArgs e)
    {
        e.Handled = true; if (_columnTouch != e.TouchDevice.Id) return;
        _columnTouch = null; e.TouchDevice.Capture(null); SaveFileColumns();
    }
    private void FileColumn_LostTouchCapture(object sender, TouchEventArgs e)
    {
        if (_columnTouch != e.TouchDevice.Id) return;
        _columnTouch = null; SaveFileColumns();
    }
    private bool LocalFileTouch => _fileMode;
    private bool IsFileTouchSurface(int x, int y)
    {
        if (!IsVisible || !LocalFileTouch) return false;
        var point = Results.PointFromScreen(new Point(x, y));
        return point.X >= 0 && point.Y >= 0 && point.X < Results.ActualWidth && point.Y < Results.ActualHeight;
    }
    private bool FileResultsTouchDown(TouchEventArgs e)
    {
        if (!LocalFileTouch) return false;
        var point = e.GetTouchPoint(Results).Position;
        if (!Results.TouchesCaptured.Any()) _fileTouches.Reset();
        if (!_fileTouches.Active)
        {
            _fileTouchItem = (ItemsControl.ContainerFromElement(Results, Results.InputHitTest(point) as DependencyObject) as ListBoxItem)?.Content as ILauncherItem;
            _fileHoldMoved = false; _fileHoldPoint = point; _fileHoldScreen = Results.PointToScreen(point);
            _fileHold.Stop(); if (_fileTouchItem is FileItem) _fileHold.Start();
        }
        else { _doubleTap.Reset(); _fileHoldMoved = true; _fileHold.Stop(); }
        _fileTouches.Down(e.TouchDevice.Id, point.X, point.Y, Environment.TickCount64, _settings.Gestures.TapDistance);
        e.TouchDevice.Capture(Results); e.Handled = true; return true;
    }
    private bool FileResultsTouchMove(TouchEventArgs e)
    {
        if (!LocalFileTouch || !_fileTouches.Active) return false;
        var point = e.GetTouchPoint(Results).Position;
        if ((point - _fileHoldPoint).Length > 12) { _fileHoldMoved = true; _fileHold.Stop(); }
        var delta = _fileTouches.Move(e.TouchDevice.Id, point.X, point.Y);
        if (delta != 0 && FindScrollViewer(Results) is { } scroll) { scroll.ScrollToVerticalOffset(scroll.VerticalOffset - delta); _doubleTap.Reset(); }
        e.Handled = true; return true;
    }
    private bool FileResultsTouchUp(TouchEventArgs e)
    {
        if (!LocalFileTouch || !_fileTouches.Active) return false;
        var point = e.GetTouchPoint(Results).Position;
        _fileHold.Stop();
        var result = _fileTouches.Up(e.TouchDevice.Id, point.X, point.Y, Environment.TickCount64);
        e.TouchDevice.Capture(null); e.Handled = true;
        var item = _fileTouchItem;
        if (!_fileTouches.Active) _fileTouchItem = null;
        if (item is null || !Results.Items.Contains(item)) { _doubleTap.Reset(); return true; }
        if (result.Action == FileTouchAction.ContextMenu && _settings.Gestures.TwoFingerRightClick)
        {
            _doubleTap.Reset(); Results.SelectedItem = item;
            var screen = Results.PointToScreen(new Point(result.X, result.Y));
            Dispatcher.BeginInvoke(() => { if (IsVisible && _fileMode && Results.Items.Contains(item)) ShowShellMenu(item, screen); });
        }
        else if (result.Action == FileTouchAction.Tap)
        {
            Results.SelectedItem = item;
            if (_doubleTap.Tap(item.Path, Environment.TickCount64, point.X, point.Y, System.Windows.Forms.SystemInformation.DoubleClickTime)) LaunchSelected();
        }
        else _doubleTap.Reset();
        return true;
    }
    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer scroll) return scroll;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(parent, i)) is { } found) return found;
        return null;
    }
}
