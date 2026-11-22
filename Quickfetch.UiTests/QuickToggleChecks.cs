using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    internal static void Run(Action<bool, string> check)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        CheckPlugin(check);
        var settings = new Settings { UiLanguage = "en-US" };
        var main = new MainWindow(settings, _ => { });
        try
        {
            main.Show(); main.UpdateLayout(); Pump(150);
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
                int index = i; var probe = new Button();
                AutomationProperties.SetName(probe, AutomationProperties.GetName(buttons[i]));
                probe.Click += (_, _) => dispatched.Add(index);
                int slot = main.ExtensionEntries.Children.IndexOf(buttons[i]);
                main.ExtensionEntries.Children.RemoveAt(slot); main.ExtensionEntries.Children.Insert(slot, probe);
            }
            var directions = new[] { new Vector(0, -72), new Vector(62, 36), new Vector(-62, 36) };
            for (int i = 0; i < directions.Length; i++)
            {
                finger.Down(center); if (i == 0) Pump(360);
                finger.Move(center + directions[i]); Pump(25);
                check(dispatched.Count == i, "Sliding over a wheel sector must wait for release.");
                finger.Up(center + directions[i]); Pump(15);
                check(dispatched.Count == i + 1 && dispatched[^1] == i, "Release must route to the sector under the finger: " + i);
            }
            finger.Down(center); Pump(370); finger.Up(center); Pump(15);
            check(dispatched.Count == 3 && !main.ExtensionsPanel.IsVisible, "Releasing a long press in the wheel center must cancel.");

            main.QuickTogglesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); main.UpdateLayout();
            check(main.TogglePanel.IsVisible, "Quick settings must open from the former download slot.");
            var controls = Descendants(main.TogglePanel).ToArray();
            var names = controls.OfType<Button>().Select(AutomationProperties.GetName).ToArray();
            foreach (var name in new[] { "Wi-Fi", "Bluetooth", "Night light", "Rotation lock", "Airplane mode", "Mobile hotspot", "v2rayN" })
                check(names.Contains(name) && names.Contains(name + " · Options"), "Missing tile or detail button: " + name);
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
            controls.OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Speakers and microphones").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i=0; i<50 && !Descendants(main.TogglePanel).OfType<Button>().Any(b=>AutomationProperties.GetName(b) == "Open sound settings"); i++) Pump(40);
            var audio = Descendants(main.TogglePanel).OfType<TextBlock>().Select(t => t.Text).ToArray();
            check(audio.Contains("Speakers") && audio.Contains("Microphones"), "The sound page must enumerate output and input separately.");
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
