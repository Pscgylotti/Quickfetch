namespace Quickfetch.Core;

public enum TouchGesture { TwoFingerTap, TopLeftDown, TopRightDown, LeftEdgeRight, RightEdgeLeft, ThreeFingerLeft, ThreeFingerRight, ThreeFingerUp, ThreeFingerDown, FourFingerUp, FourFingerDown }
public enum GestureActionKind { Disabled, Shortcut, Program }
public sealed class GestureAction
{
    public GestureActionKind Kind { get; set; }
    public string Shortcut { get; set; } = "";
    public string Program { get; set; } = "";
    public string Arguments { get; set; } = "";
}
public sealed class GestureSettings
{
    public bool Enabled { get; set; } = true;
    public bool TwoFingerRightClick { get; set; }
    public double TapDistance { get; set; } = 80;
    public string DisplayDeviceName { get; set; } = "";
    public Dictionary<TouchGesture, GestureAction> Actions { get; set; } = new();
    public GestureAction ActionFor(TouchGesture gesture)
    {
        if (Actions is not null && Actions.TryGetValue(gesture, out var action) && action is not null) return action;
        var shortcut = gesture switch
        {
            TouchGesture.ThreeFingerLeft => "Alt + Tab",
            TouchGesture.ThreeFingerRight => "Alt + Shift + Tab",
            TouchGesture.ThreeFingerUp => "Win + Tab",
            TouchGesture.ThreeFingerDown => "Win + D",
            _ => ""
        };
        return shortcut.Length > 0 ? new() { Kind = GestureActionKind.Shortcut, Shortcut = shortcut } : new();
    }
}
public readonly record struct TouchContact(int Id, double X, double Y, bool Down = true);
public readonly record struct GestureMatch(TouchGesture Gesture, double FirstX, double FirstY);

