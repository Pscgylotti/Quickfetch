using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Quickfetch.Core;
using Forms = System.Windows.Forms;

namespace Quickfetch;

public partial class MainWindow : Window
{
    private readonly Settings _settings = Settings.Load();
    private IReadOnlyList<ShortcutEntry> _entries = Array.Empty<ShortcutEntry>();
    private readonly ShortcutIconCache _iconCache = new();
    private IReadOnlyDictionary<string, System.Windows.Media.ImageSource?> _icons = new Dictionary<string, System.Windows.Media.ImageSource?>();
    private GlobalShortcut? _shortcut;
    private Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly ShortcutIndex _index = new();
    private readonly ShortcutDiskCache _diskCache = new();
    private readonly System.Threading.SemaphoreSlim _catalogGate = new(1, 1);
    private readonly HashSet<string> _shortcutChanges = new(StringComparer.OrdinalIgnoreCase);
    private bool _fullScanRequested, _cacheRestored;
    private string _watcherConfiguration = "";
    private bool _initialized, _choosingFolder, _disposed, _loading;
    private long _loadVersion;
    private bool _backdropPending;
    private bool _touchInput;
    private readonly DoubleTapTracker _doubleTap = new();
    private int? _listTouchId;
    private Point _listTouchStart;
    private ILauncherItem? _listTouchItem;
    private bool _listTouchMoved;
    private readonly ScaleTransform _openScale = new(1, 1);
    public string BackdropMode { get; private set; } = "Pending";
    public MainWindow()
    {
        InitializeComponent();
        InitializeModes();
        InitializeFileColumns();
        LauncherSurface.RenderTransform = _openScale;
        _initialized = true;
        FolderLabel.Text = _settings.Folder;
        FolderLabel.ToolTip = _settings.Folder;
        RecursiveCheck.IsChecked = _settings.IncludeSubfolders;
        AutostartCheck.IsChecked = Settings.AutoStart;
        BuiltInKeyboardCheck.IsChecked = _settings.UseBuiltInKeyboard;
        QuickKeyboard.KeyPressed += KeyboardKeyPressed;
        Loaded += (_, _) => ScheduleBackdrop();
        Activated += (_, _) => ScheduleBackdrop();
        SizeChanged += (_, _) => ScheduleBackdrop();
        _refreshTimer.Tick += async (_, _) =>
        {
            _refreshTimer.Stop();
            var paths = _shortcutChanges.ToArray(); _shortcutChanges.Clear();
            var full = _fullScanRequested; _fullScanRequested = false;
            await RefreshCatalog(false, full ? null : paths);
        };
    }

