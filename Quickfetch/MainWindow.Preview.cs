using System;
using System.Linq;
using Quickfetch.Core;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Quickfetch;

public partial class MainWindow
{
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private CancellationTokenSource? _previewCancellation;
    private void InitializeFilePreview()
    {
        FilePreviewCheck.IsChecked = _settings.EnableFilePreview;
        Results.SelectionChanged += (_, _) => QueueFilePreview();
        _previewTimer.Tick += async (_, _) =>
        {
            _previewTimer.Stop();
            if (!_settings.EnableFilePreview || !_fileMode || !IsVisible || Results.SelectedItem is not FileItem item) return;
            var cancellation = new CancellationTokenSource(); _previewCancellation = cancellation;
            PreviewTitle.Text = item.Name; PreviewDetails.Text = Loc.T("正在读取预览…");
            try
            {
                var preview = await FilePreviewService.LoadAsync(item.File, cancellation.Token, partial =>
                {
                    if (Dispatcher.HasShutdownStarted) return;
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (cancellation.IsCancellationRequested || _disposed || !ReferenceEquals(Results.SelectedItem, item) || !IsVisible) return;
                        PreviewImage.Source = partial.Image; PreviewImage.Visibility = partial.Image is null ? Visibility.Collapsed : Visibility.Visible;
                        PreviewDetails.Text = partial.Details;
                    });
                });
                if (cancellation.IsCancellationRequested || _disposed || !ReferenceEquals(Results.SelectedItem, item) || !IsVisible) return;
                PreviewTitle.Text = preview.Title; PreviewDetails.Text = preview.Details;
                PreviewImage.Source = preview.Image; PreviewImage.Visibility = preview.Image is null ? Visibility.Collapsed : Visibility.Visible;
                PreviewText.Text = preview.Text;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex))
            {
                if (!cancellation.IsCancellationRequested) PreviewDetails.Text = Loc.T("无法预览：") + ex.Message;
            }
            finally { if (ReferenceEquals(_previewCancellation, cancellation)) _previewCancellation = null; cancellation.Dispose(); }
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible) CancelFilePreview(); };
    }
    private void CancelFilePreview()
    {
        _previewTimer.Stop(); _previewCancellation?.Cancel();
        PreviewImage.Source = null; PreviewImage.Visibility = Visibility.Collapsed;
        PreviewTitle.Text = Loc.T("文件预览"); PreviewText.Text = ""; PreviewDetails.Text = Loc.T("选择文件以预览");
    }
    private void QueueFilePreview()
    {
        if (_extendingFileSelection) return;
        CancelFilePreview();
        var selected = Results.SelectedItems.OfType<FileItem>().ToArray();
        if (_fileMode && !_urlMode && !_showingUrlHistory && IsVisible)
        {
            var show = _settings.EnableFilePreview || selected.Length > 1;
            if ((FilePreviewPanel.Visibility == Visibility.Visible) != show)
                SetTouchKeyboard(QuickKeyboard.Visibility == Visibility.Visible, physicalKeyboard: !_touchInput);
            if (selected.Length > 1)
            {
                PreviewTitle.Text = Loc.F($"已选中 {selected.Length} 项");
                var total = selected.Where(item => item.File.Size.HasValue).Sum(item => (decimal)item.File.Size!.Value);
                var unknown = selected.Count(item => !item.File.Size.HasValue);
                PreviewDetails.Text = Loc.T("总大小：") + FileEntry.FormatSize((long)Math.Min(total, long.MaxValue))
                    + (unknown > 0 ? Loc.F($"（{unknown} 项大小未知，未计入）") : "");
                PreviewText.Text = string.Join(Environment.NewLine, selected.Select(item => item.Name));
                return;
            }
        }
        if (_settings.EnableFilePreview && _fileMode && IsVisible && Results.SelectedItem is FileItem) _previewTimer.Start();
    }
    private void FilePreview_Click(object sender, RoutedEventArgs e)
    {
        _settings.EnableFilePreview = FilePreviewCheck.IsChecked == true;
        try { _saveSettings(_settings); }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { SetMessage(Loc.T("保存预览设置失败：") + ex.Message); }
        SetTouchKeyboard(QuickKeyboard.Visibility == Visibility.Visible, physicalKeyboard: !_touchInput);
        QueueFilePreview(); FocusSearch();
    }
}
