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
        foreach (var (gesture, shortcut) in new[] { (TouchGesture.ThreeFingerLeft, "Alt + Tab"), (TouchGesture.ThreeFingerRight, "Alt + Shift + Tab"), (TouchGesture.ThreeFingerUp, "Win + Tab"), (TouchGesture.ThreeFingerDown, "Win + D") })
            check(new GestureSettings().ActionFor(gesture) is { Kind: GestureActionKind.Shortcut } action && action.Shortcut == shortcut, "Three-finger default action must match its requested direction: " + gesture);
        var old = System.Text.Json.JsonSerializer.Deserialize<GestureSettings>("{\"Actions\":{\"TopLeftDown\":{\"Kind\":1,\"Shortcut\":\"Esc\"}}}")!;
        check(old.ActionFor(TouchGesture.ThreeFingerLeft).Shortcut == "Alt + Tab" && old.ActionFor(TouchGesture.TopLeftDown).Shortcut == "Esc", "Existing saved configurations must acquire new defaults without replacing old custom actions.");
        old.Actions[TouchGesture.ThreeFingerLeft] = new();
        check(old.ActionFor(TouchGesture.ThreeFingerLeft).Kind == GestureActionKind.Disabled, "An explicitly disabled new gesture must stay disabled instead of falling back to its default.");
        var three = new[] { P(1, 350, 300), P(2, 430, 310), P(3, 510, 320) };
        TouchContact[] Shift(double x, double y) => three.Select(p => p with { X = p.X + x, Y = p.Y + y }).ToArray();
        foreach (var (x, y, gesture) in new[] { (-150d, 0d, TouchGesture.ThreeFingerLeft), (150d, 0d, TouchGesture.ThreeFingerRight), (0d, -150d, TouchGesture.ThreeFingerUp), (0d, 150d, TouchGesture.ThreeFingerDown) })
            check(Swipe(three, Shift(x, y))?.Gesture == gesture, "Three-finger swipes must work from the screen interior: " + gesture);
        check(Swipe(three, Shift(-79, 0)) is null && Swipe(three, Shift(-80, 0))?.Gesture == TouchGesture.ThreeFingerLeft, "Three-finger horizontal travel must reach 8% of screen width.");
        check(Swipe(three, Shift(0, 63)) is null && Swipe(three, Shift(0, 64))?.Gesture == TouchGesture.ThreeFingerDown, "Three-finger vertical travel must reach the proportional threshold and minimum distance.");
        check(Swipe(three, Shift(-150, -150)) is null, "Ambiguous diagonal swipes must not trigger a directional action.");
        check(Swipe(three, [P(1, 200, 300), P(2, 280, 310), P(3, 510, 320)]) is null, "A stationary third finger must prevent both a three-finger action and a two-finger fallback.");
        check(Swipe(three, [P(1, 200, 300), P(2, 280, 310), P(3, 660, 320)]) is null, "Opposing finger directions must not count as one swipe.");
        check(Swipe([.. three, P(4, 590, 330)], [.. Shift(-150, 0), P(4, 440, 330)]) is null, "A fourth contact must cancel the gesture instead of becoming a three-finger swipe.");
        check(Swipe(three, Shift(-150, 0), 1500) is null, "Three-finger swipes must expire after the existing time limit.");
        var triple = new TouchGestureRecognizer();
        triple.Frame([three[0]], 100, 1000, 800); triple.Frame(three, 200, 1000, 800); triple.Frame(Shift(-150, 0), 350, 1000, 800);
        check(triple.Frame([Shift(-150, 0)[2]], 400, 1000, 800) is null, "A three-finger gesture must wait until all fingers lift.");
        check(triple.Frame([], 450, 1000, 800)?.Gesture == TouchGesture.ThreeFingerLeft && triple.Frame([], 460, 1000, 800) is null, "A staggered release must trigger once, with no repeated action from empty reports.");
        triple.Frame([three[0], three[1]], 1000, 1000, 800); triple.Frame(three, 1200, 1000, 800); triple.Frame(Shift(-150, 0), 1300, 1000, 800);
        check(triple.Frame([], 1400, 1000, 800) is null, "A late third finger must not turn an existing interaction into a three-finger swipe.");
        check(Swipe([P(1, 100, 2), P(2, 160, 4)], [P(1, 110, 160), P(2, 170, 170)])?.Gesture == TouchGesture.TopLeftDown, "Both fingers from top left must trigger top-left down.");
        check(Swipe([P(1, 700, 0), P(2, 760, 0)], [P(1, 700, 180), P(2, 760, 170)])?.Gesture == TouchGesture.TopRightDown, "Top-right must be distinct from top-left.");
        check(Swipe([P(1, 0, 300), P(2, 4, 360)], [P(1, 150, 300), P(2, 160, 370)])?.Gesture == TouchGesture.LeftEdgeRight, "Left-edge swipe must require both fingers to move right.");
        check(Swipe([P(1, 999, 300), P(2, 995, 360)], [P(1, 800, 300), P(2, 800, 360)])?.Gesture == TouchGesture.RightEdgeLeft, "Right-edge swipe must require both fingers to move left.");
        check(Swipe([P(1, 0, 300), P(2, 4, 360)], [P(1, 60, 300), P(2, 64, 360)])?.Gesture == TouchGesture.LeftEdgeRight, "Left-edge swipes must accept exactly 6% of the screen width.");
        check(Swipe([P(1, 999, 300), P(2, 995, 360)], [P(1, 939, 300), P(2, 935, 360)])?.Gesture == TouchGesture.RightEdgeLeft, "Right-edge swipes must accept exactly 6% of the screen width.");
        check(Swipe([P(1, 0, 300), P(2, 4, 360)], [P(1, 60, 300), P(2, 63, 360)]) is null, "Each finger must reach 6%; average travel is insufficient.");
        check(Swipe([P(1, 0, 300), P(2, 4, 360)], [P(1, 30, 300), P(2, 34, 360)], width: 500)?.Gesture == TouchGesture.LeftEdgeRight, "The 6% threshold must scale down with narrower displays.");
        check(Swipe([P(1, 1999, 300), P(2, 1995, 360)], [P(1, 1879, 300), P(2, 1875, 360)], width: 2000)?.Gesture == TouchGesture.RightEdgeLeft, "The 6% threshold must scale up with wider displays.");
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
        check(Swipe([P(1, 100, 2), P(2, 160, 2), P(3, 220, 2)], [P(1, 100, 160), P(2, 160, 160)]) is null, "A three-contact session must not fall back to a two-finger edge gesture.");
        check(Swipe([P(1, 100, 2), P(2, 160, 2)], [P(1, 100, 160), P(2, 160, 160)], width: 300, height: 1000) is null, "Display-local dimensions must determine the top halves.");
        var four = new[] { P(1, 300, 400), P(2, 350, 400), P(3, 400, 400), P(4, 450, 400) };
        check(Swipe(four, four.Select(p => p with { Y = p.Y - 64 }).ToArray())?.Gesture == TouchGesture.FourFingerUp, "Four fingers up must use the inclusive 8% vertical threshold.");
        check(Swipe(four, four.Select(p => p with { Y = p.Y + 64 }).ToArray())?.Gesture == TouchGesture.FourFingerDown, "Four fingers down must not trigger the three-finger desktop action.");
        check(Swipe(four, four.Select(p => p with { X = p.X + 100 }).ToArray()) is null, "Four-finger horizontal swipes must remain unassigned.");
        check(Swipe(four, four.Select(p => p.Id == 4 ? p : p with { Y = p.Y + 100 }).ToArray()) is null, "A stationary fourth finger must cancel a vertical swipe.");
        check(Swipe(four.Append(P(5, 500, 400)).ToArray(), four.Append(P(5, 500, 400)).Select(p => p with { Y = p.Y + 100 }).ToArray()) is null, "A fifth finger must cancel without falling back to another gesture.");
        check(new GestureSettings().ActionFor(TouchGesture.FourFingerUp).Kind == GestureActionKind.Disabled && new GestureSettings().ActionFor(TouchGesture.FourFingerDown).Kind == GestureActionKind.Disabled, "New four-finger actions must default to unconfigured.");
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