    public void InitializeLauncher(bool background)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开 Quickfetch", null, (_, _) => Dispatcher.BeginInvoke(ShowLauncher));
        menu.Items.Add("快捷方式文件夹", null, (_, _) => Dispatcher.BeginInvoke(OpenShortcutFolder));
        menu.Items.Add("刷新快捷方式", null, (_, _) => Dispatcher.BeginInvoke(async () => await RefreshCatalog(true)));
        menu.Items.Add("触屏手势设置", null, (_, _) => Dispatcher.BeginInvoke(OpenGestureSettings));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.BeginInvoke(() => ((App)Application.Current).ExitFromTray()));
        using var personalize = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var darkTray = personalize?.GetValue("SystemUsesLightTheme") is int light && light == 0;
        _trayIcon = TrayIconFactory.Create(darkTray);
        _tray = new Forms.NotifyIcon { Text = "Quickfetch · Win + S", Icon = _trayIcon, Visible = true, ContextMenuStrip = menu };
        _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Dispatcher.BeginInvoke(ShowLauncher); };
        _tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(ShowLauncher);
        try { _shortcut = new GlobalShortcut(() => Dispatcher.BeginInvoke(ShowLauncher)); }
        catch (Exception ex) { ShowLauncher(); SetMessage(ex.Message); }
        _ = RefreshCatalog();
        ConfigureGestures();
        if (!background) ShowLauncher();
    }

    public void ShowLauncher()
    {
        if (_disposed || _draggingFile) return;
        if (!IsVisible) SettingsPanel.Visibility = Visibility.Collapsed;
        SetTouchKeyboard(TabletPosture.IsTablet);
        if (!IsVisible)
        {
            Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2;
            Top = SystemParameters.WorkArea.Top + (SystemParameters.WorkArea.Height - Height) / 2;
            PrepareOpenAnimation();
            Show();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, PlayOpenAnimation);
        }
        WindowState = WindowState.Normal;
        Activate();
        SetForegroundWindow(new WindowInteropHelper(this).Handle);
        Query.Text = "";
        UpdateResults();
        ScheduleBackdrop();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { FocusSearch(); Query.SelectAll(); });
    }
    private void PrepareOpenAnimation()
    {
        LauncherSurface.BeginAnimation(UIElement.OpacityProperty, null);
        _openScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _openScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        LauncherSurface.Opacity = 0;
        _openScale.ScaleX = _openScale.ScaleY = 0.92;
    }
    private void PlayOpenAnimation()
    {
        // Animate WPF content, keeping the native Acrylic HWND and its opacity intact.
        LauncherSurface.Opacity = 1;
        _openScale.ScaleX = _openScale.ScaleY = 1;
        if (!IsVisible || _disposed) return;
        var scale = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(180))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
        _openScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        _openScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        LauncherSurface.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)) { FillBehavior = FillBehavior.Stop });
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowMessages);
        BackdropMode = WindowBackdrop.Apply(handle);
        ScheduleBackdrop();
    }
    private void ScheduleBackdrop()
    {
        if (_disposed || _modeTransitionActive || _backdropPending || new WindowInteropHelper(this).Handle == IntPtr.Zero) return;
        _backdropPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _backdropPending = false;
            if (!_disposed && !_modeTransitionActive) BackdropMode = WindowBackdrop.Apply(new WindowInteropHelper(this).Handle);
        });
    }
    private IntPtr WindowMessages(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_everything?.HandleMessage(message, lParam) == true) { handled = true; return new IntPtr(1); }
        if (message is 0x31A or 0x31E) ScheduleBackdrop();
        if (message == 0x1A)
        {
            var setting = lParam == IntPtr.Zero ? "" : Marshal.PtrToStringUni(lParam);
            if (IsVisible && setting is "ConvertibleSlateMode" or "SystemDockMode" or "UserInteractionMode")
                Dispatcher.BeginInvoke(() => SetTouchKeyboard(TabletPosture.IsTablet));
            ScheduleBackdrop();
        }
        return IntPtr.Zero;
    }

    private async Task RefreshCatalog(bool forceIcons = false, string[]? changes = null)
    {
        await _catalogGate.WaitAsync();
        if (_disposed) { _catalogGate.Release(); return; }
        var version = ++_loadVersion;
        var roots = _settings.ShortcutRoots;
        var recursive = _settings.IncludeSubfolders;
        _loading = true;
        try
        {
            ConfigureWatchers(roots, recursive);
            var entries = await Task.Run(() =>
            {
                _index.Configure(roots, recursive);
                if (!_cacheRestored)
                {
                    _cacheRestored = true;
                    var cached = _diskCache.Load(roots, recursive);
                    if (cached is not null) { _index.Restore(cached.Items.Select(item => item.Metadata)); _iconCache.Restore(cached.Items); }
                }
                if (changes is null) _index.FullScan(); else _index.Apply(changes);
                return _index.Items.Values.Select(item => item.Entry).OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            });
            if (_disposed || version != _loadVersion) return;
            var icons = await _iconCache.LoadAsync(entries, forceIcons, _index.Items);
            if (_disposed || version != _loadVersion) return;
            _icons = icons;
            _entries = entries;
            _loading = false;
            if (IsVisible && !_fileMode) UpdateResults();
            try { await Task.Run(() => _diskCache.Save(roots, recursive, _index.Items, _iconCache)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* In-memory operation remains available when cache storage is read-only. */ }
        }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex))
        {
            if (_disposed || version != _loadVersion) return;
            AppDiagnostics.Current.Fault("shortcut-refresh", ex);
            _loading = false;
            _entries = Array.Empty<ShortcutEntry>();
            _icons = new Dictionary<string, System.Windows.Media.ImageSource?>();
            UpdateResults();
            SetMessage(ex.Message);
        }
        finally { _catalogGate.Release(); }
    }

    private void ConfigureWatchers(string[] roots, bool recursive)
    {
        var key = string.Join("|", roots) + "|" + recursive;
        if (key == _watcherConfiguration) return;
        foreach (var watcher in _watchers) watcher.Dispose(); _watchers.Clear();
        foreach (var root in roots.Where(Directory.Exists))
        {
            var watcher = new FileSystemWatcher(root, "*") { IncludeSubdirectories = recursive, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
            FileSystemEventHandler changed = (_, e) => ScheduleRefresh(e.FullPath);
            watcher.Created += changed; watcher.Deleted += changed; watcher.Changed += changed;
            watcher.Renamed += (_, e) => { ScheduleRefresh(e.OldFullPath); ScheduleRefresh(e.FullPath); };
            watcher.Error += (_, _) => ScheduleRefresh(null);
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        _watcherConfiguration = key;
    }
    private void ScheduleRefresh(string? path)
    {
        if (_disposed) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            if (path is null) _fullScanRequested = true;
            else
            {
                var prefix = Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar;
                if (!Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) && !_entries.Any(entry => entry.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) && !Directory.Exists(path)) return;
                _shortcutChanges.Add(path);
            }
            _refreshTimer.Stop(); _refreshTimer.Start();
        });
    }

    private void Query_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized) return;
        var clean = _fileMode ? Query.Text.Replace("\0", "") : Regex.Replace(Query.Text, "[^A-Za-z0-9]", "");
        if (clean != Query.Text) { var caret = Query.CaretIndex; Query.Text = clean; Query.CaretIndex = Math.Min(caret, clean.Length); return; }
        UpdateResults();
    }
    private void UpdateResults()
    {
        _doubleTap.Reset();
        _listTouchItem = null;
        _listTouchId = null;
        ScheduleCaret();
        Placeholder.Visibility = Query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_fileMode) { QueueFileSearch(); return; }
        MoreResults.Visibility = Visibility.Collapsed;
        var matches = UsageRanking.Rank(SearchMatcher.Search(_entries, Query.Text), _usage.History, DateTimeOffset.UtcNow);
        var items = matches.Select(match => new ShortcutItem(match, _icons.GetValueOrDefault(match.Path))).ToArray();
        Results.ItemsSource = items;
        Results.SelectedIndex = matches.Count > 0 ? 0 : -1;
        if (items.Length > 0) Results.ScrollIntoView(items[0]);
        EmptyMessage.Text = _loading ? "正在读取快捷方式…" : _entries.Count == 0 ? "此文件夹还没有可检索的 .lnk 快捷方式。\n支持中文、英文或数字名称，点击设置可选择文件夹。" : "没有匹配的快捷方式";
        EmptyMessage.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Status.Text = Query.Text.Length == 0 ? $"{_entries.Count} 个快捷方式" : $"{matches.Count} / {_entries.Count} 个匹配";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift) { SwitchMode(); e.Handled = true; return; }
        if (_touchInput && e.Key != Key.Escape) { SetTouchKeyboard(false, physicalKeyboard: true); FocusSearch(); }
        if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
        else if (e.Key is Key.Down or Key.Up)
        {
            if (Results.Items.Count > 0)
            {
                var next = Results.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
                Results.SelectedIndex = Math.Clamp(next, 0, Results.Items.Count - 1);
                Results.ScrollIntoView(Results.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter) { LaunchSelected((Keyboard.Modifiers & ModifierKeys.Shift) != 0); e.Handled = true; }
        else if (e.Key == Key.Apps || (e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift))
        { if (Results.SelectedItem is ILauncherItem item) ShowShellMenu(item, Results.PointToScreen(new Point(24, 24))); e.Handled = true; }
        else if (e.Key == Key.F5) { if (_fileMode) QueueFileSearch(); else _ = RefreshCatalog(true); e.Handled = true; }
    }

    private void SetTouchKeyboard(bool visible, bool physicalKeyboard = false)
    {
        StopModeTransition();
        // ReadOnly alone still allows WPF/Windows keyboard invocation on edit focus.
        // In touch mode keep focus on a non-edit surface; only our keys edit the query.
        _touchInput = _settings.UseBuiltInKeyboard && !physicalKeyboard && (visible || TabletPosture.IsTablet);
        Query.IsReadOnly = _touchInput;
        Query.Focusable = !_touchInput;
        Query.IsTabStop = !_touchInput;
        InputMethod.SetIsInputMethodEnabled(Query, _fileMode && !_touchInput);
        if (_touchInput)
        {
            FocusManager.SetFocusedElement(this, LauncherSurface);
            if (IsVisible) Keyboard.Focus(LauncherSurface);
        }
        var builtInVisible = visible && _settings.UseBuiltInKeyboard;
        QuickKeyboard.Visibility = builtInVisible ? Visibility.Visible : Visibility.Collapsed;
        KeyboardToggle.ToolTip = _settings.UseBuiltInKeyboard ? "展开或收起触屏键盘" : "打开 Windows 系统触摸键盘";
        var center = Left + Width / 2;
        Width = Math.Min(_fileMode ? 784 : 660, SystemParameters.WorkArea.Width - 24);
        if (IsVisible) Left = Math.Clamp(center - Width / 2, SystemParameters.WorkArea.Left + 12, SystemParameters.WorkArea.Right - Width - 12);
        Height = Math.Min(builtInVisible ? 750 : 490, SystemParameters.WorkArea.Height - 24);
        if (IsVisible) Top = Math.Clamp(Top, SystemParameters.WorkArea.Top + 12, SystemParameters.WorkArea.Bottom - Height - 12);
        ScheduleBackdrop();
        ScheduleCaret();
        if (!_settings.UseBuiltInKeyboard && visible && !physicalKeyboard) RequestSystemKeyboard();
    }
    private void FocusSearch()
    {
        if (_touchInput) { FocusManager.SetFocusedElement(this, LauncherSurface); Keyboard.Focus(LauncherSurface); }
        else { Query.Focus(); Keyboard.Focus(Query); }
    }
    private void Keyboard_Click(object sender, RoutedEventArgs e)
    {
        SetTouchKeyboard(!_settings.UseBuiltInKeyboard || QuickKeyboard.Visibility != Visibility.Visible);
        FocusSearch();
    }
    private void FocusTouchQuery(Point point)
    {
        SetTouchKeyboard(true);
        FocusSearch();
        Query.Select(TouchCaretIndex(point), 0);
    }
    private void Query_TouchDown(object sender, TouchEventArgs e)
    {
        if (!_settings.UseBuiltInKeyboard) { SetTouchKeyboard(true); return; }
        FocusTouchQuery(e.GetTouchPoint(Query).Position); e.Handled = true;
    }
    private void Query_StylusDown(object sender, StylusDownEventArgs e)
    {
        if (e.StylusDevice.TabletDevice.Type == TabletDeviceType.Touch)
        {
            if (!_settings.UseBuiltInKeyboard) { SetTouchKeyboard(true); return; }
            FocusTouchQuery(e.GetPosition(Query)); e.Handled = true;
        }
    }
    private void Query_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_settings.UseBuiltInKeyboard)
        {
            SetTouchKeyboard(e.StylusDevice is not null || TabletPosture.IsTablet);
            return;
        }
        if (_touchInput || e.StylusDevice is not null || TabletPosture.IsTablet)
        { FocusTouchQuery(e.GetPosition(Query)); e.Handled = true; }
        else SetTouchKeyboard(false, physicalKeyboard: true);
    }
    private void KeyboardKeyPressed(string key)
    {
        if (key == "hide") SetTouchKeyboard(false);
        else if (key == "directory") { LaunchSelected(true); return; }
        else if (key == "mode") { SwitchMode(); return; }
        else if (key == "enter") { LaunchSelected(); return; }
        else if (key is "up" or "down")
        {
            if (Results.Items.Count > 0)
            {
                Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + (key == "down" ? 1 : -1), 0, Results.Items.Count - 1);
                Results.ScrollIntoView(Results.SelectedItem);
            }
        }
        else
        {
            var fileCharacter = key == "space" ? " " : key;
            var edit = _fileMode && fileCharacter.Length == 1 ? (Query.Text.Remove(Query.SelectionStart, Query.SelectionLength).Insert(Query.SelectionStart, fileCharacter), Query.SelectionStart + 1) : QueryEditor.Apply(Query.Text, Query.SelectionStart, Query.SelectionLength, key);
            Query.Text = edit.Item1;
            Query.Select(edit.Item2, 0);
        }
        FocusSearch();
    }

    private void LaunchSelected(bool openDirectory = false)
    {
        if (Results.SelectedItem is not ILauncherItem item) return;
        try
        {
            if (item is ShortcutItem shortcut && !ShortcutCatalog.IsAllowed(shortcut.Match.Entry, _settings.ShortcutRoots, _settings.IncludeSubfolders))
                throw new IOException("快捷方式已经移动或删除。请按 F5 刷新。");
            if (!File.Exists(item.Path) && !Directory.Exists(item.Path)) throw new IOException("文件已移动或删除，请刷新搜索。");
            if (openDirectory) Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + item.Path + "\"") { UseShellExecute = true });
            else
            {
                Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(item.Path) ?? "" });
                if (item is ShortcutItem) RecordUsage(item.Path);
            }
            Hide();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetMessage("无法启动：" + ex.Message);
            FocusSearch();
        }
    }
    private void SetMessage(string message) { Status.Text = message; Status.ToolTip = message; }
    private void Results_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice is not null) { e.Handled = true; return; }
        if (ItemsControl.ContainerFromElement(Results, e.OriginalSource as DependencyObject) is ListBoxItem) LaunchSelected();
    }
    private void Results_TouchDown(object sender, TouchEventArgs e)
    {
        if (FileResultsTouchDown(e)) return;
        if (_listTouchId is int previous && !Results.TouchesOver.Any(touch => touch.Id == previous)) _listTouchId = null;
        if (_listTouchId is not null) { _listTouchMoved = true; _doubleTap.Reset(); return; }
        _listTouchId = e.TouchDevice.Id;
        _listTouchStart = e.GetTouchPoint(Results).Position;
        _listTouchMoved = false;
        _listTouchItem = (ItemsControl.ContainerFromElement(Results, e.OriginalSource as DependencyObject) as ListBoxItem)?.Content as ILauncherItem;
    }
    private void Results_TouchMove(object sender, TouchEventArgs e)
    {
        if (FileResultsTouchMove(e)) return;
        if (_listTouchId != e.TouchDevice.Id) return;
        if ((e.GetTouchPoint(Results).Position - _listTouchStart).Length > 12) { _listTouchMoved = true; _doubleTap.Reset(); }
    }
    private void Results_TouchUp(object sender, TouchEventArgs e)
    {
        if (FileResultsTouchUp(e)) return;
        if (_listTouchId != e.TouchDevice.Id) return;
        _listTouchId = null;
        var item = _listTouchItem;
        _listTouchItem = null;
        if (_listTouchMoved || item is null || !Results.Items.Contains(item)) { _doubleTap.Reset(); return; }
        var point = e.GetTouchPoint(Results).Position;
        if ((point - _listTouchStart).Length > 12) { _doubleTap.Reset(); return; }
        Results.SelectedItem = item;
        if (_doubleTap.Tap(item.Path, Environment.TickCount64, point.X, point.Y, Forms.SystemInformation.DoubleClickTime)) LaunchSelected();
        e.Handled = true; // Avoid a second launch from promoted mouse double-click messages.
    }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_disposed) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
    private void Window_Deactivated(object? sender, EventArgs e)
    {
        _fileHold.Stop(); _fileTouches.Reset(); _fileTouchItem = null; _columnTouch = null;
        _doubleTap.Reset(); _listTouchId = null; _listTouchItem = null;
        if (!_choosingFolder) Hide();
    }
    private void Header_Drag(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed && e.OriginalSource is TextBlock) DragMove(); }
    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();
    private void OpenFolder_Click(object sender, RoutedEventArgs e) => OpenShortcutFolder();
    private void Settings_Click(object sender, RoutedEventArgs e) { SettingsPanel.Visibility = SettingsPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; FocusSearch(); }
    private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        _choosingFolder = true;
        try
        {
            var picker = new OpenFolderDialog { Title = "选择 .lnk 快捷方式文件夹", InitialDirectory = _settings.Folder };
            if (picker.ShowDialog(this) == true)
            {
                _settings.Folder = picker.FolderName;
                _settings.Save();
                FolderLabel.Text = _settings.Folder; FolderLabel.ToolTip = _settings.Folder;
                await RefreshCatalog();
            }
        }
        catch (Exception ex) { SetMessage(ex.Message); }
        finally { _choosingFolder = false; FocusSearch(); }
    }
    private async void Recursive_Click(object sender, RoutedEventArgs e)
    {
        try { _settings.IncludeSubfolders = RecursiveCheck.IsChecked == true; _settings.Save(); await RefreshCatalog(); }
        catch (Exception ex) { SetMessage(ex.Message); }
        FocusSearch();
    }
    private void Autostart_Click(object sender, RoutedEventArgs e)
    {
        try { Settings.AutoStart = AutostartCheck.IsChecked == true; }
        catch (Exception ex) { SetMessage(ex.Message); AutostartCheck.IsChecked = Settings.AutoStart; }
        FocusSearch();
    }
    private void OpenShortcutFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_settings.Folder}\"") { UseShellExecute = true }); }
        catch (Exception ex) { ShowLauncher(); SetMessage(ex.Message); }
    }
    public void DisposeLauncher()
    {
        if (_disposed) return;
        _disposed = true;
        _fileHold.Stop();
        HideSystemKeyboard();
        ++_gestureGeneration; _gestures?.Dispose(); _gestures = null;
        _fileTimer.Stop(); _caretBlink.Stop(); _fileCancellation?.Cancel(); _everything?.Dispose();
        foreach (var watcher in _watchers) watcher.Dispose(); _watchers.Clear();
        _refreshTimer.Stop(); _shortcut?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        _trayIcon?.Dispose();
    }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
}
