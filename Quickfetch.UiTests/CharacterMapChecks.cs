using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Quickfetch;
using Quickfetch.Core;

internal static class CharacterMapChecks
{
    internal static void Run(MainWindow main, Action<bool, string> check)
    {
        ((App)Application.Current).SetTheme();
        check(main.FindName("ExtensionsButton") is Button extensions && extensions.ContextMenu.Items[0] is MenuItem { Header: "字符映射" }, "The extension list must start with Character Map.");
        check(MainWindow.IsCharacterMapShortcut(Key.C, ModifierKeys.Alt) && !MainWindow.IsCharacterMapShortcut(Key.C, ModifierKeys.Control) && !MainWindow.IsCharacterMapShortcut(Key.C, ModifierKeys.None), "Alt+C must open Character Map without stealing typing or Ctrl+C.");
        string copied = "";
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var window = new CharacterMapWindow("AB", "Hello world 123", text => copied = text);
        try
        {
            window.Show(); Pump(window.RefreshResults()); window.UpdateLayout();
            var styles = (ListBox)window.FindName("StyleResults");
            VirtualizingPanel.SetIsVirtualizing(styles, false); window.UpdateLayout();
            check(styles.Items.Count == 23 && ((ItemsControl)window.FindName("Characters")).ItemsSource is not null, "Opening the map must asynchronously load both conversion previews and the local character search.");
            var searchRect = ((TextBox)window.FindName("SearchInput")).GetRectFromCharacterIndex(0);
            check(searchRect.Height >= 16 && searchRect.Top >= 0 && searchRect.Bottom <= ((TextBox)window.FindName("SearchInput")).ActualHeight, "The search field must display the full text line without clipping its caret or glyphs.");
            Save(window, "artifacts/character-map-preview.png");
            var composer = (TextBox)window.FindName("Composer"); composer.Select(1, 0);
            var bold = Descendants<Button>(styles).First(b => b.DataContext is CharacterConversion { Name: "数学粗体" });
            bold.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(composer.Text == "A𝐇𝐞𝐥𝐥𝐨 𝐰𝐨𝐫𝐥𝐝 𝟏𝟐𝟑B", "Clicking a whole style preview must insert all converted words at the existing caret.");
            var word = Descendants<Button>(styles).First(b => b.DataContext is CharacterWord { Original: "world", Text: "𝐰𝐨𝐫𝐥𝐝" });
            composer.SelectAll(); word.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(composer.Text == "𝐰𝐨𝐫𝐥𝐝", "Clicking a word chip must replace the selected text with only that word's conversion.");
            var search = (TextBox)window.FindName("SearchInput"); search.Text = "积分"; Pump(window.RefreshResults()); window.UpdateLayout();
            var characters = (ItemsControl)window.FindName("Characters");
            var integral = Descendants<Button>(characters).First(b => b.DataContext is CharacterEntry { Text: "∫" });
            integral.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(composer.Text.EndsWith("∫") && ((TextBlock)window.FindName("StyleHint")).Visibility == Visibility.Visible, "Chinese name searches must show symbol results and avoid meaningless Latin conversion previews.");
            window.Insert("👨‍👩‍👧‍👦");
            ((Button)window.FindName("CopyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(copied == composer.Text && copied.EndsWith("👨‍👩‍👧‍👦"), "Copy must preserve complete joined emoji sequences and assembled text.");
            ((Button)window.FindName("ClearButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(composer.Text.Length == 0, "Clear must empty the composer without changing the search query.");
            check(search.Text == "积分", "Clearing composed text must preserve search state.");
            search.Text = "笑脸"; ((ComboBox)window.FindName("Category")).SelectedItem = "Emoji"; Pump(window.RefreshResults()); window.UpdateLayout();
            check(window.CharacterResults.Any() && window.CharacterResults.All(e => e.Group == "Emoji"), "Category filters must update Chinese emoji results.");
            Save(window, "artifacts/character-map-emoji-preview.png");
            window.Width = 680; window.Height = 580; window.UpdateLayout();
            check(composer.ActualWidth > 400 && characters.ActualWidth > 200 && ((Button)window.FindName("CopyButton")).ActualHeight >= 42, "The compact layout must keep both results and touch-sized composer actions usable.");
            Save(window, "artifacts/character-map-compact-preview.png");
            var systemRequests = 0; window.SystemKeyboardRequest = show => { if (show) systemRequests++; return true; };
            window.ShowInputKeyboard(search); var floating = window.FloatingKeyboard!; floating.UpdateLayout();
            check(floating.IsVisible && floating.Owner == window && !search.Focusable && search.IsReadOnly && System.Windows.Documents.AdornerLayer.GetAdornerLayer(search)?.GetAdorners(search)?.Length > 0, "The built-in floating keyboard must retain a visible caret while keeping text focus away from the OS touch keyboard.");
            search.Text = "a"; search.Select(1, 0);
            var aKey = Descendants<Button>(floating).Single(b => b.Tag as string == "a");
            aKey.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(search.Text == "aa" && systemRequests == 0, "Floating letter keys must edit search without invoking the system keyboard.");
            Descendants<Button>(floating).Single(b => b.Tag as string == "shift").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            aKey.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(search.Text == "aaA", "Shift must change the inserted virtual-keyboard letters to uppercase.");
            composer.Text = "A👨‍👩‍👧‍👦B"; composer.Select(12, 0); window.ShowInputKeyboard(composer);
            Descendants<Button>(floating).Single(b => b.Tag as string == "backspace").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(composer.Text == "AB" && composer.CaretIndex == 1 && search.Text == "aaA", "Switching the active input must preserve its caret and delete complete emoji without editing the other input.");
            Descendants<Button>(floating).Single(b => b.Tag as string == "enter").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(composer.Text == "A\nB", "The composer's tall Enter key must insert a newline.");
            var original = new Point(floating.Left, floating.Top); var dpi = VisualTreeHelper.GetDpi(floating);
            floating.BeginDrag(new Point(0, 0)); floating.DragTo(new Point(20 * dpi.DpiScaleX, 15 * dpi.DpiScaleY));
            check(Math.Abs(floating.Left - original.X - 20) < 1 && Math.Abs(floating.Top - original.Y - 15) < 1, "Dragging the keyboard handle must move the floating window immediately in display-scaled coordinates.");
            Save(floating, "artifacts/character-floating-keyboard-preview.png");
            Descendants<Button>(floating).Single(b => b.Tag as string == "copy").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(copied == composer.Text, "The floating keyboard copy action must use the assembled text.");
            Descendants<Button>(floating).Single(b => b.Tag as string == "hide").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!floating.IsVisible && composer.Focusable && !composer.IsReadOnly, "Hiding the floating keyboard must restore normal physical text editing.");
        }
        finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previousContext); }
        check(window.FloatingKeyboard is null || !window.FloatingKeyboard.IsVisible, "Closing Character Map must close its owned floating keyboard.");
        var fallback = new CharacterMapWindow(useBuiltInKeyboard: false);
        try
        {
            var requested = false; fallback.SystemKeyboardRequest = show => { requested = show; return true; };
            fallback.Show(); fallback.ShowInputKeyboard((TextBox)fallback.FindName("SearchInput"));
            check(requested && fallback.FloatingKeyboard is null, "Disabling the built-in keyboard preference must keep the system keyboard path available.");
        }
        finally { fallback.Close(); }
        var wasVisible = main.IsVisible;
        try
        {
            main.Show(); main.UpdateLayout();
            OpenFromMenu(main, child => { ((TextBox)child.FindName("Composer")).Text = "保留草稿 π"; ((TextBox)child.FindName("SearchInput")).Text = "alpha"; check(child.Owner == main && main.IsVisible, "Opening Character Map from the extension menu must keep the launcher visible as its owner."); });
            OpenFromMenu(main, child => check(((TextBox)child.FindName("Composer")).Text == "保留草稿 π" && ((TextBox)child.FindName("SearchInput")).Text == "alpha", "Reopening Character Map must retain the in-memory draft and search for this app session."));
        }
        finally { if (!wasVisible) main.Hide(); }
    }
    private static void OpenFromMenu(MainWindow main, Action<CharacterMapWindow> inspect)
    {
        var button = (Button)main.FindName("ExtensionsButton");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!button.ContextMenu.IsOpen) throw new Exception("The extension button did not expand its menu.");
        Exception? failure = null; var opened = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) =>
        {
            var child = Application.Current.Windows.OfType<CharacterMapWindow>().FirstOrDefault();
            if (child is null) return;
            timer.Stop(); opened = true;
            try { inspect(child); } catch (Exception ex) { failure = ex; } finally { child.Close(); }
        };
        timer.Start();
        try { ((MenuItem)button.ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); }
        finally { timer.Stop(); }
        if (failure is not null) throw failure;
        if (!opened) throw new Exception("The first extension item did not open Character Map.");
    }
    private static void Pump(Task task)
    {
        var frame = new DispatcherFrame();
        var dispatcher = Dispatcher.CurrentDispatcher;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        timeout.Tick += (_, _) => { frame.Continue = false; };
        task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false));
        timeout.Start(); Dispatcher.PushFrame(frame); timeout.Stop();
        if (!task.IsCompleted) throw new Exception("Character map search timed out.");
        task.GetAwaiter().GetResult();
    }
    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T found) yield return found; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    private static void Save(FrameworkElement view, string path)
    {
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) { var bounds = new Rect(0, 0, view.ActualWidth, view.ActualHeight); dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(22, 25, 30)), null, bounds); dc.DrawRectangle(new VisualBrush(view), null, bounds); }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); Directory.CreateDirectory("artifacts"); using var file = File.Create(path); encoder.Save(file);
    }
}
