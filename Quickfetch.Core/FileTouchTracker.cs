namespace Quickfetch.Core;

public enum FileTouchAction { None, Tap, ContextMenu }
public readonly record struct FileTouchResult(FileTouchAction Action, double X, double Y);

// A list-local gesture: consume promoted clicks, preserve one-finger scrolling,
// and decide only after both fingers lift. No background timer or injection.
public sealed class FileTouchTracker
{
    private readonly Dictionary<int, (double X, double Y)> _active = new();
    private double _x, _y;
    private long _started;
    private bool _multi, _cancelled, _moved;
    private int _contacts;
    public bool Active => _active.Count > 0;
    public void Reset() { _active.Clear(); _travel.Clear(); _contacts = 0; _multi = _cancelled = _moved = false; }
    public void Down(int id, double x, double y, long now, double distance)
    {
        if (!Active) { Reset(); _x = x; _y = y; _started = now; }
        else
        {
            _multi = true;
            if (_contacts != 1 || now - _started > 180 || Math.Sqrt(Math.Pow(x - _x, 2) + Math.Pow(y - _y, 2)) > distance) _cancelled = true;
        }
        _contacts++;
        _active[id] = (x, y);
    }
    public double Move(int id, double x, double y)
    {
        if (!_active.TryGetValue(id, out var previous)) return 0;
        var delta = y - previous.Y;
        // Every contact has its own accumulated travel, including the second finger.
        _travel.TryGetValue(id, out var travel);
        travel += Math.Sqrt(Math.Pow(x - previous.X, 2) + Math.Pow(delta, 2));
        _travel[id] = travel;
        if (travel > 12) _moved = true;
        _active[id] = (x, y);
        return _multi || !_moved ? 0 : delta;
    }
    private readonly Dictionary<int, double> _travel = new();
    public FileTouchResult Up(int id, double x, double y, long now)
    {
        Move(id, x, y);
        if (!_active.Remove(id) || Active) return default;
        var action = _cancelled || _moved || now - _started > 350 ? FileTouchAction.None : _multi ? FileTouchAction.ContextMenu : FileTouchAction.Tap;
        var result = new FileTouchResult(action, _x, _y);
        Reset(); _travel.Clear();
        return result;
    }
}
