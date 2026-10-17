using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Quickfetch;

public sealed class TouchKeyboard : Border
{
    private readonly Grid _grid;
    public event Action<string>? KeyPressed;
    public TouchKeyboard()
    {
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(8);
        SetResourceReference(BackgroundProperty, "KeyboardSurface");
        SetResourceReference(BorderBrushProperty, "Line");
        BorderThickness = new Thickness(1);
        var grid = new Grid();
        _grid = grid;
        for (var i = 0; i < 24; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 5; i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
        AddLetters(grid, "1234567890", 0, 0);
        AddLetters(grid, "qwertyuiop", 1, 0);
        AddLetters(grid, "asdfghjkl", 2, 1);
        AddLetters(grid, "zxcvbnm", 3, 2);
        AddKey(grid, "⌫", "backspace", 3, 16, 4, "删除光标前的字符");
        AddKey(grid, "清空", "clear", 4, 0, 6);
        AddKey(grid, "↑", "up", 4, 6, 4);
        AddKey(grid, "↓", "down", 4, 10, 4);
        AddKey(grid, "收起", "hide", 4, 14, 6);
        AddKey(grid, "↵\n启动", "enter", 0, 20, 4);
        Grid.SetRowSpan(grid.Children[grid.Children.Count - 1], 5);
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
        AddKey(grid, "应用 / 文件", "mode", 5, 0, 6);
        AddKey(grid, "空格", "space", 5, 6, 8);
        AddKey(grid, "打开目录", "directory", 5, 14, 6);
        SetFileMode(false);
        Child = grid;
        AutomationProperties.SetName(this, "英文与数字触屏键盘");
    }
    public void SetFileMode(bool fileMode)
    {
        _grid.RowDefinitions[5].Height = new GridLength(fileMode ? 46 : 0);
        foreach (UIElement child in _grid.Children)
        {
            if (Grid.GetRow(child) == 5) child.Visibility = fileMode ? Visibility.Visible : Visibility.Collapsed;
            if (child is Button key && (string)key.Tag == "enter") Grid.SetRowSpan(key, fileMode ? 6 : 5);
        }
    }
    private void AddLetters(Grid grid, string letters, int row, int start)
    {
        for (var i = 0; i < letters.Length; i++) AddKey(grid, letters[i].ToString().ToUpperInvariant(), letters[i].ToString(), row, start + 2 * i, 2);
    }
    private void AddKey(Grid grid, string text, string value, int row, int column, int span, string? tooltip = null)
    {
        var key = new Button { Content = text, Tag = value, Focusable = false, IsTabStop = false, FontSize = value == "enter" ? 22 : value.Length == 1 ? 17 : 13,
            Margin = new Thickness(2), Padding = new Thickness(0), ToolTip = tooltip ?? text };
        key.SetResourceReference(Button.BackgroundProperty, value == "enter" ? "Selection" : "KeySurface");
        key.SetResourceReference(Button.ForegroundProperty, "Ink");
        AutomationProperties.SetName(key, tooltip ?? text);
        key.Click += (_, _) => KeyPressed?.Invoke(value);
        Grid.SetRow(key, row); Grid.SetColumn(key, column); Grid.SetColumnSpan(key, span);
        grid.Children.Add(key);
    }
}
