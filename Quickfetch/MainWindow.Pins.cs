using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Quickfetch.Core;

namespace Quickfetch;

public partial class MainWindow
{
    private object? _pinnedCatalog, _pinnedPaths;
    private bool _pinnedFileMode, _pinnedEnglish;
    private static bool IsPinButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button { Name: "PinResultButton" }) return true;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }
    private void PinResult_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (((FrameworkElement)sender).DataContext is ShortcutItem item) TogglePinned(item);
    }
    internal void TogglePinned(ShortcutItem item)
    {
        var old = (_settings.PinnedApps ?? []).Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        var pinned = old.Contains(item.Path, StringComparer.OrdinalIgnoreCase);
        _settings.PinnedApps = pinned ? old.Where(path => !path.Equals(item.Path, StringComparison.OrdinalIgnoreCase)).ToArray()
            : old.Append(item.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        try { _saveSettings(_settings); }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex))
        { _settings.PinnedApps = old; SetMessage(Loc.T("保存固定应用失败：") + ex.Message); }
        RefreshPinnedApps(); FocusSearch();
    }
    private void RefreshPinnedApps()
    {
        if (_pinPressed is not null) return;
        PinnedRail.Visibility = _fileMode ? Visibility.Collapsed : Visibility.Visible;
        if (ReferenceEquals(_pinnedCatalog, _entries) && ReferenceEquals(_pinnedPaths, _settings.PinnedApps) && _pinnedFileMode == _fileMode && _pinnedEnglish == Loc.IsEnglish) return;
        _pinnedCatalog = _entries; _pinnedPaths = _settings.PinnedApps; _pinnedFileMode = _fileMode; _pinnedEnglish = Loc.IsEnglish;
        PinnedItems.Children.Clear();
        if (_fileMode) return;
        var catalog = _entries.DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var path in (_settings.PinnedApps ?? []).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!catalog.TryGetValue(path, out var entry)) continue;
            var item = new ShortcutItem(new SearchResult(entry, 6, 0, 0, 0, 0, 0, false), _icons.GetValueOrDefault(path));
            var button = new Button { Width = 44, Height = 44, Padding = new Thickness(0), Margin = new Thickness(0, 2, 0, 2), Focusable = false, ToolTip = item.Name, Tag = item.Path };
            System.Windows.Automation.AutomationProperties.SetName(button, item.Name);
            if (item.Icon is not null) button.Content = new Image { Source = item.Icon, Width = 28, Height = 28 };
            else button.Content = new TextBlock { Text = "\uE71D", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 24 };
            AttachPinnedInput(button, item);
            var menu = new ContextMenu { Style = (Style)FindResource("PinnedMenu") }; var unpin = new MenuItem { Header = Loc.T("取消固定"), Style = (Style)FindResource("PinnedMenuItem") };
            menu.Opened += (_, _) => _pinnedMenuOpen = true; menu.Closed += (_, _) => _pinnedMenuOpen = false;
            unpin.Click += (_, _) => TogglePinned(item); menu.Items.Add(unpin); button.ContextMenu = menu;
            PinnedItems.Children.Add(button);
        }
    }
}
