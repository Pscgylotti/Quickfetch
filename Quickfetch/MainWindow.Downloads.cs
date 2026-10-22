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
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { SetMessage("无法打开下载面板：" + ex.Message); }
        finally { _choosingFolder = false; FocusSearch(); }
    }
    private async Task SubmitUrl()
    {
        if (_urlSubmitting || !DownloadManager.TryUrl(Query.Text, out var uri)) return;
        _urlSubmitting = true;
        try
        {
            InitializeDownloads(); SetMessage("正在检查网址…"); var probe = await _downloads!.ProbeAsync(uri.AbsoluteUri);
            if (probe.IsFile) _downloads.Enqueue(probe, new(uri.AbsoluteUri));
            else Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            Hide();
        }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { SetMessage("无法处理链接：" + ex.Message); }
        finally { _urlSubmitting = false; }
    }
    private void StopDownloads() { _downloadBridgeCancellation?.Cancel(); _downloads?.Dispose(); DownloadNotifications.CloseAll(); }
}
