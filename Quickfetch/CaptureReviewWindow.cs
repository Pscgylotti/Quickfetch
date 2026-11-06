using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace Quickfetch;

internal sealed class CaptureReviewWindow : Window
{
    private readonly BitmapSource _image;
    private readonly CaptureStorage _storage;
    private readonly Action<string> _copyText;
    private readonly Action<BitmapSource> _copyImage;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly WindowsAiOcrSession _ocr = new();
    private readonly Canvas _words = new();
    private readonly Viewbox _view = new() { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.Both };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 10, 16, 12) };
    private readonly TextBox _qrText = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly DockPanel _qrResult = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(8) };
    private readonly Button _scanQr, _openQr;
    private readonly Button _recognize, _copySelection, _registerAi;
    private readonly List<(CaptureWord Word, ToggleButton Button)> _segments = [];
    private bool _closed, _working, _fit = true;
    internal Func<BitmapSource, CancellationToken, Task<CaptureText>> Recognizer { get; set; }
    internal int WordCount => _segments.Count;
    internal string SelectedText => string.Join(Environment.NewLine, _segments.Where(s => s.Button.IsChecked == true).GroupBy(s => s.Word.Line).Select(line => string.Join(" ", line.Select(s => s.Word.Text))));
    internal CaptureReviewWindow(BitmapSource image, CaptureStorage storage, Action<string>? copyText = null, Action<BitmapSource>? copyImage = null)
    {
        _image = image; _storage = storage; _copyText = copyText ?? Clipboard.SetText; _copyImage = copyImage ?? Clipboard.SetImage; Recognizer = _ocr.Recognize;
        Title = Loc.T("Quickfetch · 截图操作"); Width = Math.Min(1120, SystemParameters.WorkArea.Width - 32); Height = Math.Min(820, SystemParameters.WorkArea.Height - 32);
        MinWidth = 560; MinHeight = 420; WindowStartupLocation = WindowStartupLocation.CenterScreen; WindowStyle = WindowStyle.None; Background = Brushes.Transparent;
        CaptureUi.Theme(this); ShowInTaskbar = true;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(6), CornerRadius = new CornerRadius(12) });
        var border = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(12) }; border.SetResourceReference(Border.BackgroundProperty, "Surface"); border.SetResourceReference(Border.BorderBrushProperty, "Line");
        var root = new DockPanel(); border.Child = root; Content = border;
        var title = new DockPanel { Margin = new Thickness(8, 0, 0, 6) };
        var close = CaptureUi.Button("关闭", Close); DockPanel.SetDock(close, Dock.Right); title.Children.Add(close);
        var caption = new TextBlock { Text = Title, FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) };
        caption.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 1) DragMove(); }; title.Children.Add(caption); DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var actions = new WrapPanel();
        _recognize = CaptureUi.Button("识别文字 · OCR", () => _ = Recognize()); actions.Children.Add(_recognize);
        _scanQr = CaptureUi.Button("识别二维码", () => _ = RecognizeQr()); actions.Children.Add(_scanQr);
        _copySelection = CaptureUi.Button("复制所选文字", CopySelection); _copySelection.IsEnabled = false; actions.Children.Add(_copySelection);
        actions.Children.Add(CaptureUi.Button("全选文字", () => SelectAll(true)));
        actions.Children.Add(CaptureUi.Button("取消文字选择", () => SelectAll(false)));
        actions.Children.Add(CaptureUi.Button("复制截图", () => Act(() => { _copyImage(_image); _status.Text = Loc.T("截图已复制。"); })));
        actions.Children.Add(CaptureUi.Button("另存为", () => Save(true)));
        actions.Children.Add(CaptureUi.Button("保存到上次目录", () => Save(false)));
        DockPanel.SetDock(actions, Dock.Top); root.Children.Add(actions);
        var zoom = new WrapPanel { Margin = new Thickness(4, 0, 0, 4) };
        zoom.Children.Add(CaptureUi.Button("适应窗口", () => { _fit = true; ResizePreview(); }));
        zoom.Children.Add(CaptureUi.Button("100%", () => { _fit = false; _view.Width = _image.PixelWidth; }));
        zoom.Children.Add(CaptureUi.Button("−", () => SetZoom(.8))); zoom.Children.Add(CaptureUi.Button("＋", () => SetZoom(1.25)));
        _registerAi = CaptureUi.Button("注册 AI 组件", () => _ = RegisterAi()); _registerAi.Visibility = Visibility.Collapsed; zoom.Children.Add(_registerAi);
        zoom.Children.Add(new TextBlock { Text = $"{image.PixelWidth} × {image.PixelHeight}", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) });
        DockPanel.SetDock(zoom, Dock.Top); root.Children.Add(zoom);
        _status.Text = Loc.T("点击 OCR 识别；随后点击图片上的词段，多选后复制。识别在本机完成。"); DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        var qrActions = new WrapPanel();
        qrActions.Children.Add(CaptureUi.Button("复制二维码内容", () => Act(() => { _copyText(_qrText.Text); _status.Text = Loc.T("二维码内容已复制。"); })));
        _openQr = CaptureUi.Button("打开链接", () => Act(() =>
        {
            if (ScreenshotQr.WebLink(_qrText.Text) is Uri uri)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }));
        qrActions.Children.Add(_openQr);
        DockPanel.SetDock(qrActions, Dock.Bottom); _qrResult.Children.Add(qrActions); _qrResult.Children.Add(_qrText);
        DockPanel.SetDock(_qrResult, Dock.Bottom); root.Children.Add(_qrResult);
        var stage = new Grid { Width = image.PixelWidth, Height = image.PixelHeight, Background = Brushes.Black };
        stage.Children.Add(new Image { Source = image, Stretch = Stretch.Fill }); stage.Children.Add(_words); _view.Child = stage; _scroll.Content = _view; root.Children.Add(_scroll);
        _scroll.SizeChanged += (_, _) => { if (_fit) ResizePreview(); };
        Loaded += (_, _) => { WindowBackdrop.Apply(new WindowInteropHelper(this).Handle); ResizePreview(); };
        Closed += (_, _) => { _closed = true; _lifetime.Cancel(); _ocr.Dispose(); _lifetime.Dispose(); _segments.Clear(); _words.Children.Clear(); _view.Child = null; };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (_qrText.IsKeyboardFocusWithin) return;
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { if (_segments.Any(s => s.Button.IsChecked == true)) CopySelection(); else Act(() => _copyImage(_image)); e.Handled = true; }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { SelectAll(true); e.Handled = true; }
        };
    }
    private async Task RecognizeQr()
    {
        if (_closed || !_scanQr.IsEnabled) return;
        _scanQr.IsEnabled = false; _qrResult.Visibility = Visibility.Collapsed;
        _status.Text = Loc.T("正在识别二维码…");
        try
        {
            var image = _image;
            if (!image.IsFrozen) { image = (BitmapSource)image.Clone(); image.Freeze(); }
            var result = await Task.Run(() => ScreenshotQr.Decode(image), _lifetime.Token);
            if (_closed) return;
            _status.Text = Loc.T(string.IsNullOrEmpty(result) ? "未识别到二维码，请尝试重新框选。" : "已识别二维码。");
            if (string.IsNullOrEmpty(result)) return;
            _qrText.Text = result;
            _openQr.Visibility = ScreenshotQr.WebLink(result) is null ? Visibility.Collapsed : Visibility.Visible;
            _qrResult.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { if (!_closed) _status.Text = Loc.T("二维码识别失败：") + ex.Message; }
        finally { if (!_closed) _scanQr.IsEnabled = true; }
    }
    private void SetZoom(double factor) { _fit = false; _view.Width = Math.Clamp(_view.Width * factor, 100, _image.PixelWidth * 4.0); }
    private void ResizePreview() => _view.Width = Math.Max(1, Math.Min(_scroll.ViewportWidth > 0 ? _scroll.ViewportWidth - 12 : ActualWidth - 60, (_scroll.ViewportHeight > 0 ? _scroll.ViewportHeight - 12 : ActualHeight - 260) * _image.PixelWidth / _image.PixelHeight));
    internal async Task Recognize()
    {
        if (_working || _closed) return;
        _working = true; _recognize.IsEnabled = false; _status.Text = Loc.T("正在准备 Windows AI OCR（NPU）…");
        try
        {
            var result = await Recognizer(_image, _lifetime.Token);
            if (_closed) return;
            ShowText(result);
            _status.Text = result.Words.Length == 0 ? Loc.T("未识别到文字，可更换截图区域后重试。") : Loc.F($"识别到 {result.Words.Length} 个词段 · {result.Language} · 点击词段选择，Ctrl+C 复制。");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { if (!_closed) { _status.Text = Loc.T("文字识别失败：") + ex.Message; if (ex.Message.Contains("windows-ai-identity", StringComparison.Ordinal)) _registerAi.Visibility = Visibility.Visible; } }
        finally { _working = false; if (!_closed) _recognize.IsEnabled = true; }
    }
    private async Task RegisterAi()
    {
        if (_working || _closed) return;
        _working = true; _registerAi.IsEnabled = _recognize.IsEnabled = false;
        try { await WindowsOcr.Register(_lifetime.Token); if (!_closed) { _status.Text = Loc.T("AI 组件已注册，请重新识别。"); _registerAi.Visibility = Visibility.Collapsed; } }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { if (!_closed) _status.Text = ex.Message; }
        finally { _working = false; if (!_closed) _registerAi.IsEnabled = _recognize.IsEnabled = true; }
    }
    internal void ShowText(CaptureText text)
    {
        _segments.Clear(); _words.Children.Clear();
        foreach (var word in text.Words.Take(8000))
        {
            if (string.IsNullOrEmpty(word.Text) || !double.IsFinite(word.X + word.Y + word.Width + word.Height) || word.Width <= 0 || word.Height <= 0 || word.X < 0 || word.Y < 0 || word.X >= _image.PixelWidth || word.Y >= _image.PixelHeight) continue;
            var button = new ToggleButton { Content = new TextBlock { Text = word.Text, FontSize = Math.Max(8, word.Height * .8), Foreground = Brushes.Black, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }, Width = Math.Min(word.Width + 3, _image.PixelWidth - word.X), Height = Math.Min(word.Height + 3, _image.PixelHeight - word.Y), Padding = new Thickness(0), Focusable = false, ToolTip = word.Text };
            var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(2)); border.SetValue(Border.BackgroundProperty, Brushes.White); border.Name = "Word";
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(presenter);
            var template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = border };
            var trigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true }; trigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(130, 193, 255)), "Word")); template.Triggers.Add(trigger); button.Template = template;
            System.Windows.Automation.AutomationProperties.SetName(button, word.Text);
            Canvas.SetLeft(button, word.X); Canvas.SetTop(button, word.Y); button.Checked += (_, _) => UpdateSelection(); button.Unchecked += (_, _) => UpdateSelection();
            _words.Children.Add(button); _segments.Add((word, button));
        }
        UpdateSelection();
    }
    internal void SelectAll(bool select) { foreach (var segment in _segments) segment.Button.IsChecked = select; UpdateSelection(); }
    private void UpdateSelection() => _copySelection.IsEnabled = _segments.Any(s => s.Button.IsChecked == true);
    internal void CopySelection() { if (SelectedText.Length > 0) Act(() => { _copyText(SelectedText); _status.Text = Loc.T("所选文字已复制。"); }); }
    private void Save(bool saveAs) => Act(() => { if (_storage.Save(_image, saveAs) is string path) _status.Text = Loc.T("截图已保存：") + path; });
    private void Act(Action action) { try { action(); } catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { _status.Text = Loc.T("截图操作失败：") + ex.Message; } }
}