// Input is one complete contact frame, in display-local DIPs. No timer or persisted touch history.
public sealed class TouchGestureRecognizer
{
    private sealed class Track(TouchContact point)
    {
        public TouchContact Start = point, Last = point;
        public double MaxMovement;
        public bool Lifted;
        public void Update(TouchContact point)
        {
            Last = point;
            MaxMovement = Math.Max(MaxMovement, Distance(Start, point));
        }
    }
    private readonly Dictionary<int, Track> _tracks = new();
    private long _started, _lastFrame;
    private bool _cancelled;
    private int _maxDown;
    public void Reset() { _tracks.Clear(); _cancelled = false; _maxDown = 0; }
    public GestureMatch? Frame(IReadOnlyList<TouchContact> contacts, long milliseconds, double width, double height, double tapDistance = 80)
    {
        if (milliseconds - _lastFrame > 2000 || milliseconds < _lastFrame) Reset();
        _lastFrame = milliseconds;
        var down = contacts.Where(p => p.Down).ToArray();
        _maxDown = Math.Max(_maxDown, down.Length);
        if (_tracks.Count == 0)
        {
            if (down.Length == 0) return null;
            _started = milliseconds;
        }
        foreach (var point in contacts)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) { _cancelled = true; continue; }
            if (_tracks.TryGetValue(point.Id, out var track))
            {
                if (track.Lifted && point.Down) _cancelled = true;
                track.Update(point);
            }
            else if (point.Down)
            {
                if (_tracks.Count >= 4 || milliseconds - _started > 180 || _tracks.Values.Any(t => t.Lifted)) _cancelled = true;
                if (_tracks.Count < 5) _tracks.Add(point.Id, new Track(point));
            }
        }
        foreach (var (id, track) in _tracks)
            if (!down.Any(p => p.Id == id)) track.Lifted = true;
        if (down.Length != 0) return null;
        var result = Match(milliseconds - _started, width, height, tapDistance);
        Reset();
        return result;
    }
    private GestureMatch? Match(long duration, double width, double height, double tapDistance)
    {
        if (_cancelled || _tracks.Count is not (2 or 3 or 4) || _maxDown != _tracks.Count || duration > 1400 || !double.IsFinite(width + height) || width <= 0 || height <= 0) return null;
        var pair = _tracks.Values.ToArray(); var a = pair[0]; var b = pair[1];
        GestureMatch Found(TouchGesture gesture) => new(gesture, a.Start.X, a.Start.Y);
        if (pair.Length >= 3)
        {
            if (pair.Any(t => t.Start.X < 0 || t.Start.Y < 0 || t.Start.X > width || t.Start.Y > height)) return null;
            bool Along(Track t, bool horizontal, int direction)
            {
                var primary = horizontal ? t.Last.X - t.Start.X : t.Last.Y - t.Start.Y;
                var cross = horizontal ? t.Last.Y - t.Start.Y : t.Last.X - t.Start.X;
                return primary * direction >= Math.Max(64, (horizontal ? width : height) * .08) && Math.Abs(cross) <= Math.Abs(primary) * .6;
            }
            if (pair.Length == 3 && pair.All(t => Along(t, true, -1))) return Found(TouchGesture.ThreeFingerLeft);
            if (pair.Length == 3 && pair.All(t => Along(t, true, 1))) return Found(TouchGesture.ThreeFingerRight);
            if (pair.All(t => Along(t, false, -1))) return Found(pair.Length == 4 ? TouchGesture.FourFingerUp : TouchGesture.ThreeFingerUp);
            if (pair.All(t => Along(t, false, 1))) return Found(pair.Length == 4 ? TouchGesture.FourFingerDown : TouchGesture.ThreeFingerDown);
            return null;
        }
        if (duration <= 320 && pair.All(t => t.MaxMovement <= 18) && Distance(a.Start, b.Start) <= Math.Clamp(tapDistance, 20, 140))
            return Found(TouchGesture.TwoFingerTap);
        const double edge = 32;
        bool Vertical(Track t) => t.Last.Y - t.Start.Y >= height * 0.08 && Math.Abs(t.Last.X - t.Start.X) <= (t.Last.Y - t.Start.Y) * 0.6;
        bool Horizontal(Track t, int direction) => (t.Last.X - t.Start.X) * direction >= width * .06 && Math.Abs(t.Last.Y - t.Start.Y) <= Math.Abs(t.Last.X - t.Start.X) * 0.6;
        if (pair.All(t => t.Start.Y >= 0 && t.Start.Y <= height * 0.06 && Vertical(t)))
        {
            if (pair.All(t => t.Start.X < width / 2)) return Found(TouchGesture.TopLeftDown);
            if (pair.All(t => t.Start.X >= width / 2)) return Found(TouchGesture.TopRightDown);
        }
        if (pair.All(t => t.Start.X is >= 0 and <= edge && Horizontal(t, 1))) return Found(TouchGesture.LeftEdgeRight);
        if (pair.All(t => t.Start.X >= width - edge && t.Start.X <= width && Horizontal(t, -1))) return Found(TouchGesture.RightEdgeLeft);
        return null;
    }
    private static double Distance(TouchContact a, TouchContact b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}

// HID hybrid reports may split one scan over several packets. Never interpret an incomplete packet as a lift.
public sealed class TouchFrameAssembler
{
    private readonly Dictionary<int, TouchContact> _pending = new();
    private int _expected;
    private long _started;
    public void Reset() { _pending.Clear(); _expected = 0; }
    public TouchContact[]? Add(int count, IReadOnlyList<TouchContact> slots, long now)
    {
        if (now - _started > 100) Reset();
        if (count > 0) { Reset(); _expected = count; _started = now; }
        if (_expected == 0) return Array.Empty<TouchContact>();
        foreach (var slot in slots)
        {
            if (_pending.Count < _expected || _pending.ContainsKey(slot.Id)) _pending[slot.Id] = slot;
        }
        if (_pending.Count < _expected) return null;
        var frame = _pending.Values.ToArray(); Reset(); return frame;
    }
}
