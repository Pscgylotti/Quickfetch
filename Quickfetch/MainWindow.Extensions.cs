using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace Quickfetch;

public partial class MainWindow
{
    private string _characterDraft = "", _characterQuery = "";
    internal static bool IsCharacterMapShortcut(Key key, ModifierKeys modifiers) => key == Key.C && modifiers == ModifierKeys.Alt;
    private void Extensions_Click(object sender, RoutedEventArgs e)
    {
        if (_choosingFolder) return;
        if (ExtensionsPanel.IsVisible) { CloseExtensions(); FocusSearch(); return; }
        CloseQuickToggles();
        PositionExtensions();
        ExtensionsPanel.Visibility = Visibility.Visible;
        SetToggled(ExtensionsButton, true);
        CharacterMapMenuItem.Focus();
    }

    private void Extensions_RightClick(object sender, MouseButtonEventArgs e)
    {
        Extensions_Click(sender, e);
        e.Handled = true;
    }

    private void PositionExtensions()
    {
        var right = ExtensionsButton.TranslatePoint(new Point(ExtensionsButton.ActualWidth, 0), ExtensionsLayer).X;
        Canvas.SetLeft(ExtensionsPanel, Math.Clamp(right - ExtensionsPanel.Width, 0, Math.Max(0, ExtensionsLayer.ActualWidth - ExtensionsPanel.Width)));
    }

    private void ExtensionsLayer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ExtensionsPanel.IsVisible) PositionExtensions();
        if (TogglePanel.IsVisible) PositionQuickToggles();
    }

    private void CloseExtensions()
    {
        CancelExtensionPress();
        CloseQuickToggles();
        if (ExtensionsPanel.Visibility != Visibility.Visible) return;
        ExtensionsPanel.Visibility = Visibility.Collapsed;
        SetToggled(ExtensionsButton, false);
    }

    private void DismissExtensionsOutside(object? sender, InputEventArgs e)
    {
        if (e.OriginalSource is not Visual target) return;
        if (TogglePanel.IsVisible && target != QuickTogglesButton && !QuickTogglesButton.IsAncestorOf(target)
            && target != TogglePanel && !TogglePanel.IsAncestorOf(target)) CloseQuickToggles();
        if (!ExtensionsPanel.IsVisible) return;
        if (target == ExtensionsButton || ExtensionsButton.IsAncestorOf(target) ||
            target == ExtensionsPanel || ExtensionsPanel.IsAncestorOf(target)) return;
        CloseExtensions();
    }

    private bool HandleExtensionsKey(Key key)
    {
        if (key == Key.Escape && (_radialPopup?.IsOpen == true || TogglePanel.IsVisible)) { CloseExtensions(); FocusSearch(); return true; }
        if (!ExtensionsPanel.IsVisible) return false;
        var buttons = ExtensionEntries.Children.OfType<Button>().ToArray();
        var index = Array.FindIndex(buttons, b => b.IsKeyboardFocused);
        if (key == Key.Escape) { CloseExtensions(); FocusSearch(); return true; }
        if (key is Key.Up or Key.Down or Key.Tab)
        {
            var previous = key == Key.Up || key == Key.Tab && Keyboard.Modifiers == ModifierKeys.Shift;
            buttons[(index + (previous ? -1 : 1) + buttons.Length) % buttons.Length].Focus();
            return true;
        }
        if (key is Key.Enter or Key.Space)
        {
            buttons[Math.Max(0, index)].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return true;
        }
        return false;
    }

    private void CharacterMap_Click(object sender, RoutedEventArgs e) => OpenCharacterMap();
    private void OpenCharacterMap()
    {
        if (_choosingFolder) return;
        CloseExtensions();
        CharacterMapWindow? window = null;
        try
        {
            _choosingFolder = true;
            window = new CharacterMapWindow(_characterDraft, _characterQuery, useBuiltInKeyboard: _settings.UseBuiltInKeyboard) { Owner = this };
            window.ShowDialog();
        }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { SetMessage(Loc.T("无法打开字符映射：") + ex.Message); }
        finally
        {
            if (window is not null) { _characterDraft = window.Draft; _characterQuery = window.QueryText; }
            _choosingFolder = false; FocusSearch();
        }
    }
}
