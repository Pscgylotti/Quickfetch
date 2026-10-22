using System;

namespace Quickfetch;

public partial class MainWindow
{
    private bool _inputSuspended;
    internal void SuspendBackgroundInput()
    {
        if (_disposed) return;
        _inputSuspended = true;
        ++_gestureGeneration;
        _gestures?.Dispose(); _gestures = null;
        _shortcut?.Dispose(); _shortcut = null;
        _doubleTap.Reset(); _listTouchId = null; _listTouchItem = null;
        _fileTimer.Stop(); _fileCancellation?.Cancel(); _caretBlink.Stop();
    }
    internal void ResumeBackgroundInput()
    {
        if (_disposed) return;
        _inputSuspended = false;
        try
        {
            _shortcut?.Dispose(); _shortcut = null;
            _shortcut = new GlobalShortcut(() =>
            {
                if (!_disposed && !Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(ShowLauncher);
            });
        }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { AppDiagnostics.Current.Fault("resume-shortcut", ex); SetMessage(Loc.T("唤醒后快捷键恢复失败：") + ex.Message); }
        try { ConfigureGestures(); }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { AppDiagnostics.Current.Fault("resume-touch", ex); GestureStatus.Text = Loc.T("唤醒后触屏监听恢复失败：") + ex.Message; }
        if (IsVisible) { ScheduleBackdrop(); ScheduleCaret(); if (_fileMode) QueueFileSearch(); }
    }
}
