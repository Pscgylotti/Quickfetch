using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Quickfetch;

public partial class MainWindow
{
    private bool _captureBusy;
    internal static bool IsCaptureShortcut(Key key, ModifierKeys modifiers) => key == Key.S && modifiers == ModifierKeys.Alt;
    private void Capture_Click(object sender, RoutedEventArgs e) { ExtensionsButton.ContextMenu.IsOpen = false; _ = OpenCapture(); }
    private async Task OpenCapture()
    {
        if (_choosingFolder || _captureBusy) return;
        _captureBusy = _choosingFolder = true;
        try
        {
            Hide();
            // Yield through layout and composition so the frozen desktop excludes the launcher and its popup.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); await Task.Delay(180);
            var screen = await Task.Run(ScreenCapture.Take);
            if (_disposed) return;
            var storage = new CaptureStorage(_settings, _saveSettings);
            var selector = new CaptureOverlay(screen, storage); selector.ShowDialog();
            if (selector.ReviewImage is { } image && !_disposed) new CaptureReviewWindow(image, storage).ShowDialog();
        }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex))
        {
            _captureBusy = false; ShowLauncher(); SetMessage(Loc.T("无法截图：") + ex.Message);
        }
        finally { _captureBusy = _choosingFolder = false; }
    }
}
