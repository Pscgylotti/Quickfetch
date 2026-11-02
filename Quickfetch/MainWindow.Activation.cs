using System;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Quickfetch;

public partial class MainWindow
{
    private readonly DispatcherTimer _activationRetry = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private bool _restoringLauncherFocus;
    private int _activationAttempts;
    private IntPtr _activationPrevious;
    private bool _startDismissRequested;
    private void StartLauncherActivation(IntPtr previousForeground)
    {
        _activationRetry.Stop(); _activationRetry.Tick -= RetryLauncherActivation; _activationRetry.Tick += RetryLauncherActivation;
        _activationPrevious = previousForeground; _activationAttempts = 10; _restoringLauncherFocus = true; _startDismissRequested = false;
        RetryLauncherActivation(null, EventArgs.Empty);
    }
    private void RetryLauncherActivation(object? sender, EventArgs e)
    {
        _activationRetry.Stop();
        var foreground = GetForegroundWindow(); var own = new WindowInteropHelper(this).Handle;
        if (_disposed || !IsVisible || _captureBusy || (foreground != own && foreground != _activationPrevious && !LauncherActivation.IsStartMenu(foreground) && !LauncherActivation.IsStartMenu(_activationPrevious)))
        { _restoringLauncherFocus = false; return; }
        if (_activationAttempts < 10 && foreground == own) { _restoringLauncherFocus = false; return; }
        if (!_startDismissRequested && LauncherActivation.DismissStartMenu(foreground))
        {
            _startDismissRequested = true;
            --_activationAttempts; _activationRetry.Start(); return;
        }
        if (LauncherActivation.Activate(this)) FocusSearch();
        if (--_activationAttempts > 0) _activationRetry.Start();
        else _restoringLauncherFocus = false;
    }
}
