using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Quickfetch.Core;

namespace Quickfetch;

internal static class DownloadFileActions
{
    internal static void Open(DownloadJob job)
    {
        if (!File.Exists(job.CompletedPath)) throw new IOException("下载文件不存在或已被移动。");
        Process.Start(new ProcessStartInfo(job.CompletedPath) { UseShellExecute = true });
    }
    internal static void Folder(DownloadJob job) => Process.Start(new ProcessStartInfo("explorer.exe", job.CompletedPath.Length > 0 ? "/select,\"" + job.CompletedPath + "\"" : "\"" + job.Folder + "\"") { UseShellExecute = true });
    internal static void Delete(DownloadJob job)
    {
        if (!File.Exists(job.CompletedPath)) throw new IOException("文件已被删除或移动。");
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(job.CompletedPath, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin, Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);
    }
}

internal static class DownloadNotifications
{
    private static readonly Queue<(DownloadJob Job, DownloadManager Manager)> Pending = new();
    private static readonly List<DownloadToast> Active = [];
    private static bool _closing;
    internal static void Show(DownloadJob job, DownloadManager manager)
    {
        if (_closing) return;
        var existing = Active.Find(t => t.Id == job.Id);
        if (existing is not null) { existing.Refresh(); return; }
        foreach (var pending in Pending) if (pending.Job.Id == job.Id) return;
        Pending.Enqueue((job, manager)); Fill();
    }
    private static void Fill()
    {
        var maximum = SystemParameters.WorkArea.Height < 650 ? 1 : 2;
        while (Active.Count < maximum && Pending.TryDequeue(out var entry))
        {
            var toast = new DownloadToast(entry.Job, entry.Manager); Active.Add(toast);
            toast.Closed += (_, _) => { Active.Remove(toast); if (!_closing) Fill(); };
            toast.SizeChanged += (_, _) => Reflow(); toast.Show();
        }
        Reflow();
    }
    internal static void Reflow()
    {
        var work = SystemParameters.WorkArea; var bottom = work.Bottom - 12;
        foreach (var toast in Active) { toast.Left = Math.Max(work.Left, work.Right - toast.Width - 12); toast.Top = Math.Max(work.Top, bottom - toast.Height); bottom = toast.Top - 8; }
    }
    internal static void CloseAll() { _closing = true; Pending.Clear(); foreach (var toast in Active.ToArray()) toast.Close(); Active.Clear(); }
}

internal sealed class DownloadToast : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DownloadManager _manager;
    private DownloadJob _job;
    private readonly DownloadView _view;
    private readonly DownloadMap _map = new() { Height = 36, Margin = new Thickness(0, 6, 0, 2) };
    private readonly TextBlock _status = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxHeight = 32, ClipToBounds = true };
    private readonly TextBlock _caption = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel _body = new();
    private readonly Button _pause = new() { MinWidth = 56, MinHeight = 36, Padding = new Thickness(6, 2, 6, 2) };
    private readonly Button _open = new() { Content = "打开", MinHeight = 36, Padding = new Thickness(8, 2, 8, 2) };
    private readonly Button _delete = new() { Content = "删除", MinHeight = 36, Padding = new Thickness(8, 2, 8, 2) };
    private DateTimeOffset? _ended;
    private bool _collapsed;
    internal Guid Id => _job.Id;
    internal DownloadToast(DownloadJob job, DownloadManager manager)
    {
        _job = job; _manager = manager; _view = new DownloadView(job);
        Width = Math.Min(390, SystemParameters.WorkArea.Width - 24); Height = 194; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = true; Background = Brushes.Transparent;
        FontFamily = new FontFamily("Segoe UI"); UseLayoutRounding = true;
        SetResourceReference(ForegroundProperty, "Ink");
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0), CornerRadius = new CornerRadius(10) });
        var stack = new StackPanel { Margin = new Thickness(12, 8, 12, 8) }; var border = new Border { Child = stack, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "Surface"); border.SetResourceReference(Border.BorderBrushProperty, "Line"); Content = border;
        var top = new DockPanel();
        var close = new Button { Content = "×", Width = 32, Height = 30, Padding = new Thickness(0), ToolTip = "关闭提示，下载继续" }; close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Right); top.Children.Add(close);
        var collapse = new Button { Content = "−", Width = 32, Height = 30, Padding = new Thickness(0), ToolTip = "收起 / 展开" };
        collapse.Click += (_, _) => { _collapsed = !_collapsed; _body.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible; Height = _collapsed ? 48 : 194; collapse.Content = _collapsed ? "＋" : "−"; };
        DockPanel.SetDock(collapse, Dock.Right); top.Children.Add(collapse); top.Children.Add(_caption); stack.Children.Add(top); stack.Children.Add(_body);
        _body.Children.Add(new TextBlock { Text = job.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 5, 0, 6), ToolTip = job.Name });
        _body.Children.Add(_status); _body.Children.Add(_map);
        var actions = new WrapPanel();
        _pause.Click += (_, _) => Act(() => { if (_job.Status is DownloadStatus.Queued or DownloadStatus.Running) manager.Pause(Id); else manager.Resume(Id); }); actions.Children.Add(_pause);
        _open.Click += (_, _) => Act(() => DownloadFileActions.Open(_job)); actions.Children.Add(_open);
        var folder = new Button { Content = "打开文件夹", MinHeight = 36, Padding = new Thickness(8, 2, 8, 2) }; folder.Click += (_, _) => Act(() => DownloadFileActions.Folder(_job)); actions.Children.Add(folder);
        _delete.Click += (_, _) => Act(() => DownloadFileActions.Delete(_job)); actions.Children.Add(_delete);
        var cancel = new Button { Content = "取消任务", MinHeight = 36, Padding = new Thickness(8, 2, 8, 2) }; cancel.Click += (_, _) => Act(() => manager.Cancel(Id)); actions.Children.Add(cancel); _body.Children.Add(actions);
        _timer.Tick += (_, _) => { Refresh(); if (_ended is DateTimeOffset ended && DateTimeOffset.Now - ended > TimeSpan.FromSeconds(20) && !IsMouseOver) Close(); };
        Loaded += (_, _) => { WindowBackdrop.Apply(new WindowInteropHelper(this).Handle); Refresh(); _timer.Start(); }; Closed += (_, _) => _timer.Stop();
        SizeChanged += (_, _) => { if (IsLoaded) WindowBackdrop.Apply(new WindowInteropHelper(this).Handle); };
    }
    private void Act(Action action) { try { action(); Refresh(); } catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { _status.Text = ex.Message; } }
    internal void Refresh()
    {
        var current = Array.Find(_manager.Snapshot(), job => job.Id == Id); if (current is null) { Close(); return; } _job = current;
        _caption.Text = (current.Status == DownloadStatus.Completed ? "下载完成 · " : "Quickfetch 下载 · ") + current.Name;
        _view.Update(current); _status.Text = _view.Details + (current.Error.Length > 0 ? "\n" + current.Error : ""); _map.Job = current;
        _pause.Content = _view.ActionLabel; _pause.IsEnabled = _view.CanToggle; _open.IsEnabled = _delete.IsEnabled = current.Status == DownloadStatus.Completed;
        if (current.Status is DownloadStatus.Completed or DownloadStatus.Cancelled or DownloadStatus.Failed) _ended ??= DateTimeOffset.Now; else _ended = null;
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e); var handle = new WindowInteropHelper(this).Handle;
        SetWindowLongPtr(handle, -20, new IntPtr(GetWindowLongPtr(handle, -20).ToInt64() | 0x08000080));
        WindowBackdrop.Apply(handle);
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
