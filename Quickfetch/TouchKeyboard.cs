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
        AddKey(grid, "", "backspace", 3, 16, 4, "删除光标前的字符", glyph: true);
        AddLetters(grid, "-_.()[]+@#", 4, 0);
        AddKey(grid, "清空", "clear", 5, 0, 6);
        AddKey(grid, "", "up", 5, 6, 4, "上一项", glyph: true);
        AddKey(grid, "", "down", 5, 10, 4, "下一项", glyph: true);
        AddKey(grid, "收起", "hide", 5, 14, 6, "收起键盘");
        AddKey(grid, "启动", "enter", 0, 20, 4);
        Grid.SetRowSpan(grid.Children[grid.Children.Count - 1], 7);
        AddKey(grid, "应用 / 文件", "mode", 6, 0, 6);
        AddKey(grid, "空格", "space", 6, 6, 8);
        AddKey(grid, "打开目录", "directory", 6, 14, 6);
        SetFileMode(false);
        Child = grid;
        AutomationProperties.SetName(this, "英文、数字与文件名符号触屏键盘");
    }
    public void SetFileMode(bool fileMode)
    {
        _grid.RowDefinitions[4].Height = _grid.RowDefinitions[6].Height = new GridLength(fileMode ? KeyHeight : 0);
        foreach (UIElement child in _grid.Children)
        {
            if (Grid.GetRow(child) is 4 or 6) child.Visibility = fileMode ? Visibility.Visible : Visibility.Collapsed;
        }
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
        key.Click += (_, _) => KeyPressed?.Invoke(value);
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
