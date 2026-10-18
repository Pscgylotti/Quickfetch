using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Quickfetch.Core;

namespace Quickfetch;

public partial class MainWindow
{
    private readonly UsageStore _usage = new();
    private EverythingClient? _everything;
    private bool _fileMode;
    private DataTemplate _appTemplate = null!;
    private readonly DispatcherTimer _fileTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private CancellationTokenSource? _fileCancellation;
    private long _fileVersion;
    private uint _fileSort = 1, _fileLimit = 500;
    private readonly DispatcherTimer _caretBlink = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _caretPending;
    internal Func<bool> EverythingAvailable { get; set; } = () => EverythingClient.IsAvailable;

    private void InitializeModes()
    {
        _fileSort = _settings.FileSort is >= 1 and <= 6 or 13 or 14 ? _settings.FileSort : 1;
        UpdateSortLabels();
        _appTemplate = Results.ItemTemplate;
        _fileTimer.Tick += async (_, _) => { _fileTimer.Stop(); await SearchFiles(); };
        Query.SelectionChanged += (_, _) => ScheduleCaret();
        Query.SizeChanged += (_, _) => ScheduleCaret();
        IsVisibleChanged += (_, _) =>
        {
            ConfigureOutsideClicks();
            if (!IsVisible)
            {
                StopModeTransition();
                _fileHold.Stop();
                HideSystemKeyboard();
                _caretBlink.Stop(); _fileTimer.Stop(); _fileCancellation?.Cancel(); _fileVersion++;
                Results.ItemsSource = Array.Empty<ILauncherItem>(); _fileLimit = 500;
            }
            else ScheduleCaret();
        };
        _caretBlink.Tick += (_, _) => TouchCaret.Opacity = TouchCaret.Opacity == 0 ? 1 : 0;
        Loaded += (_, _) => ScheduleCaret();
    }
    private void Mode_Click(object sender, RoutedEventArgs e) => SwitchMode();
    private void SwitchMode()
    {
        _openScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        _openScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        _openScale.ScaleX = _openScale.ScaleY = 1;
        LauncherSurface.BeginAnimation(OpacityProperty, null); LauncherSurface.Opacity = 1;
        _fileHold.Stop(); _fileTouches.Reset(); _fileTouchItem = null;
        var transferredQuery = _fileMode ? "" : Query.Text;
        _fileMode = !_fileMode;
        _fileVersion++;
        _fileCancellation?.Cancel(); _fileTimer.Stop();
        FileSortHeader.Visibility = _fileMode ? Visibility.Visible : Visibility.Collapsed;
        InstallEverythingButton.Visibility = Visibility.Collapsed;
        Results.ItemTemplate = _fileMode ? (DataTemplate)FindResource("FileItemTemplate") : _appTemplate;
        ModeLabel.Text = _fileMode ? "文件 · EVERYTHING" : "QUICKFETCH";
        ModeButton.ToolTip = _fileMode ? "切换到应用检索 · Tab" : "搜索文件 · Tab";
        ModeButton.SetResourceReference(BackgroundProperty, _fileMode ? "Selection" : "Surface");
        Placeholder.Text = _fileMode ? "搜索文件名、路径或 Everything 语法" : "输入英文、数字或拼音，快速启动";
        ResultHint.Text = _fileMode ? "Enter 打开    Shift + Enter 所在目录    Tab 应用" : "↑ ↓ 选择    Enter 启动    Tab 文件";
        QuickKeyboard.SetFileMode(_fileMode);
        SetTouchKeyboard(QuickKeyboard.Visibility == Visibility.Visible, physicalKeyboard: !_touchInput, animateModeBounds: true);
        Query.Text = transferredQuery;
        UpdateResults();
        FocusSearch(); Query.SelectAll();
        AnimateModeTransition();
    }
    private void QueueFileSearch(bool resetLimit = true)
    {
        _fileVersion++;
        _fileCancellation?.Cancel(); _fileTimer.Stop();
        if (resetLimit) _fileLimit = 500;
        MoreResults.Visibility = Visibility.Collapsed;
        if (!RefreshEverythingAvailability()) return;
        if (string.IsNullOrWhiteSpace(Query.Text))
        {
            Results.ItemsSource = Array.Empty<FileItem>();
            EmptyMessage.Text = "输入文件名或路径，直接检索 Everything 索引。\n可使用空格、中文及 Everything 搜索语法。";
            EmptyMessage.Visibility = Visibility.Visible;
            Status.Text = "文件搜索 · Everything";
            return;
        }
        // Clear stale results so Enter cannot accidentally open an old query's file.
        Results.ItemsSource = Array.Empty<FileItem>();
        EmptyMessage.Text = "正在检索 Everything…"; EmptyMessage.Visibility = Visibility.Visible;
        Status.Text = "正在搜索…";
        _fileTimer.Start();
    }
    private async Task SearchFiles()
    {
        var version = _fileVersion;
        _fileCancellation?.Dispose();
        var cancellation = _fileCancellation = new CancellationTokenSource();
        try
        {
            _everything ??= new EverythingClient(new WindowInteropHelper(this).EnsureHandle());
            var page = await _everything.SearchAsync(Query.Text, _fileSort, _fileLimit, cancellation.Token);
            if (_disposed || !_fileMode || version != _fileVersion) return;
            var items = page.Entries.Select(file => new FileItem(file)).ToArray();
            _fileSort = page.Sort;
            Results.ItemsSource = items; Results.SelectedIndex = items.Length > 0 ? 0 : -1;
            if (items.Length > 0) Results.ScrollIntoView(items[0]);
            EmptyMessage.Text = "没有找到匹配的文件";
            EmptyMessage.Visibility = items.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Status.Text = $"{items.Length:N0} / {page.Total:N0} 个结果" + (_everything.PinyinApplied ? " · 含拼音匹配" : "");
            MoreResults.Visibility = items.Length < page.Total ? Visibility.Visible : Visibility.Collapsed;
            UpdateSortLabels();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex))
        {
            if (_disposed || !_fileMode || version != _fileVersion) return;
            AppDiagnostics.Current.Fault("file-search", ex);
            Results.ItemsSource = Array.Empty<FileItem>(); EmptyMessage.Text = ex.Message; EmptyMessage.Visibility = Visibility.Visible;
            Status.Text = "Everything 未就绪";
            if (!EverythingClient.IsAvailable) RefreshEverythingAvailability();
        }
    }
    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        var column = uint.Parse((string)((Button)sender).Tag);
        _fileSort = (_fileSort - 1) / 2 == column ? (_fileSort % 2 == 0 ? _fileSort - 1 : _fileSort + 1) : column * 2 + (column is 2 or 6 ? 2u : 1u);
        _settings.FileSort = _fileSort;
        try { _saveSettings(_settings); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { AppDiagnostics.Current.Fault("save-file-sort", ex); }
        UpdateSortLabels(); QueueFileSearch(); FocusSearch();
    }
    private void UpdateSortLabels()
    {
        foreach (var (button, column, label) in new[] { (NameSort, 0u, "名称"), (PathSort, 1u, "路径"), (SizeSort, 2u, "大小"), (DateSort, 6u, "修改日期") })
            button.Content = label + ((_fileSort - 1) / 2 == column ? (_fileSort % 2 == 1 ? " ↑" : " ↓") : "");
    }
    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (_fileLimit >= 5000) { SetMessage("已显示 5,000 项，请输入更精确的文件名或路径。"); return; }
        _fileLimit += 500; QueueFileSearch(false); FocusSearch();
    }
    private bool RefreshEverythingAvailability()
    {
        var available = EverythingAvailable();
        InstallEverythingButton.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
        FileSortHeader.IsEnabled = available;
        if (!available)
        {
            Results.ItemsSource = Array.Empty<FileItem>();
            EmptyMessage.Text = "文件搜索不可用：未检测到运行中的 Everything。\n请安装或启动 Everything，然后按 F5 重试。";
            EmptyMessage.Visibility = Visibility.Visible;
            Status.Text = "文件搜索不可用";
        }
        return available;
    }
    private void InstallEverything_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.voidtools.com/downloads/") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception ex) { SetMessage("无法打开下载页面：" + ex.Message); }
    }
    private void RecordUsage(string path)
    {
        try { _usage.Record(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetMessage("已启动；使用记录未能保存：" + ex.Message); }
    }
    private void ShowShellMenu(ILauncherItem item, Point point)
    {
        if (_fileMode && item is FileItem) { ShowFileFlyout(item, point); return; }
        _choosingFolder = true;
        try
        {
            var result = ShellContextMenu.Show(new WindowInteropHelper(this).Handle, item.Path, (int)point.X, (int)point.Y);
            if (result.Invoked)
            {
                if (item is ShortcutItem && result.Verb is "open" or "runas") RecordUsage(item.Path);
                Hide();
            }
            else FocusSearch();
        }
        catch (Exception ex) when (ex is COMException or IOException or System.ComponentModel.Win32Exception) { SetMessage("无法显示菜单：" + ex.Message); }
        finally { _choosingFolder = false; }
    }
    private void Results_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(Results, e.OriginalSource as DependencyObject) is not ListBoxItem row || row.Content is not ILauncherItem item) return;
        Results.SelectedItem = item; e.Handled = true;
        ShowShellMenu(item, Results.PointToScreen(e.GetPosition(Results)));
    }
    private void ScheduleCaret()
    {
        if (_caretPending || _disposed) return;
        _caretPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _caretPending = false;
            var visible = _touchInput && IsVisible;
            TouchCaret.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible) { _caretBlink.Stop(); return; }
            var rect = Query.GetRectFromCharacterIndex(Query.SelectionStart + Query.SelectionLength);
            if (rect.IsEmpty) { _caretBlink.Stop(); return; }
            Canvas.SetLeft(TouchCaretLine, Math.Max(0, rect.X));
            Canvas.SetTop(TouchCaretLine, rect.Y);
            TouchCaretLine.Y2 = Math.Max(18, rect.Height);
            TouchCaret.Opacity = 1;
            _caretBlink.Stop(); _caretBlink.Start();
        });
    }
}
