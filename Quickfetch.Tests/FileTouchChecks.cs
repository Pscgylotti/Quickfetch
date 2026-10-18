using Quickfetch.Core;

internal static class FileTouchChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var t = new FileTouchTracker();
        void Down(int id, double x, double y, long time) => t.Down(id, x, y, time, 80);
        Down(1, 20, 30, 0);
        check(t.Up(1, 20, 30, 60).Action == FileTouchAction.Tap, "A short single contact must remain a tap.");
        Down(1, 20, 30, 100); Down(2, 50, 30, 140);
        check(t.Up(2, 50, 30, 180).Action == FileTouchAction.None, "No menu may open while the first contact remains down.");
        var menu = t.Up(1, 20, 30, 200);
        check(menu.Action == FileTouchAction.ContextMenu && menu.X == 20 && menu.Y == 30, "Two-finger release must target the first contact, regardless of lift order.");
        Down(1, 20, 30, 300); Down(2, 50, 30, 330); t.Up(1, 20, 30, 350);
        check(t.Up(2, 50, 30, 380).Action == FileTouchAction.ContextMenu, "The opposite lift order must produce exactly one menu.");
        Down(1, 20, 30, 500); Down(2, 150, 30, 530); t.Up(1, 20, 30, 550);
        check(t.Up(2, 150, 30, 580).Action == FileTouchAction.None, "Distant contacts must not become a right click.");
        Down(1, 20, 30, 600); Down(2, 50, 30, 820); t.Up(1, 20, 30, 840);
        check(t.Up(2, 50, 30, 860).Action == FileTouchAction.None, "A late second contact must not become a right click.");
        Down(1, 20, 30, 900); Down(2, 50, 30, 930); t.Move(2, 50, 60); t.Up(2, 50, 60, 960);
        check(t.Up(1, 20, 30, 980).Action == FileTouchAction.None, "Movement of either finger must cancel the tap.");
        Down(1, 20, 30, 1000);
        check(t.Move(1, 20, 32) == 0 && t.Move(1, 20, 60) > 0, "Small jitter must preserve taps, while one-finger travel scrolls.");
        check(t.Up(1, 20, 60, 1100).Action == FileTouchAction.None, "Scrolling must never launch or open a menu.");
        Down(1, 20, 30, 1200); Down(2, 50, 30, 1220); t.Up(2, 50, 30, 1240); Down(3, 50, 30, 1250); t.Up(3, 50, 30, 1260);
        check(t.Up(1, 20, 30, 1270).Action == FileTouchAction.None, "A third contact must cancel, even after a previous finger lifted.");
        Down(1, 20, 30, 1300);
        check(t.Up(1, 20, 30, 1800).Action == FileTouchAction.None, "Long holds must not turn into taps on release.");
        Down(1, 20, 30, 1900); t.Move(1, 20, 70); t.Reset(); Down(1, 20, 30, 2000);
        check(t.Up(1, 20, 30, 2050).Action == FileTouchAction.Tap, "A fresh sequence must discard cancelled travel history.");
        var drag = new TouchDragState(100, 200);
        drag.Frame([new(4, 110, 210)]);
        check(!drag.Released && !drag.Cancelled && drag.X == 110, "Dragging must track the first nearby contact without a mouse button.");
        drag.Frame([new(4, 410, 510)]); drag.Frame([]);
        check(drag.Released && drag.X == 410 && drag.Y == 510, "An empty release frame must preserve the final drop position rather than jump back to the source.");
        drag.Frame([new(5, 900, 900)]);
        check(drag.X == 410 && drag.Released, "A later contact must never restart a completed drag.");
        drag = new(100, 200); drag.Frame([new(4, 110, 210), new(5, 120, 220)]);
        check(drag.Cancelled, "A second finger must cancel a one-finger file drag.");
        drag = new(100, 200); drag.Frame([new(4, 700, 700)]);
        check(drag.Cancelled, "An unrelated contact on another area must never take over the dragged file.");
        drag = new(100, 200); drag.Frame([new(4, 110, 210)]); drag.Frame([new(4, 160, 260, false)]);
        check(drag.Released && drag.X == 160 && drag.Y == 260, "Explicit liftoff reports must carry the actual final drop position.");
    }
}
