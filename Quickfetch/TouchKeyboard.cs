using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Quickfetch;

public sealed class TouchKeyboard : Border
{
    private const double KeyHeight = 44;
    private static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private readonly Grid _grid;
    private bool _characterMode, _shift;
    public event Action<string>? KeyPressed;
    public TouchKeyboard()
    {
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(6);
        SetResourceReference(BackgroundProperty, "KeyboardSurface");
        SetResourceReference(BorderBrushProperty, "Line");
        BorderThickness = new Thickness(1);
        var grid = new Grid();
        _grid = grid;
        for (var i = 0; i < 24; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 7; i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(i is 4 or 6 ? 0 : KeyHeight) });
        AddLetters(grid, "1234567890", 0, 0);
        AddLetters(grid, "qwertyuiop", 1, 0);
        AddLetters(grid, "asdfghjkl", 2, 1);
        AddLetters(grid, "zxcvbnm", 3, 2);
        AddKey(grid, "", "backspace", 3, 16, 4, Loc.T("删除光标前的字符"), glyph: true);
        AddLetters(grid, "-_.()[]+@#", 4, 0);
        AddKey(grid, Loc.T("清空"), "clear", 5, 0, 6);
        AddKey(grid, "", "up", 5, 6, 4, Loc.T("上一项"), glyph: true);
        AddKey(grid, "", "down", 5, 10, 4, Loc.T("下一项"), glyph: true);
        AddKey(grid, Loc.T("收起"), "hide", 5, 14, 6, Loc.T("收起键盘"));
        AddKey(grid, Loc.T("启动"), "enter", 0, 20, 4);
        Grid.SetRowSpan(grid.Children[grid.Children.Count - 1], 7);
        AddKey(grid, Loc.T("应用 / 文件"), "mode", 6, 0, 6);
        AddKey(grid, Loc.T("空格"), "space", 6, 6, 8);
        AddKey(grid, Loc.T("打开目录"), "directory", 6, 14, 6);
        SetFileMode(false);
        Child = grid;
        AutomationProperties.SetName(this, Loc.T("英文、数字与文件名符号触屏键盘"));
    }
    public void SetFileMode(bool fileMode)
    {
        _grid.RowDefinitions[4].Height = _grid.RowDefinitions[6].Height = new GridLength(fileMode ? KeyHeight : 0);
        foreach (UIElement child in _grid.Children)
        {
            if (Grid.GetRow(child) is 4 or 6) child.Visibility = fileMode ? Visibility.Visible : Visibility.Collapsed;
        }
    }
    internal void SetUrlMode(bool enabled)
    {
        foreach (UIElement child in _grid.Children)
            if (child is Button key && key.Tag as string == "mode") { key.Content = enabled ? Loc.T("补全网址") : Loc.T("应用 / 文件"); key.ToolTip = enabled ? Loc.T("补全选中的网址 · Tab") : Loc.T("切换应用与文件搜索"); }
    }
    internal void SetCharacterMode(bool composer)
    {
        _characterMode = true; SetFileMode(true);
        foreach (UIElement child in _grid.Children)
        {
            if (child is not Button key) continue;
            var value = key.Tag as string;
            if (value is "mode" or "shift") { key.Tag = "shift"; key.Content = _shift ? "Shift ↑" : "Shift"; key.ToolTip = Loc.T("切换大小写"); }
            else if (value is "directory" or "copy") { key.Tag = "copy"; key.Content = Loc.T("复制文本"); key.ToolTip = Loc.T("复制下方文本框的全部内容"); }
            else if (value is "up" or "left") { key.Tag = "left"; key.Content = "←"; key.ToolTip = Loc.T("光标左移"); }
            else if (value is "down" or "right") { key.Tag = "right"; key.Content = "→"; key.ToolTip = Loc.T("光标右移"); }
            else if (value == "enter" && key.Content is StackPanel enter && enter.Children[1] is TextBlock label) label.Text = composer ? Loc.T("换行") : Loc.T("搜索");
            if (key.Tag is string command && command.Length > 1) AutomationProperties.SetName(key, command == "enter" ? composer ? Loc.T("换行") : Loc.T("搜索") : key.ToolTip as string ?? key.Content?.ToString() ?? command);
        }
        RefreshLetterCase();
        AutomationProperties.SetName(this, Loc.T("字符映射悬浮键盘"));
    }
    private void RefreshLetterCase()
    {
        foreach (UIElement child in _grid.Children)
            if (child is Button key && key.Tag is string value && value.Length == 1 && char.IsAsciiLetter(value[0])) key.Content = _shift ? value.ToUpperInvariant() : value.ToLowerInvariant();
    }
    private void AddLetters(Grid grid, string letters, int row, int start)
    {
        for (var i = 0; i < letters.Length; i++) AddKey(grid, letters[i].ToString().ToUpperInvariant(), letters[i].ToString(), row, start + 2 * i, 2);
    }
    private void AddKey(Grid grid, string text, string value, int row, int column, int span, string? tooltip = null, bool glyph = false)
    {
        // Characters read in Ink, commands step back to Muted, and Enter is the one filled accent key.
        var enter = value == "enter";
        object content = text;
        if (glyph) content = Glyph(text, 17);
        else if (enter)
        {
            var stack = new StackPanel();
            stack.Children.Add(Glyph("", 22));
            var label = new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            label.SetBinding(TextBlock.ForegroundProperty, ButtonForeground());
            stack.Children.Add(label);
            content = stack;
        }
        var key = new Button { Content = content, Tag = value, Focusable = false, IsTabStop = false, FontSize = value.Length == 1 ? 16 : 13,
            Margin = new Thickness(2), Padding = new Thickness(0), ToolTip = tooltip ?? text };
        key.SetResourceReference(Button.BackgroundProperty, enter ? "Accent" : "KeySurface");
        key.SetResourceReference(Button.ForegroundProperty, enter ? "OnAccent" : value.Length == 1 ? "Ink" : "Muted");
        AutomationProperties.SetName(key, tooltip ?? text);
        key.Click += (_, _) =>
        {
            var current = (string)key.Tag;
            if (_characterMode && current == "shift") { _shift = !_shift; key.Content = _shift ? "Shift ↑" : "Shift"; RefreshLetterCase(); return; }
            KeyPressed?.Invoke(_characterMode && _shift && current.Length == 1 ? current.ToUpperInvariant() : current);
        };
        Grid.SetRow(key, row); Grid.SetColumn(key, column); Grid.SetColumnSpan(key, span);
        grid.Children.Add(key);
    }
    private static TextBlock Glyph(string glyph, double size)
    {
        var icon = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = size, HorizontalAlignment = HorizontalAlignment.Center };
        icon.SetBinding(TextBlock.ForegroundProperty, ButtonForeground());
        return icon;
    }
    private static Binding ButtonForeground() => new(nameof(Button.Foreground)) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) };
}
