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
        menu.PlacementTarget = ExtensionsButton; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true;
    }
    private void Extensions_Opened(object sender, RoutedEventArgs e) => _extensionsMenuOpen = true;
    private void Extensions_Closed(object sender, RoutedEventArgs e) => _extensionsMenuOpen = false;
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
