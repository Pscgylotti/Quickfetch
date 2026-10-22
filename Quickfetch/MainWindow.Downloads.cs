using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Quickfetch.Core;

namespace Quickfetch;

public partial class MainWindow
{
    private DownloadManager? _downloads;
    private CancellationTokenSource? _downloadBridgeCancellation;
    private Task? _downloadBridgeTask;
    private bool _urlSubmitting;
    private void InitializeDownloads()
    {
        if (_downloads is not null) return;
        _settings.Downloads ??= new DownloadOptions { Folder = DownloadBridge.DefaultFolder() };
        if (string.IsNullOrWhiteSpace(_settings.Downloads.Folder)) _settings.Downloads.Folder = DownloadBridge.DefaultFolder();
        _downloads = new DownloadManager(Path.Combine(Settings.Storage, "downloads-v1.json"), () => _settings.Downloads);
        _downloads.Added += job => { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => DownloadNotifications.Show(job, _downloads)); };
        _downloads.Completed += job => { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => DownloadNotifications.Show(job, _downloads)); };
        _downloadBridgeCancellation = new CancellationTokenSource();
        _downloadBridgeTask = DownloadBridge.Serve(_downloads, _downloadBridgeCancellation.Token);
    }
    private void Downloads_Click(object sender, RoutedEventArgs e)
    {
        if (_choosingFolder) return;
        try { InitializeDownloads(); _choosingFolder = true; new DownloadsWindow(_downloads!, _settings, _saveSettings) { Owner = this }.ShowDialog(); }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { SetMessage(Loc.T("无法打开下载面板：") + ex.Message); }
        finally { _choosingFolder = false; FocusSearch(); }
    }
    private async Task SubmitUrl()
    {
        if (_urlSubmitting || !UrlHistory.TryAddress(UrlToOpen(), out var uri)) { if (!_urlSubmitting) SetMessage(Loc.T("请输入完整网址，或选择历史记录后按 Tab 补全。")); return; }
        _urlSubmitting = true;
        try
        {
            SetMessage(Loc.T("正在打开网址…")); var probe = await _urlHistory.ResolveAsync(uri.AbsoluteUri, async url => { InitializeDownloads(); return await _downloads!.ProbeAsync(url); });
            if (probe.IsFile) { InitializeDownloads(); _downloads!.Enqueue(probe, new(uri.AbsoluteUri)); }
            else Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            try { _urlHistory.Record(uri.AbsoluteUri, probe.IsFile, probe.IsFile ? probe.Name : uri.Host, DateTimeOffset.UtcNow); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppDiagnostics.Current.Fault("url-history-save", ex); }
            Hide();
        }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { SetMessage(Loc.T("无法处理链接：") + ex.Message); }
        finally { _urlSubmitting = false; }
    }
    private void StopDownloads() { _downloadBridgeCancellation?.Cancel(); _downloads?.Dispose(); DownloadNotifications.CloseAll(); }
}
