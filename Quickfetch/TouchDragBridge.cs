using System;
using System.Windows;
using Quickfetch.Core;

namespace Quickfetch;

// OLE normally checks mouse buttons, which touch does not hold. The source's
// QueryContinueDrag uses this contact state; only pointer movement is synthesized.
// No synthetic button is ever pressed, so cancellation cannot leave a button stuck.
internal sealed class TouchDragBridge : IDisposable
{
    private readonly object _gate = new();
    private readonly GlobalTouchGestures _listener;
    private readonly TouchDragState _state;
    private bool _disposed;
    private volatile bool _failed;
    internal bool Released => _state.Released;
    internal bool Cancelled => _failed || _state.Cancelled;
    internal TouchDragBridge(GlobalTouchGestures listener, Point origin) { _listener = listener; _state = new(origin.X, origin.Y); listener.FrameObserved = Observe; }
    private void Observe(TouchContact[] frame)
    {
        lock (_gate)
        {
            if (_disposed || Released || Cancelled) return;
            _state.Frame(frame);
            try { GestureActionRunner.MoveDragPointer((int)_state.X, (int)_state.Y); }
            catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { _failed = true; AppDiagnostics.Current.Fault("touch-drag-pointer", ex); }
        }
    }
    public void Dispose() { lock (_gate) { _disposed = true; _listener.FrameObserved = null; } }
}
