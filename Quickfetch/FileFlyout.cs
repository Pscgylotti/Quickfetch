using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Quickfetch;

internal enum FileMenuAction { None, Open, OpenFolder, Cut, Copy, CopyPath, Properties, More }

// A Fluent-style app menu. Explorer extensions remain available through More.
internal sealed class FileFlyout : Window
{
    private bool _closing;
    internal FileMenuAction Action { get; private set; }
    internal FileFlyout(string name)
    {
        Title = "文件操作"; Width = 294; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; Background = Brushes.Transparent;
        FontFamily = new FontFamily("Segoe UI");
        var panel = new StackPanel { Margin = new Thickness(7) };
        var toolbar = new UniformGrid { Columns = 3, Margin = new Thickness(2, 2, 2, 5) };
        Add(toolbar, "\uE8C6", "剪切", FileMenuAction.Cut, true);
        Add(toolbar, "\uE8C8", "复制", FileMenuAction.Copy, true);
        Add(toolbar, "\uE71B", "复制路径", FileMenuAction.CopyPath, true);
        panel.Children.Add(toolbar); Divider(panel);
        Add(panel, "\uE8E5", "打开", FileMenuAction.Open);
        Add(panel, "\uE838", "打开所在文件夹", FileMenuAction.OpenFolder);
        Add(panel, "\uE946", "属性", FileMenuAction.Properties);
        Divider(panel);
        Add(panel, "\uE712", "显示更多选项", FileMenuAction.More);
        var border = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = panel };
        border.SetResourceReference(Border.BackgroundProperty, "KeyboardSurface");
        border.SetResourceReference(Border.BorderBrushProperty, "Line"); Content = border;
        ToolTip = name;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Closing += (_, _) => _closing = true;
        Deactivated += (_, _) => { if (!_closing) Close(); };
    }
    protected override void OnSourceInitialized(EventArgs e) { base.OnSourceInitialized(e); WindowBackdrop.Apply(new WindowInteropHelper(this).Handle); }
    private void Add(Panel panel, string glyph, string label, FileMenuAction action, bool compact = false)
    {
        var content = new StackPanel { Orientation = compact ? Orientation.Vertical : Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 20, Width = compact ? double.NaN : 36, HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); content.Children.Add(icon);
        var text = new TextBlock { Text = label, FontSize = compact ? 11 : 14, VerticalAlignment = VerticalAlignment.Center, Margin = compact ? new Thickness(0, 6, 0, 0) : new Thickness() };
        if (compact) text.TextAlignment = TextAlignment.Center;
        content.Children.Add(text);
        var button = new Button { Content = content, Height = compact ? 64 : 48, HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = label };
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        // An explicit template keeps row content left-aligned and touch targets generous.
        var factory = new FrameworkElementFactory(typeof(Border)); factory.Name = "Hover";
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6)); factory.SetValue(Border.PaddingProperty, new Thickness(10, 5, 10, 5)); factory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = factory };
        foreach (var property in new[] { UIElement.IsMouseOverProperty, UIElement.IsKeyboardFocusWithinProperty })
        {
            var trigger = new Trigger { Property = property, Value = true };
            trigger.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Selection"), "Hover")); template.Triggers.Add(trigger);
        }
        button.Template = template;
        button.Click += (_, _) => { Action = action; DialogResult = true; };
        panel.Children.Add(button);
    }
    private static void Divider(Panel panel)
    {
        var line = new Border { Height = 1, Margin = new Thickness(4, 4, 4, 4) }; line.SetResourceReference(Border.BackgroundProperty, "Line"); panel.Children.Add(line);
    }
}
