using Quickfetch.Core;

internal static class GestureChecks
{
    internal static void Run(Action<bool, string> check)
    {
        TouchContact P(int id, double x, double y, bool down = true) => new(id, x, y, down);
        GestureMatch? Swipe(TouchContact[] start, TouchContact[] end, int duration = 400, double width = 1000, double height = 800)
        {
            var r = new TouchGestureRecognizer();
            check(r.Frame(start, 100, width, height) is null, "Finger-down must not trigger an action.");
            check(r.Frame(end, 100 + duration - 50, width, height) is null, "Movement must not trigger before release.");
            return r.Frame([], 100 + duration, width, height);
        }
        check(new GestureSettings().Enabled && !new GestureSettings().TwoFingerRightClick, "Global gestures default on; two-finger right click requires opt-in.");
        check(new GestureSettings().ActionFor(TouchGesture.TopLeftDown).Kind == GestureActionKind.Disabled, "Unconfigured gestures must have no action.");
        check(Swipe([P(1, 100, 2), P(2, 160, 4)], [P(1, 110, 160), P(2, 170, 170)])?.Gesture == TouchGesture.TopLeftDown, "Both fingers from top left must trigger top-left down.");
        check(Swipe([P(1, 700, 0), P(2, 760, 0)], [P(1, 700, 180), P(2, 760, 170)])?.Gesture == TouchGesture.TopRightDown, "Top-right must be distinct from top-left.");
        check(Swipe([P(1, 0, 300), P(2, 4, 360)], [P(1, 150, 300), P(2, 160, 370)])?.Gesture == TouchGesture.LeftEdgeRight, "Left-edge swipe must require both fingers to move right.");
        check(Swipe([P(1, 999, 300), P(2, 995, 360)], [P(1, 800, 300), P(2, 800, 360)])?.Gesture == TouchGesture.RightEdgeLeft, "Right-edge swipe must require both fingers to move left.");
        check(Swipe([P(1, 499, 2), P(2, 501, 2)], [P(1, 499, 160), P(2, 501, 160)]) is null, "A top swipe spanning both screen halves must not trigger either action.");
        check(Swipe([P(1, 100, 100), P(2, 160, 100)], [P(1, 100, 260), P(2, 160, 260)]) is null, "Ordinary two-finger scrolling in the middle must not trigger an edge action.");
        check(Swipe([P(1, 100, 2), P(2, 160, 2)], [P(1, 100, 160), P(2, 160, 2)]) is null, "One moving finger must not trigger a swipe.");
        check(Swipe([P(1, 100, 2), P(2, 160, 2)], [P(1, 320, 160), P(2, 380, 160)]) is null, "A mostly diagonal gesture must not trigger down.");
        check(Swipe([P(1, 100, 2), P(2, 160, 2)], [P(1, 100, 50), P(2, 160, 50)]) is null, "Swipes shorter than 8% of the display height must not trigger.");
        check(Swipe([P(1, 100, 60), P(2, 160, 60)], [P(1, 100, 140), P(2, 160, 140)], height: 1000)?.Gesture == TouchGesture.TopLeftDown, "The top 6% boundary and exact 8% travel must be inclusive.");
        check(Swipe([P(1, 700, 60), P(2, 760, 60)], [P(1, 700, 140), P(2, 760, 140)], height: 1000)?.Gesture == TouchGesture.TopRightDown, "Percentage thresholds must also apply to the right half.");
        check(Swipe([P(1, 100, 60), P(2, 160, 61)], [P(1, 100, 150), P(2, 160, 151)], height: 1000) is null, "Both contacts must start within the top 6%.");
        check(Swipe([P(1, 100, 50), P(2, 160, 50)], [P(1, 100, 130), P(2, 160, 129)], height: 1000) is null, "Both contacts must individually travel at least 8%.");
        check(Swipe([P(1, 100, 120), P(2, 160, 120)], [P(1, 100, 280), P(2, 160, 280)], height: 2000)?.Gesture == TouchGesture.TopLeftDown, "Large displays must use proportional start and travel thresholds.");
        check(Swipe([P(1, 100, 24), P(2, 160, 24)], [P(1, 100, 56), P(2, 160, 56)], height: 400)?.Gesture == TouchGesture.TopLeftDown, "Small displays must not retain the old fixed 100-DIP threshold.");
        check(Swipe([P(1, 100, 2), P(2, 160, 2)], [P(1, 100, 160), P(2, 160, 160)], 1600) is null, "Long held gestures must expire.");
        check(Swipe([P(1, 100, 2)], [P(1, 100, 160)]) is null, "One finger must never trigger.");
        check(Swipe([P(1, 100, 2), P(2, 160, 2), P(3, 220, 2)], [P(1, 100, 160), P(2, 160, 160)]) is null, "A third finger must cancel the entire session.");
        check(Swipe([P(1, 100, 2), P(2, 160, 2)], [P(1, 100, 160), P(2, 160, 160)], width: 300, height: 1000) is null, "Display-local dimensions must determine the top halves.");
        var tap = new TouchGestureRecognizer();
        tap.Frame([P(42, 320, 400)], 100, 1000, 800);
        tap.Frame([P(42, 321, 400), P(43, 370, 420)], 160, 1000, 800);
        check(tap.Frame([P(43, 370, 420)], 220, 1000, 800) is null, "Lifting one finger must wait for the other.");
        var hit = tap.Frame([], 250, 1000, 800);
        check(hit is { Gesture: TouchGesture.TwoFingerTap, FirstX: 320, FirstY: 400 }, "Two-finger tap must preserve the first down position.");
        check(tap.Frame([], 260, 1000, 800) is null, "Repeated empty packets must not repeat an action.");
        check(Swipe([P(1, 200, 300), P(2, 380, 300)], [P(1, 200, 300), P(2, 380, 300)], 200) is null, "Distant fingers must not right-click.");
        check(Swipe([P(1, 200, 300), P(2, 240, 300)], [P(1, 225, 300), P(2, 265, 300)], 200) is null, "A dragged tap must not right-click.");
        check(Swipe([P(1, 200, 300), P(2, 240, 300)], [P(1, 200, 300), P(2, 240, 300)], 400) is null, "Long taps must not right-click.");
        tap.Frame([P(1, 200, 300)], 1000, 1000, 800);
        tap.Frame([P(1, 200, 300), P(2, 240, 300)], 1200, 1000, 800);
        check(tap.Frame([], 1250, 1000, 800) is null, "Late second fingers must not turn separate taps into a gesture.");
        tap.Frame([P(1, 200, 300), P(2, 240, 300)], 2000, 1000, 800);
        tap.Frame([P(1, 260, 300), P(2, 300, 300)], 2050, 1000, 800);
        tap.Frame([P(1, 200, 300), P(2, 240, 300)], 2100, 1000, 800);
        check(tap.Frame([], 2200, 1000, 800) is null, "Moving out and returning must not become a stationary tap.");
        tap.Frame([P(1, 200, 300), P(2, 240, 300)], 3000, 1000, 800); tap.Reset();
        check(tap.Frame([], 3100, 1000, 800) is null, "Disabling or device removal must cancel pending contacts.");
        var frames = new TouchFrameAssembler();
        check(frames.Add(2, [P(1, 0.2, 0.3)], 100) is null, "A partial hybrid frame must wait for continuation.");
        check(frames.Add(0, [P(2, 0.3, 0.3)], 101)?.Length == 2, "A zero-count continuation must join the current frame.");
        check(frames.Add(0, [P(1, 0.2, 0.3, false)], 200)?.Length == 0, "A zero-contact frame after completion must release all contacts.");
        frames.Add(2, [P(1, 0.2, 0.3)], 300);
        check(frames.Add(0, [P(2, 0.3, 0.3)], 500)?.Length == 0, "An incomplete frame must expire instead of mixing old contacts.");
        frames.Add(2, [P(1, 0.2, 0.3)], 600);
        check(frames.Add(1, [P(2, 0.3, 0.3)], 601)?.Single().Id == 2, "A new frame must replace an incomplete old scan.");
        var restored = System.Text.Json.JsonSerializer.Deserialize<GestureSettings>(System.Text.Json.JsonSerializer.Serialize(new GestureSettings { TwoFingerRightClick = true, Actions = new() { [TouchGesture.TopLeftDown] = new() { Kind = GestureActionKind.Shortcut, Shortcut = "Win + S" } } }))!;
        check(restored.TwoFingerRightClick && restored.ActionFor(TouchGesture.TopLeftDown).Shortcut == "Win + S", "Gesture settings and enum-keyed actions must round-trip.");
    }
}
