namespace Quickfetch.Core;

public sealed class DoubleTapTracker
{
    private string? _path;
    private long _time;
    private double _x, _y;
    public void Reset() => _path = null;
    public bool Tap(string path, long milliseconds, double x, double y, int interval)
    {
        var doubleTap = _path == path && milliseconds >= _time && milliseconds - _time <= interval
            && Math.Abs(x - _x) <= 24 && Math.Abs(y - _y) <= 24;
        _path = doubleTap ? null : path;
        _time = milliseconds; _x = x; _y = y;
        return doubleTap;
    }
}
