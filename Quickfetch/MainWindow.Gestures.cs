using System;
using System.Text.Json;
using System.Windows;
using Quickfetch.Core;

namespace Quickfetch;

public partial class MainWindow
{
    private GlobalTouchGestures? _gestures;
    private int _gestureGeneration;
    private void ConfigureGestures()
    {
        var generation = ++_gestureGeneration;
        _gestures?.Dispose(); _gestures = null;
        _settings.Gestures ??= new();
        GestureStatus.Text = _settings.Gestures.Enabled ? "全局监听准备中…" : "全局触屏手势已关闭";
        if (!_settings.Gestures.Enabled || _disposed) return;
        // The listener owns an immutable snapshot; changes replace it rather than race touch frames.
        var snapshot = JsonSerializer.Deserialize<GestureSettings>(JsonSerializer.Serialize(_settings.Gestures))!;
        _gestures = new GlobalTouchGestures(snapshot, (match, x, y) =>
        {
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (_disposed || generation != _gestureGeneration) return;
                try
                {
                    if (match.Gesture == TouchGesture.TwoFingerTap) GestureActionRunner.RightClick(x, y);
                    else
                    {
                        var action = snapshot.ActionFor(match.Gesture);
                        if (action.Kind == GestureActionKind.Disabled) return;
                        // Shortcuts belong to the current foreground window, including Esc for this launcher.
                        if (action.Kind == GestureActionKind.Program && IsVisible) Hide();
                        GestureActionRunner.Run(action, ShowLauncher);
                    }
                }
                catch (Exception ex) { GestureStatus.Text = "手势执行失败：" + ex.Message; _tray?.ShowBalloonTip(4000, "Quickfetch · 手势未执行", ex.Message, System.Windows.Forms.ToolTipIcon.Warning); }
            });
        }, status =>
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => { if (!_disposed && generation == _gestureGeneration) GestureStatus.Text = status; });
        });
    }
    private void Gestures_Click(object sender, RoutedEventArgs e) => OpenGestureSettings();
    private void OpenGestureSettings()
    {
        if (_choosingFolder || _disposed) return;
        ShowLauncher(); _choosingFolder = true;
        ++_gestureGeneration; _gestures?.Dispose(); _gestures = null;
        try
        {
            var dialog = new GestureSettingsWindow(_settings.Gestures ?? new()) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                var previous = _settings.Gestures ?? new();
                _settings.Gestures = dialog.Result;
                try { _settings.Save(); }
                catch { _settings.Gestures = previous; throw; }
            }
        }
        catch (Exception ex) { SetMessage(ex.Message); }
        finally { _choosingFolder = false; ConfigureGestures(); FocusSearch(); }
    }
}
