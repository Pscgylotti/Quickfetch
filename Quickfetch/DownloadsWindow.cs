using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using Quickfetch.Core;

namespace Quickfetch;

internal sealed class DownloadView : INotifyPropertyChanged
{
    public DownloadJob Job { get; private set; }
    public string Name => Job.Name;
    public string ActionLabel => Job.Status is DownloadStatus.Queued or DownloadStatus.Running ? Loc.T("暂停") : Loc.T("继续");
    public bool CanToggle => Job.Status is DownloadStatus.Queued or DownloadStatus.Running or DownloadStatus.Paused or DownloadStatus.Failed;
    public bool IsCompleted => Job.Status == DownloadStatus.Completed;
    public Visibility CompletedActionsVisibility => IsCompleted ? Visibility.Visible : Visibility.Collapsed;
    public string Details => (Job.Status switch { DownloadStatus.Queued => Loc.T("排队中"), DownloadStatus.Running => Loc.T("下载中"), DownloadStatus.Paused => Loc.T("已暂停"), DownloadStatus.Completed => Loc.T("已完成"), DownloadStatus.Failed => Loc.T("失败"), _ => Loc.T("已取消") })
        + " · " + FileEntry.FormatSize(Job.Downloaded) + (Job.Size is long size ? " / " + FileEntry.FormatSize(size) + $" · {(size == 0 ? 100 : Job.Downloaded * 100d / size):0.0}%" : Loc.T(" / 大小未知"))
        + (Job.Status == DownloadStatus.Running ? " · " + FileEntry.FormatSize((long)Math.Max(0, _speed)) + "/s" : "")
        + (Job.StreamKind.Length > 0 ? " · HLS " + Job.Parts.Count(p => p.Done) + "/" + Job.Parts.Count + Loc.T(" 分片") : Job.Ranged ? " · " + Job.WorkerCount + " worker / " + Job.Ranges.Count + Loc.T(" 段") : Loc.T(" · 单路（服务器不支持分段）"))
        + (Job.Phase.Length > 0 ? " · " + Job.Phase : "");
    public string Error => Loc.T(Job.Error);
    private DateTimeOffset _time = DateTimeOffset.UtcNow;
    private long _bytes;
    private double _speed;
    public DownloadView(DownloadJob job) { Job = job; _bytes = job.Downloaded; }
    public void Update(DownloadJob job)
    {
        var now = DateTimeOffset.UtcNow;
        if (job.Status == DownloadStatus.Running && Job.Status == DownloadStatus.Running) _speed = Math.Max(0, job.Downloaded - _bytes) / Math.Max(.1, (now - _time).TotalSeconds);
        else _speed = 0;
        _bytes = job.Downloaded; _time = now; Job = job;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class DownloadMap : FrameworkElement
{
    public static readonly DependencyProperty JobProperty = DependencyProperty.Register(nameof(Job), typeof(DownloadJob), typeof(DownloadMap), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public DownloadJob? Job { get => (DownloadJob?)GetValue(JobProperty); set => SetValue(JobProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        if (Job is not { } job || ActualWidth < 1) return;
        var width = ActualWidth; var size = Math.Max(1, job.StreamKind.Length > 0 ? job.Parts.Count : job.Size ?? Math.Max(1, job.Downloaded));
        var muted = TryFindResource("Muted") as Brush ?? Brushes.Gray;
        var accent = TryFindResource("Accent") as Brush ?? Brushes.CornflowerBlue;
        var ink = TryFindResource("Ink") as Brush ?? Brushes.White;
        const double height = 16;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height), 6, 6));
        dc.DrawRectangle(TryFindResource("Line") as Brush ?? Brushes.DarkGray, null, new Rect(0, 0, width, height));
        foreach (var range in job.Ranges)
        {
            var x = Math.Clamp(range.Start * width / size, 0, width);
            var end = Math.Clamp(range.Next * width / size, 0, width);
            dc.DrawRectangle(accent, null, new Rect(x, 0, Math.Max(0, end - x), height));
            dc.DrawLine(new Pen(muted, .5), new Point(x, 0), new Point(x, height));
        }
        if (job.StreamKind.Length > 0)
        {
            for (int i = 0; i < job.Parts.Count; i++) if (job.Parts[i].Done) dc.DrawRectangle(accent, null, new Rect(i * width / size, 0, width / size, height));
        }
        else if (!job.Ranged) dc.DrawRectangle(accent, null, new Rect(0, 0, Math.Min(width, job.Downloaded * width / size), height));
        dc.Pop();
        var count = job.StreamKind.Length > 0 ? 8 : job.Ranged ? job.WorkerCount : 1;
        for (int i = 0; i < count; i++)
        {
            var x = Math.Clamp(job.Pointers[i] * width / size, 0, Math.Max(0, width - 1));
            dc.DrawLine(new Pen(ink, 1.5), new Point(x, 1), new Point(x, height - 1));
            var label = new FormattedText((i + 1).ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, muted, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(label, new Point(Math.Clamp(x - label.Width / 2, 0, Math.Max(0, width - label.Width)), height + 4));
        }
    }
}

internal sealed class DownloadsWindow : Window
{
    private readonly DownloadManager _manager;
    private readonly Settings _settings;
    private readonly Action<Settings> _save;
    private readonly ObservableCollection<DownloadView> _items = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    internal DownloadsWindow(DownloadManager manager, Settings settings, Action<Settings> save)
    {
        _manager = manager; _settings = settings; _save = save;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Quickfetch;component/DownloadFields.xaml", UriKind.Relative) });
        SetResourceReference(ForegroundProperty, "Ink");
        Title = Loc.T("Quickfetch · 下载"); Width = Math.Min(820, SystemParameters.WorkArea.Width - 24); Height = Math.Min(680, SystemParameters.WorkArea.Height - 24);
        WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Background = Brushes.Transparent;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0), CornerRadius = new CornerRadius(10) });
        var root = new Grid { Margin = new Thickness(18) };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) root.RowDefinitions.Add(new RowDefinition { Height = height });
        var border = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = root }; border.SetResourceReference(Border.BackgroundProperty, "Surface"); border.SetResourceReference(Border.BorderBrushProperty, "Line"); Content = border;
        var header = new DockPanel(); var close = Button(Loc.T("关闭"), (_, _) => Close()); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        header.Children.Add(new TextBlock { Text = Loc.T("下载队列与历史"), FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }); root.Children.Add(header);
        var controls = new StackPanel { Margin = new Thickness(0, 10, 0, 10) }; Grid.SetRow(controls, 1); root.Children.Add(controls);
        var addRow = new DockPanel(); var add = Button(Loc.T("加入队列"), async (_, _) =>
        {
            if (addRow.Children.OfType<TextBox>().FirstOrDefault() is not { } input) return;
            try { _message.Text = Loc.T("正在检查链接…"); var probe = await _manager.ProbeAsync(input.Text); _manager.Enqueue(probe, new(input.Text)); input.Clear(); _message.Text = Loc.T("已加入后台队列。"); Refresh(); }
            catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { _message.Text = ex.Message; }
        }); DockPanel.SetDock(add, Dock.Right); addRow.Children.Add(add);
        addRow.Children.Add(new TextBox { MinHeight = 38, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = Loc.T("HTTP / HTTPS 文件链接"), Margin = new Thickness(0, 0, 8, 0) }); controls.Children.Add(addRow);
        var settingsPanel = new StackPanel(); var expander = new Expander { Header = Loc.T("下载位置、代理与浏览器扩展"), Content = settingsPanel, Margin = new Thickness(0, 8, 0, 0) }; controls.Children.Add(expander);
        var folder = new TextBox { Text = settings.Downloads.Folder, MinHeight = 34, VerticalContentAlignment = VerticalAlignment.Center };
        var folderRow = new DockPanel(); var choose = Button(Loc.T("选择目录"), (_, _) => { var picker = new OpenFolderDialog { InitialDirectory = folder.Text }; if (picker.ShowDialog(this) == true) folder.Text = picker.FolderName; }); DockPanel.SetDock(choose, Dock.Right); folderRow.Children.Add(choose); folderRow.Children.Add(folder); settingsPanel.Children.Add(folderRow);
        var proxyRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) }; var proxy = new ComboBox { Width = 160, MinHeight = 34, ItemsSource = new[] { Loc.T("系统代理"), Loc.T("不使用代理"), Loc.T("自定义代理") }, SelectedIndex = settings.Downloads.ProxyMode switch { "none" => 1, "custom" => 2, _ => 0 } }; DockPanel.SetDock(proxy, Dock.Left); proxyRow.Children.Add(proxy);
        var address = new TextBox { Text = settings.Downloads.ProxyAddress, MinHeight = 34, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = Loc.T("例如 http://127.0.0.1:7890 或 socks5://127.0.0.1:1080") }; proxyRow.Children.Add(address); settingsPanel.Children.Add(proxyRow);
        var ffmpeg = new TextBox { Text = settings.Downloads.FFmpegPath, MinHeight = 34, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = Loc.T("FFmpeg.exe 路径；留空时从 PATH 查找。HLS 合并需要 FFmpeg。"), Margin = new Thickness(0, 6, 0, 0) }; settingsPanel.Children.Add(new TextBlock { Text = Loc.T("FFmpeg 路径（可留空）"), FontSize = 11 }); settingsPanel.Children.Add(ffmpeg);
        var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        actions.Children.Add(Button(Loc.T("保存配置"), (_, _) =>
        {
            try
            {
                var full = Path.GetFullPath(folder.Text); Directory.CreateDirectory(full);
                if (proxy.SelectedIndex == 2 && (!Uri.TryCreate(address.Text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "socks5") || uri.UserInfo.Length > 0)) throw new IOException(Loc.T("代理地址无效。"));
                var previous = settings.Downloads;
                settings.Downloads = new DownloadOptions { Folder = full, ProxyMode = proxy.SelectedIndex switch { 1 => "none", 2 => "custom", _ => "system" }, ProxyAddress = address.Text.Trim(), FFmpegPath = ffmpeg.Text.Trim().Trim('"') };
                try { save(settings); } catch { settings.Downloads = previous; throw; }
                _message.Text = Loc.T("已保存。目录用于新任务，代理在下一次任务启动或恢复时生效。");
            }
            catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { _message.Text = ex.Message; }
        }));
        actions.Children.Add(Button(Loc.T("安装浏览器桥接 / 打开扩展目录"), (_, _) =>
        {
            try { var path = DownloadBridge.Install(); Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true }); _message.Text = Loc.T("三种浏览器桥接已注册。Edge / Chrome：在扩展管理页加载对应目录。Firefox：about:debugging 临时加载 firefox/manifest.json（正式安装需 Mozilla 签名）。扩展 ID：") + DownloadBridge.ExtensionId; }
            catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { _message.Text = ex.Message; }
        })); settingsPanel.Children.Add(actions);
        _message.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); Grid.SetRow(_message, 2); root.Children.Add(_message);
        var list = new ListBox { ItemsSource = _items, Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled); ScrollViewer.SetPanningMode(list, PanningMode.VerticalOnly); VirtualizingPanel.SetScrollUnit(list, ScrollUnit.Pixel);
        list.ItemTemplate = (DataTemplate)XamlReader.Parse("""
          <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:q="clr-namespace:Quickfetch;assembly=Quickfetch">
            <Border Background="{DynamicResource Panel}" CornerRadius="8" Padding="12" Margin="0,4">
              <StackPanel>
                <TextBlock Text="{Binding Name}" FontSize="15" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
                <TextBlock Text="{Binding Details}" Foreground="{DynamicResource Muted}" Margin="0,6,0,8" TextWrapping="Wrap"/>
                <q:DownloadMap Job="{Binding Job}" Height="36"/>
                <TextBlock Text="{Binding Error}" Foreground="{DynamicResource Muted}" TextWrapping="Wrap"/>
                <WrapPanel Margin="0,6,0,0">
                  <Button Content="{Binding ActionLabel}" IsEnabled="{Binding CanToggle}" Tag="toggle" MinHeight="40" MinWidth="68" Margin="0,0,8,0"/>
                  <Button Content="{q:Tr T2cd0f3be8738}" Tag="cancel" MinHeight="40" MinWidth="68" Margin="0,0,8,0"/>
                  <Button Content="{q:Tr T7d0d5850515d}" Tag="folder" MinHeight="40" MinWidth="90"/>
                  <Button Content="{q:Tr Tc771248e511f}" Tag="open" IsEnabled="{Binding IsCompleted}" MinHeight="40" MinWidth="68" Margin="8,0,0,0"/>
                  <Button Content="{q:Tr T88f47860e77d}" Tag="delete" IsEnabled="{Binding IsCompleted}" MinHeight="40" MinWidth="90" Margin="8,0,0,0"/>
                  <Button Content="{q:Tr T081ed577f0c2}" Tag="remove" Visibility="{Binding CompletedActionsVisibility}" ToolTip="{q:Tr T64d506a2ca11}" MinHeight="40" MinWidth="90" Margin="8,0,0,0"/>
                </WrapPanel>
              </StackPanel>
            </Border>
          </DataTemplate>
          """);
        list.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((sender, e) =>
        {
            if (e.OriginalSource is not Button button || button.DataContext is not DownloadView view) return;
            try
            {
                if ((string)button.Tag == "toggle") { if (view.Job.Status is DownloadStatus.Running or DownloadStatus.Queued) manager.Pause(view.Job.Id); else manager.Resume(view.Job.Id); }
                else if ((string)button.Tag == "cancel") manager.Cancel(view.Job.Id);
                else if ((string)button.Tag == "folder") DownloadFileActions.Folder(view.Job);
                else if ((string)button.Tag == "open") DownloadFileActions.Open(view.Job);
                else if ((string)button.Tag == "delete") DownloadFileActions.Delete(view.Job);
                else if ((string)button.Tag == "remove") manager.RemoveCompleted(view.Job.Id);
                Refresh();
            }
            catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { _message.Text = ex.Message; }
            e.Handled = true;
        })); Grid.SetRow(list, 3); root.Children.Add(list);
        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) }; var clear = Button(Loc.T("清空已结束记录"), (_, _) => { manager.ClearHistory(); Refresh(); }); DockPanel.SetDock(clear, Dock.Right); footer.Children.Add(clear);
        footer.Children.Add(new TextBlock { Text = Loc.T("蓝色：已下载区间 · 1–8：worker 位置\n暂停保留图示，恢复后按恢复顺序排队。"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center }); Grid.SetRow(footer, 4); root.Children.Add(footer);
        _timer.Tick += (_, _) => Refresh(); Loaded += (_, _) => { WindowBackdrop.Apply(new WindowInteropHelper(this).Handle); Refresh(); _timer.Start(); }; Closed += (_, _) => _timer.Stop();
    }
    private static Button Button(string text, RoutedEventHandler click) { var button = new Button { Content = text, MinHeight = 40, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(4, 0, 0, 0) }; button.Click += click; return button; }
    private void Refresh()
    {
        var snapshot = _manager.Snapshot(); var live = snapshot.Select(j => j.Id).ToHashSet();
        foreach (var view in _items.Where(v => !live.Contains(v.Job.Id)).ToArray()) _items.Remove(view);
        foreach (var job in snapshot)
        {
            var view = _items.FirstOrDefault(v => v.Job.Id == job.Id);
            if (view is null) _items.Add(new(job)); else view.Update(job);
        }
        for (int i = 0; i < snapshot.Length; i++) { var current = _items.IndexOf(_items.First(v => v.Job.Id == snapshot[i].Id)); if (current != i) _items.Move(current, i); }
    }
}
