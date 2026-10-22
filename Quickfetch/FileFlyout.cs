using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Quickfetch;

internal enum FileMenuAction { None, Open, OpenFolder, Cut, Copy, CopyPath, Properties, More }

// A Fluent-style app menu. Explorer extensions remain available through More.
internal sealed class FileFlyout : Window
{
    private bool _closing;
    internal FileMenuAction Action { get; private set; }
    internal FileFlyout(string name)
    {
        Title = Loc.T("文件操作"); Width = 294; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; Background = Brushes.Transparent;
        FontFamily = new FontFamily("Segoe UI");
        var panel = new StackPanel { Margin = new Thickness(6) };
        var toolbar = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 3) };
        Add(toolbar, "\uE8C6", Loc.T("剪切"), FileMenuAction.Cut, true);
        Add(toolbar, "\uE8C8", Loc.T("复制"), FileMenuAction.Copy, true);
        Add(toolbar, "\uE71B", Loc.T("复制路径"), FileMenuAction.CopyPath, true);
        panel.Children.Add(toolbar); Divider(panel);
        Add(panel, "\uE8E5", Loc.T("打开"), FileMenuAction.Open);
        Add(panel, "\uE838", Loc.T("打开所在文件夹"), FileMenuAction.OpenFolder);
        Add(panel, "\uE946", Loc.T("属性"), FileMenuAction.Properties);
        Divider(panel);
        Add(panel, "\uE712", Loc.T("显示更多选项"), FileMenuAction.More);
        var border = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = panel };
        border.SetResourceReference(Border.BackgroundProperty, "KeyboardSurface");
        border.SetResourceReference(Border.BorderBrushProperty, "Line"); Content = border;
        border.RenderTransform = new TranslateTransform();
        Loaded += (_, _) =>
        {
            if (!SystemParameters.ClientAreaAnimation) return;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            border.RenderTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            border.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)) { FillBehavior = FillBehavior.Stop });
        };
        ToolTip = name;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Closing += (_, _) => _closing = true;
        Deactivated += (_, _) => { if (!_closing) Close(); };
    }
    protected override void OnSourceInitialized(EventArgs e) { base.OnSourceInitialized(e); WindowBackdrop.Apply(new WindowInteropHelper(this).Handle); }
    private void Add(Panel panel, string glyph, string label, FileMenuAction action, bool compact = false)
    {
        var content = new StackPanel { Orientation = compact ? Orientation.Vertical : Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = compact ? 19 : 17, Width = compact ? double.NaN : 34, HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); content.Children.Add(icon);
        var text = new TextBlock { Text = label, FontSize = compact ? 11.5 : 13.5, VerticalAlignment = VerticalAlignment.Center, Margin = compact ? new Thickness(0, 6, 0, 0) : new Thickness() };
        if (compact) text.TextAlignment = TextAlignment.Center;
        content.Children.Add(text);
        // The shared button template supplies eased hover/press feedback; rows stay 46 DIP for touch.
        var button = new Button { Content = content, Height = compact ? 62 : 46, Padding = new Thickness(compact ? 4 : 12, 0, 12, 0), Margin = new Thickness(0, 1, 0, 1),
            HorizontalContentAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Stretch, ToolTip = label };
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => { Action = action; DialogResult = true; };
        panel.Children.Add(button);
    }
    private static void Divider(Panel panel)
    {
        var line = new Border { Height = 1, Margin = new Thickness(8, 4, 8, 4) }; line.SetResourceReference(Border.BackgroundProperty, "Line"); panel.Children.Add(line);
    }
}
