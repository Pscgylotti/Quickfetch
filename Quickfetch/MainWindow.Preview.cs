using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Quickfetch;

public partial class MainWindow
{
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
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
            PreviewTitle.Text = item.Name; PreviewDetails.Text = "正在读取预览…";
            try
            {
                var preview = await FilePreviewService.LoadAsync(item.File, cancellation.Token);
                if (cancellation.IsCancellationRequested || _disposed || !ReferenceEquals(Results.SelectedItem, item) || !IsVisible) return;
                PreviewTitle.Text = preview.Title; PreviewDetails.Text = preview.Details;
                PreviewImage.Source = preview.Image; PreviewImage.Visibility = preview.Image is null ? Visibility.Collapsed : Visibility.Visible;
                PreviewText.Text = preview.Text;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex))
            {
                if (!cancellation.IsCancellationRequested) PreviewDetails.Text = "无法预览：" + ex.Message;
            }
            finally { if (ReferenceEquals(_previewCancellation, cancellation)) _previewCancellation = null; cancellation.Dispose(); }
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible) CancelFilePreview(); };
    }
    private void CancelFilePreview()
    {
        _previewTimer.Stop(); _previewCancellation?.Cancel();
        PreviewImage.Source = null; PreviewImage.Visibility = Visibility.Collapsed;
        PreviewTitle.Text = "文件预览"; PreviewText.Text = ""; PreviewDetails.Text = "选择文件以预览";
    }
    private void QueueFilePreview()
    {
        CancelFilePreview();
        if (_settings.EnableFilePreview && _fileMode && IsVisible && Results.SelectedItem is FileItem) _previewTimer.Start();
    }
    private void FilePreview_Click(object sender, RoutedEventArgs e)
    {
        _settings.EnableFilePreview = FilePreviewCheck.IsChecked == true;
        try { _saveSettings(_settings); }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { SetMessage("保存预览设置失败：" + ex.Message); }
        SetTouchKeyboard(QuickKeyboard.Visibility == Visibility.Visible, physicalKeyboard: !_touchInput);
        QueueFilePreview(); FocusSearch();
    }
}
