using System;
using Microsoft.Win32;

namespace Quickfetch;

// System notifications only: no polling, wake requests, watchdog or changes to Windows sleep policy.
internal sealed class PowerLifecycle : IDisposable
{
    private readonly Action<Action> _post;
    private readonly Action _suspend, _resume;
    private readonly AppDiagnostics _diagnostics;
    private readonly Func<long> _clock;
    private volatile bool _disposed;
    private long _lastResume = long.MinValue;
    internal PowerLifecycle(Action<Action> post, Action suspend, Action resume, AppDiagnostics? diagnostics = null, Func<long>? clock = null, bool subscribe = true)
    {
        _post = post; _suspend = suspend; _resume = resume;
        _diagnostics = diagnostics ?? AppDiagnostics.Current; _clock = clock ?? (() => Environment.TickCount64);
        if (subscribe)
        {
            try { SystemEvents.PowerModeChanged += PowerChanged; SystemEvents.SessionSwitch += SessionChanged; }
            catch { Dispose(); throw; }
        }
    }
    private void PowerChanged(object sender, PowerModeChangedEventArgs args) => OnPower(args.Mode);
    private void SessionChanged(object sender, SessionSwitchEventArgs args) => OnSession(args.Reason);
    internal void OnPower(PowerModes mode)
    {
        if (_disposed || mode == PowerModes.StatusChange) return;
        _diagnostics.Write("power", mode.ToString());
        Post(() =>
        {
            if (mode == PowerModes.Suspend) { _lastResume = long.MinValue; _suspend(); }
            else if (mode == PowerModes.Resume) Recover();
        });
    }
    internal void OnSession(SessionSwitchReason reason)
    {
        if (_disposed) return;
        _diagnostics.Write("session", reason.ToString());
        if (reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            Post(Recover);
    }
    private void Recover()
    {
        var now = _clock();
        if (_lastResume != long.MinValue && now >= _lastResume && now - _lastResume < 2000) return;
        _lastResume = now;
        _resume();
        _diagnostics.Write("input-resumed", "Input recovery handler finished; component failures are logged separately.");
    }
    private void Post(Action operation)
    {
        try
        {
            _post(() =>
            {
                if (_disposed) return;
                try { operation(); }
                catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { _diagnostics.Fault("power-recovery", ex); }
            });
        }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { _diagnostics.Fault("power-dispatch", ex); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        SystemEvents.PowerModeChanged -= PowerChanged; SystemEvents.SessionSwitch -= SessionChanged;
    }
}
