using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Quickfetch;
using Quickfetch.Core;

internal static class PinnedChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var settings = JsonSerializer.Deserialize<Settings>("{}")!;
        check(settings.PinnedApps.Length == 0, "The pinned rail must start empty.");
        Settings? saved = null;
        var main = new MainWindow(settings, value => saved = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(value)));
        var entry = new ShortcutEntry("Pinned app", "C:\\Test.lnk");
        typeof(MainWindow).GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(main, new[] { entry });
        var item = new ShortcutItem(SearchMatcher.Search([entry], "").Single(), null);
        main.TogglePinned(item);
        var rail = (StackPanel)main.FindName("PinnedItems");
        check(saved?.PinnedApps.SequenceEqual(new[] { entry.Path }) == true && rail.Children.Count == 1, "Pinning must persist the identity and add one icon button.");
        check(rail.Children[0] is Button { Content: TextBlock { Text: "\uE71D" }, Width: >= 40, Height: >= 40 }, "Missing icons must use a generic app glyph with a touch-sized target.");
        var button = rail.Children[0];
        typeof(MainWindow).GetMethod("UpdateResults", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, null);
        check(ReferenceEquals(button, rail.Children[0]), "Typing must reuse rail controls rather than rebuilding pinned icons.");
        var mode = (Button)main.FindName("ModeButton"); mode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(((Border)main.FindName("PinnedRail")).Visibility == Visibility.Collapsed, "Pinned apps must not take space in file-search mode.");
        mode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(rail.Children.Count == 1, "Returning to app mode must restore pinned apps.");
        ((MenuItem)((Button)rail.Children[0]).ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        check(rail.Children.Count == 0 && saved?.PinnedApps.Length == 0, "Unpin must immediately clear both the rail and saved identity.");
        main.TogglePinned(item); main.ShowLauncher(); main.UpdateLayout();
        typeof(MainWindow).GetMethod("ResetOpenAnimation", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, null);
        Save(main, "artifacts/pinned-rail-preview.png", (int)main.ActualWidth, (int)main.ActualHeight);
        main.DisposeLauncher(); main.Close();
        var restored = new MainWindow(new Settings { PinnedApps = [entry.Path] }, _ => {});
        typeof(MainWindow).GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(restored, new[] { entry });
        typeof(MainWindow).GetMethod("UpdateResults", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(restored, null);
        check(((StackPanel)restored.FindName("PinnedItems")).Children.Count == 1, "A saved pin must resolve from the catalog after restart.");
        restored.DisposeLauncher(); restored.Close();
        var picker = new LanguagePicker { Width = 180, Height = 56, SelectedValue = "en-US" };
        picker.Measure(new Size(180, 56)); picker.Arrange(new Rect(0, 0, 180, 56)); picker.UpdateLayout();
        var label = Descendants<ContentPresenter>(picker).First(c => c.Content?.ToString() == "English");
        var center = label.TranslatePoint(new Point(0, label.ActualHeight / 2), picker).Y;
        check(Math.Abs(center - 28) <= 1, "The selected language text must be vertically centered in a taller touch-sized dropdown.");
        var arrow = Descendants<TextBlock>(picker).First(t => t.Text == "⌄");
        check(Math.Abs(arrow.TranslatePoint(new Point(0, arrow.ActualHeight / 2), picker).Y - center) <= 1, "The dropdown arrow and selected text must share a vertical center.");
        Save(picker, "artifacts/language-alignment-preview.png", 180, 56);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Save(Visual visual, string path, int width, int height)
    {
        Directory.CreateDirectory("artifacts");
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); png.Save(stream);
    }
}
