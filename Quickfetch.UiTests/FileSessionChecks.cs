using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Quickfetch;

internal static class FileSessionChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var window = new MainWindow(new Settings(), _ => {});
        window.EverythingAvailable = () => false;
        typeof(MainWindow).GetField("_choosingFolder", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
        try
        {
            window.ShowLauncher();
            ((Button)window.FindName("ModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FileUiChecks.PumpAnimation(window);
            var query = (TextBox)window.FindName("Query"); query.Text = "retained query";
            var rows = (ListBox)window.FindName("Results");
            var items = Enumerable.Range(0,200).Select(i => new FileItem(new("file"+i,"C:\\","C:\\file"+i,false,i,null))).ToArray();
            rows.ItemsSource = items; rows.SelectedIndex = 12; window.UpdateLayout();
            var scroll = Find(rows)!; scroll.ScrollToVerticalOffset(300); window.UpdateLayout();
            var offset = scroll.VerticalOffset;
            window.Hide(); window.ShowLauncher(); Pump();
            Console.WriteLine($"File session: same={ReferenceEquals(rows.ItemsSource,items)}; index={rows.SelectedIndex}; text={query.Text}; mode={typeof(MainWindow).GetField("_fileMode", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)}; pending={typeof(MainWindow).GetField("_fileSearchPending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)}");
            check(ReferenceEquals(rows.ItemsSource,items) && rows.SelectedIndex == 12 && query.Text == "retained query", "Reopening file mode must retain query, result objects and selected file.");
            check(Math.Abs(scroll.VerticalOffset-offset)<1, "Reopening must restore the previous scroll offset.");
            check(query.SelectionLength == query.Text.Length, "Reopening must select the whole query for replacement.");
            rows.SelectedItems.Add(items[5]); rows.SelectedItems.Add(items[8]); window.UpdateLayout();
            check(((TextBlock)window.FindName("PreviewTitle")).Text.Contains("3") && ((TextBlock)window.FindName("PreviewDetails")).Text.Contains("25 B"), "Multiple selection must show item count and total known sizes.");
            window.Hide(); window.ShowLauncher(); Pump();
            check(rows.SelectedItems.Count == 3 && rows.SelectedItems.Contains(items[12]), "Reopening must preserve the complete multi-selection.");
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            rows.RaiseEvent(wheel); Pump(); window.UpdateLayout();
            check(wheel.Handled && Math.Abs(scroll.VerticalOffset-(offset-30))<1, "Wheel input must scroll 30 pixels per notch instead of full rows.");
            typeof(MainWindow).GetMethod("ExtendFileSelection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { 1 });
            check(rows.SelectedItems.Count == 2 && rows.SelectedItems.Contains(items[12]) && rows.SelectedItems.Contains(items[13]), "Keyboard range extension must select the contiguous range from its anchor.");
        }
        finally { window.DisposeLauncher(); window.Close(); }
    }
    private static ScrollViewer? Find(DependencyObject node)
    {
        if(node is ScrollViewer scroll) return scroll;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++) if(Find(VisualTreeHelper.GetChild(node,i)) is {} found) return found;
        return null;
    }
    private static void Pump()
    {
        var frame=new DispatcherFrame(); var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(100)};
        timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
    }
}
