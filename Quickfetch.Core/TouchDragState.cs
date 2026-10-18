namespace Quickfetch.Core;

// Screen-pixel contact state for OLE drag continuation. Kept independent of
// input injection so release/cancellation rules can be verified without clicks.
public sealed class TouchDragState(double x, double y)
{
    private int? _contact;
    private readonly double _originX = x, _originY = y;
    private volatile bool _released, _cancelled;
    public bool Released => _released;
    public bool Cancelled => _cancelled;
    public double X { get; private set; } = x;
    public double Y { get; private set; } = y;
    public void Frame(IReadOnlyList<TouchContact> frame)
    {
        if (_released || _cancelled) return;
        var down = frame.Where(c => c.Down).ToArray();
        if (down.Length > 1) { _cancelled = true; return; }
        if (_contact is null && down.Length == 1)
        {
            if (Math.Sqrt(Math.Pow(down[0].X - _originX, 2) + Math.Pow(down[0].Y - _originY, 2)) > 180) { _cancelled = true; return; }
            _contact = down[0].Id;
        }
        foreach (var contact in frame)
            if (contact.Id == _contact) { X = contact.X; Y = contact.Y; break; }
        _released = down.Length == 0 || _contact is not null && !down.Any(c => c.Id == _contact);
    }
}
