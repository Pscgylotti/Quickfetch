using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Quickfetch;

internal static class QuickToggleChecks
{
    internal static void Run(Action<bool, string> check, bool nativeFlyouts = false, bool energySaver = false)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        CheckPlugin(check);
        var settings = new Settings { UiLanguage = "en-US" };
        var main = new MainWindow(settings, _ => { });
        try
        {
            main.Show(); LauncherActivation.Activate(main); main.UpdateLayout(); Pump(150);
            var anchor = main.ExtensionsButton;
            var center = anchor.TranslatePoint(new Point(anchor.ActualWidth / 2, anchor.ActualHeight / 2), main);
            var finger = new LauncherInputChecks.TestTouch(main, 706);
            for (var i = 1; i <= 4; i++)
            {
                finger.Down(center); finger.Up(center); Pump(15);
                check(main.ExtensionsPanel.IsVisible == (i % 2 == 1), "Short touch must toggle extensions once: " + i);
            }
            var buttons = main.ExtensionEntries.Children.OfType<Button>().ToArray();
            check(buttons.SequenceEqual(new[] { main.CharacterMapMenuItem, main.CaptureMenuItem, main.DownloadsButton }), "Radial and list must share the same three ordered actions.");
            check(AutomationProperties.GetAcceleratorKey(main.DownloadsButton) == "Alt + D", "The Downloads entry must advertise Alt+D.");
            var dispatched = new List<int>();
            for (int i = 0; i < buttons.Length; i++)
            {
                int index = i; var probe = new Button { Tag = buttons[i].Tag };
                AutomationProperties.SetName(probe, AutomationProperties.GetName(buttons[i]));
                probe.Click += (_, _) => dispatched.Add(index);
                int slot = main.ExtensionEntries.Children.IndexOf(buttons[i]);
                main.ExtensionEntries.Children.RemoveAt(slot); main.ExtensionEntries.Children.Insert(slot, probe);
            }
            var directions = new[] { new Vector(0, -72), new Vector(62, 36), new Vector(-62, 36) };
            for (int i = 0; i < directions.Length; i++)
            {
                finger.Down(center);
                if (i == 0)
                {
                    Pump(360);
                    var wheel = PresentationSource.CurrentSources.OfType<PresentationSource>()
                        .SelectMany(source => Descendants(source.RootVisual)).OfType<ExtensionWheel>().Single();
                    wheel.UpdateLayout();
                    var drawing = VisualTreeHelper.GetDrawing(wheel);
                    var glyphs = GlyphRuns(drawing).SelectMany(run => run.GlyphIndices).ToArray();
                    check(glyphs.Length == buttons.Length && glyphs.All(glyph => glyph != 0),
                        $"The wheel must draw one visible icon per action and no text labels. Glyphs: [{string.Join(", ", glyphs)}].");
                }
                finger.Move(center + directions[i]); Pump(25);
                check(dispatched.Count == i, "Sliding over a wheel sector must wait for release.");
                finger.Up(center + directions[i]); Pump(15);
                check(dispatched.Count == i + 1 && dispatched[^1] == i, "Release must route to the sector under the finger: " + i);
            }
            finger.Down(center); Pump(370); finger.Up(center); Pump(15);
            check(dispatched.Count == 3 && !main.ExtensionsPanel.IsVisible, "Releasing a long press in the wheel center must cancel.");

            for (int i = 0; i < directions.Length; i++)
            {
                dispatched.Clear();
                finger.Down(center);
                finger.Move(center + directions[(i + 1) % directions.Length] * 5);
                Pump(25);
                check(dispatched.Count == 0, "Dragging beyond the rim must wait for release.");
                finger.Up(center + directions[i] * 8);
                check(dispatched.SequenceEqual(new[] { i }), "A distant release must use the final direction: " + i);
            }
            dispatched.Clear();
            finger.Down(center); finger.Move(center + directions[1] * 5); Pump(25);
            finger.Move(center); finger.Up(center);
            check(dispatched.Count == 0 && !main.ExtensionsPanel.IsVisible, "Returning from beyond the rim to the center must cancel.");
            finger.Down(center); finger.Move(center + directions[0] * 5); Pump(25);
            finger.Move(center); finger.Move(center + directions[2] * 5); finger.Up(center + directions[2] * 5);
            check(dispatched.SequenceEqual(new[] { 2 }), "Leaving the center again must allow a new selection.");

            main.QuickTogglesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); main.UpdateLayout();
            check(main.TogglePanel.IsVisible, "Quick settings must open from the former download slot.");
            var controls = Descendants(main.TogglePanel).ToArray();
            var names = controls.OfType<Button>().Select(AutomationProperties.GetName).ToArray();
            foreach (var name in new[] { "Wi-Fi", "Bluetooth", "Power mode", "Rotation lock", "Airplane mode", "Mobile hotspot", "v2rayN" })
                check(names.Contains(name) && names.Contains(name + " · Options"), "Missing tile or detail button: " + name);
            check(!names.Contains("Night light"), "Power mode must replace the night light tile.");
            check(controls.OfType<Slider>().Count() == 2, "Brightness and volume must be separate sliders.");
            var before = DateTime.UtcNow;
            while (DateTime.UtcNow - before < TimeSpan.FromSeconds(6) && controls.OfType<Button>().Single(b => AutomationProperties.GetName(b) == "v2rayN").ToolTip as string == "v2rayN") Pump(50);
            foreach (var b in controls.OfType<Button>().Where(b => names.Contains(AutomationProperties.GetName(b) + " · Options")))
                Console.WriteLine(AutomationProperties.GetName(b) + " = " + AutomationProperties.GetItemStatus(b));
            foreach (var s in controls.OfType<Slider>()) Console.WriteLine(AutomationProperties.GetName(s) + " = " + (s.IsEnabled ? s.Value.ToString() : "unavailable"));
            foreach (var slider in controls.OfType<Slider>())
            {
                var track = (Track)slider.Template.FindName("PART_Track", slider);
                var middle = new Point(track.ActualWidth / 2, track.ActualHeight / 2);
                check(track.ActualHeight >= 38 && Math.Abs(track.ValueFromPoint(middle) - 50) < 1,
                    "The visible slider and its touch mapping must agree across the full-height touch target.");
            }
            Button FindButton(string name) => Descendants(main.TogglePanel).OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);
            var power = FindButton("Power mode");
            var initialSaver = power.IsEnabled ? QuickSystemControls.Read("power").GetAwaiter().GetResult().On : null;
            check(AutomationProperties.GetItemStatus(power) == (initialSaver switch { true => "On", false => "Off", null => "Unavailable" }),
                "The power tile must reflect the actual Windows Energy saver switch.");
            if (energySaver && initialSaver == false)
            {
                var profile = QuickPowerMode.Current();
                try
                {
                    power.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    for (int i = 0; i < 150 && !power.IsEnabled; i++) Pump(100);
                    check(QuickSystemControls.Read("power").GetAwaiter().GetResult().On == true &&
                        AutomationProperties.GetItemStatus(power) == "On" && ReferenceEquals(power, FindButton("Power mode")),
                        "One click must enable Windows Energy saver on the home page.");
                    power.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    for (int i = 0; i < 150 && !power.IsEnabled; i++) Pump(100);
                    check(QuickSystemControls.Read("power").GetAwaiter().GetResult().On == false &&
                        AutomationProperties.GetItemStatus(power) == "Off" && QuickPowerMode.Current() == profile,
                        "A second click must turn off Energy saver and preserve the Windows power profile.");
                }
                finally
                {
                    if (QuickSystemControls.Read("power").GetAwaiter().GetResult().On == true)
                    {
                        var restore = QuickSystemControls.Set("power", false, CancellationToken.None);
                        while (!restore.IsCompleted) Pump(50);
                        restore.GetAwaiter().GetResult();
                    }
                    LauncherActivation.Activate(main); Pump(200);
                }
            }
            FindButton("Power mode · Options").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(FindButton("Open Windows settings").IsEnabled, "The power page must also offer Windows power settings.");
            if (power.IsEnabled)
            {
                var modes = QuickPowerMode.Choices.Select(mode => FindButton(mode.Name)).ToArray();
                check(modes.Length == 3 && modes.Count(b => AutomationProperties.GetItemStatus(b) == "Selected") == 1,
                    "Show all three Windows power modes with the configured one selected.");
            }
            FindButton("Back").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var homePower = FindButton("Power mode");
            if (nativeFlyouts)
            {
                main.LaunchQuickProtocol = uri =>
                {
                    Console.WriteLine("Opening native flyout " + uri);
                    QuickSystemControls.OpenSettings(uri);
                };
                main.UpdateLayout();
                foreach (var page in new[] { "Wi-Fi · Options", "Bluetooth · Options", "Sound output" })
                {
                    check(LauncherActivation.Activate(main), "The launcher must own the foreground before opening a device flyout.");
                    FindButton(page).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var corner = main.TogglePanel.PointToScreen(new Point(main.TogglePanel.ActualWidth, 0));
                    bool placed = false;
                    string seen = "", position = "";
                    for (int i = 0; i < 35 && !placed; i++)
                    {
                        Pump(80);
                        var foreground = LauncherActivation.GetForegroundWindow();
                        var title = new System.Text.StringBuilder(80);
                        GetClassName(foreground, title, title.Capacity);
                        if (!seen.EndsWith("|" + title)) seen += "|" + title;
                        if (title.ToString() != "ControlCenterWindow") continue;
                        var regionHandle = CreateRectRgn(0, 0, 0, 0);
                        try
                        {
                            if (GetWindowRgn(foreground, regionHandle) < 2 || GetRgnBox(regionHandle, out var box) < 2) continue;
                            GetWindowRect(foreground, out var bounds);
                            var panelCorner = new System.Drawing.Point((int)Math.Round(corner.X), (int)Math.Round(corner.Y));
                            var expected = SystemQuickFlyout.OriginFor(System.Windows.Forms.Screen.FromPoint(panelCorner).WorkingArea,
                                panelCorner, System.Drawing.Rectangle.FromLTRB(box.Left, box.Top, box.Right, box.Bottom));
                            placed = Math.Abs(expected.X - bounds.Left) <= 2 && Math.Abs(expected.Y - bounds.Top) <= 2;
                            position = $"{bounds.Left},{bounds.Top} expected {expected}";
                            if (placed) main.DismissForOutsideClick(bounds.Left + box.Left + 10, bounds.Top + box.Bottom - 10);
                        }
                        finally { DeleteObject(regionHandle); }
                    }
                    check(placed && main.IsVisible && main.TogglePanel.IsVisible, $"{page} must open the positioned Windows flyout and keep Quickfetch visible. Foreground={seen}, position={position}, visible={main.IsVisible}, panel={main.TogglePanel.IsVisible}");
                    LauncherActivation.Activate(main); Pump(400);
                    check(ReferenceEquals(homePower, FindButton("Power mode")), "Closing the system flyout must leave the same home controls in place.");
                }
            }
            var work = new System.Drawing.Rectangle(-1920, 0, 1920, 1040);
            var region = new System.Drawing.Rectangle(8, 300, 400, 480);
            check(SystemQuickFlyout.OriginFor(work, new(-1800, 900), region) == new System.Drawing.Point(-1928, 260),
                "The visible flyout must stay on the anchor monitor and account for transparent host margins.");
            var anchorPoint = new System.Drawing.Point(-600, 200);
            var rest = SystemQuickFlyout.OriginFor(work, anchorPoint, region);
            foreach (var trigger in new[] { new System.Drawing.Point(-800, 100), new(-800, 800), new(-1400, 440), new(-200, 440) })
            {
                var start = SystemQuickFlyout.EntranceOrigin(work, anchorPoint, region, trigger, 18, 0);
                var midway = SystemQuickFlyout.EntranceOrigin(work, anchorPoint, region, trigger, 18, 0.5);
                var end = SystemQuickFlyout.EntranceOrigin(work, anchorPoint, region, trigger, 18, 1);
                int Distance(System.Drawing.Point p) => Math.Abs(p.X - rest.X) + Math.Abs(p.Y - rest.Y);
                check(end == rest && Distance(start) == 18 && Distance(midway) < Distance(start) &&
                    Math.Sign(start.X - rest.X) == Math.Sign(trigger.X + 800) && Math.Sign(start.Y - rest.Y) == Math.Sign(trigger.Y - 440),
                    "The entrance must start on the trigger side and settle at its anchor.");
            }
            var launched = new List<string>();
            main.LaunchQuickProtocol = uri => { launched.Add(uri); throw new InvalidOperationException("Unavailable in fixture"); };
            foreach (var name in new[] { "Wi-Fi · Options", "Bluetooth · Options", "Sound output" })
            {
                FindButton(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(ReferenceEquals(homePower, FindButton("Power mode")), "A missing Windows flyout must leave the home page in place.");
            }
            check(launched.Count == 3 && launched[0] is ("ms-controlcenter:wifi" or "ms-availablenetworks:") && launched[1].EndsWith("bluetooth") &&
                launched[2].EndsWith("volume"), "Each arrow must directly launch the corresponding Windows panel.");
            main.QuickTogglesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!main.TogglePanel.IsVisible && main.ToggleBody.Children.Count == 0, "Closing quick settings must tear down the page.");
            check(MainWindow.IsUrlShortcut(System.Windows.Input.Key.W, System.Windows.Input.ModifierKeys.Alt), "Alt+W must retain URL entry.");
        }
        finally { main.DisposeLauncher(); main.Close(); SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    private static void CheckPlugin(Action<bool,string> check)
    {
        var root = Directory.CreateTempSubdirectory("qf-toggle-");
        try
        {
            var child = root.CreateSubdirectory("fixture");
            File.WriteAllText(Path.Combine(child.FullName, "plugin.json"), """{"version":1,"id":"sample","title":"Sample","script":"switch.ps1","actions":[{"id":"reset","title":"Reset"}]}""");
            File.WriteAllText(Path.Combine(child.FullName, "switch.ps1"), """
                param($command)
                $state=Join-Path $PSScriptRoot 'state'
                if($command -eq 'on') { [IO.File]::WriteAllText($state, '1') }
                if($command -in 'off','action.reset') { [IO.File]::Delete($state) }
                @{on=[IO.File]::Exists($state);available=$true;note=$env:QUICKFETCH_TOGGLE_EXE} | ConvertTo-Json -Compress
                """);
            var invalid = root.CreateSubdirectory("invalid");
            File.WriteAllText(Path.Combine(invalid.FullName, "plugin.json"), """{"version":1,"id":"bad","title":"Bad","script":"../fixture/switch.ps1"}""");
            var plugin = TogglePlugin.Discover(root.FullName).Single();
            check(plugin.Id == "sample" && plugin.Actions.Single().Id == "reset", "Only valid manifests load.");
            ToggleReading Run(string command)
            {
                var task = plugin.Run(command, "C:\\Example path\\example.exe", CancellationToken.None);
                while (!task.IsCompleted) Pump(15);
                return task.GetAwaiter().GetResult();
            }
            check(Run("status").On == false && Run("on").On == true && Run("status").On == true && Run("off").On == false, "Plugin on/off must round-trip through its backend.");
            check(Run("on").Note == "C:\\Example path\\example.exe" && Run("action.reset").On == false, "Detail action and executable setting must reach the plugin.");
        }
        finally { root.Delete(recursive:true); }
    }
    private static IEnumerable<GlyphRun> GlyphRuns(Drawing drawing)
    {
        if (drawing is GlyphRunDrawing glyph) yield return glyph.GlyphRun;
        if (drawing is DrawingGroup group)
            foreach (var run in group.Children.SelectMany(GlyphRuns)) yield return run;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int capacity);
    [DllImport("user32")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [DllImport("user32")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("gdi32")] private static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);
    [DllImport("gdi32")] private static extern int GetRgnBox(IntPtr region, out Rect rect);
    [DllImport("gdi32")] private static extern bool DeleteObject(IntPtr region);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i=0; i<VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var item in Descendants(child)) yield return item;
        }
    }
    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_,_) => { timer.Stop(); frame.Continue=false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
}
