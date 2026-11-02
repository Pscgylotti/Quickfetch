using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Quickfetch;

public partial class MainWindow
{
    private string _characterDraft = "", _characterQuery = "";
    private bool _extensionsMenuOpen;
    internal static bool IsCharacterMapShortcut(Key key, ModifierKeys modifiers) => key == Key.C && modifiers == ModifierKeys.Alt;
    private void Extensions_Click(object sender, RoutedEventArgs e)
    {
        if (_choosingFolder) return;
        var menu = ExtensionsButton.ContextMenu;
        menu.PlacementTarget = ExtensionsButton; menu.Placement = PlacementMode.Bottom; menu.IsOpen = !menu.IsOpen;
    }
    private void Extensions_Opened(object sender, RoutedEventArgs e)
    {
        _extensionsMenuOpen = true;
        InputManager.Current.PreProcessInput -= Extensions_PreProcessInput;
        InputManager.Current.PreProcessInput += Extensions_PreProcessInput;
    }
    private void Extensions_Closed(object sender, RoutedEventArgs e)
    {
        _extensionsMenuOpen = false;
        InputManager.Current.PreProcessInput -= Extensions_PreProcessInput;
    }
    private void Extensions_PreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        if (!ExtensionsButton.ContextMenu.IsOpen) return;
        var input = e.StagingItem.Input;
        Point point;
        if (input is TouchEventArgs touch && (touch.RoutedEvent == PreviewTouchDownEvent || touch.RoutedEvent == TouchDownEvent)) point = touch.GetTouchPoint(ExtensionsButton).Position;
        else if (input is StylusDownEventArgs stylus && (stylus.RoutedEvent == PreviewStylusDownEvent || stylus.RoutedEvent == StylusDownEvent)) point = stylus.GetPosition(ExtensionsButton);
        else if (input is MouseButtonEventArgs mouse && mouse.ChangedButton == MouseButton.Left && mouse.ButtonState == MouseButtonState.Pressed) point = mouse.GetPosition(ExtensionsButton);
        else return;
        if (point.X < 0 || point.Y < 0 || point.X >= ExtensionsButton.ActualWidth || point.Y >= ExtensionsButton.ActualHeight) return;
        // Intercept before Popup auto-dismissal: otherwise the subsequent Click opens it again.
        input.Handled = true; ExtensionsButton.ContextMenu.IsOpen = false;
    }
    private void CharacterMap_Click(object sender, RoutedEventArgs e) { ExtensionsButton.ContextMenu.IsOpen = false; OpenCharacterMap(); }
    private void OpenCharacterMap()
    {
        if (_choosingFolder) return;
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
