using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Quickfetch.Core;
using Forms = System.Windows.Forms;

namespace Quickfetch;

public partial class GestureSettingsWindow : Window
{
    private readonly Dictionary<TouchGesture, ActionEditor> _editors = new();
    public GestureSettings Result { get; private set; }
    private sealed record DisplayChoice(string Name, string Label);
    public GestureSettingsWindow(GestureSettings settings)
    {
        InitializeComponent();
        Height = Math.Max(MinHeight, Math.Min(Height, SystemParameters.WorkArea.Height - 20));
        Width = Math.Max(MinWidth, Math.Min(Width, SystemParameters.WorkArea.Width - 20));
        Result = JsonSerializer.Deserialize<GestureSettings>(JsonSerializer.Serialize(settings)) ?? new();
        var dark = ((SolidColorBrush)Application.Current.FindResource("Ink")).Color.R >= 128;
        if (!dark) Background = new SolidColorBrush(Color.FromRgb(236, 241, 249));
        else
        {
            Resources["GestureField"] = new SolidColorBrush(Color.FromRgb(43, 52, 66));
            Resources["GesturePopup"] = new SolidColorBrush(Color.FromRgb(37, 45, 57));
            Resources["GestureBorder"] = new SolidColorBrush(Color.FromRgb(75, 88, 110));
        }
        EnabledCheck.IsChecked = Result.Enabled;
        RightClickCheck.IsChecked = Result.TwoFingerRightClick;
        var displays = new[] { new DisplayChoice("", Loc.T("主显示器（默认）")) }.Concat(Forms.Screen.AllScreens.Select((s, i) => new DisplayChoice(s.DeviceName, Loc.F($"显示器 {i + 1} · {s.Bounds.Width} × {s.Bounds.Height}{(s.Primary ? Loc.T(" · 主屏") : "")}")))).ToArray();
        DisplayPicker.ItemsSource = displays; DisplayPicker.SelectedItem = displays.FirstOrDefault(d => d.Name == Result.DisplayDeviceName) ?? displays[0];
        foreach (var (gesture, label) in new[] { (TouchGesture.TopLeftDown, Loc.T("顶部左半区 · 双指向下")), (TouchGesture.TopRightDown, Loc.T("顶部右半区 · 双指向下")), (TouchGesture.LeftEdgeRight, Loc.T("屏幕左侧 · 双指向右")), (TouchGesture.RightEdgeLeft, Loc.T("屏幕右侧 · 双指向左")) })
        {
            var editor = new ActionEditor(label, Result.ActionFor(gesture));
            _editors.Add(gesture, editor); Editors.Children.Add(editor);
        }
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var actions = _editors.ToDictionary(pair => pair.Key, pair => pair.Value.Read());
            foreach (var (gesture, action) in actions)
            {
                try { GestureActionRunner.Validate(action); }
                catch (ArgumentException ex) { throw new ArgumentException(_editors[gesture].Label + "：" + ex.Message); }
            }
            Result.Enabled = EnabledCheck.IsChecked == true;
            Result.TwoFingerRightClick = RightClickCheck.IsChecked == true;
            Result.DisplayDeviceName = ((DisplayChoice)DisplayPicker.SelectedItem).Name;
            Result.Actions = actions; DialogResult = true;
        }
        catch (ArgumentException ex) { ErrorText.Text = ex.Message; }
    }
    private sealed class ActionEditor : Border
    {
        public string Label { get; }
        private readonly ComboBox _kind = new() { ItemsSource = new[] { Loc.T("未设置"), Loc.T("快捷键"), Loc.T("启动程序 / 快捷方式") }, Margin = new Thickness(0, 8, 0, 0) };
        private readonly TextBox _shortcut = new() { IsReadOnly = true, ToolTip = Loc.T("点击后按下组合键；也可从下方预设中选择"), Margin = new Thickness(0, 8, 0, 0) };
        private readonly TextBox _program = new() { MinWidth = 160, Margin = new Thickness(0, 8, 0, 0), ToolTip = Loc.T("程序的完整路径") };
        private readonly TextBox _arguments = new() { Margin = new Thickness(0, 8, 0, 0), ToolTip = Loc.T("可选：程序启动参数") };
        private readonly StackPanel _keys = new(), _launch = new();
        public ActionEditor(string label, GestureAction action)
        {
            Label = label; Margin = new Thickness(0, 0, 0, 10); Padding = new Thickness(14, 12, 14, 14); CornerRadius = new CornerRadius(10);
            SetResourceReference(BackgroundProperty, "Panel"); SetResourceReference(BorderBrushProperty, "Line"); BorderThickness = new Thickness(1);
            var content = new StackPanel(); Child = content;
            content.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.SemiBold });
            content.Children.Add(_kind); content.Children.Add(_keys); content.Children.Add(_launch);
            _shortcut.Text = action.Shortcut;
            _shortcut.PreviewKeyDown += (_, e) =>
            {
                e.Handled = true;
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
                var parts = new List<string>(); var modifiers = Keyboard.Modifiers;
                if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
                if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
                if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
                if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
                parts.Add(new KeyConverter().ConvertToInvariantString(key) ?? key.ToString());
                var candidate = string.Join(" + ", parts);
                try { GestureActionRunner.ParseShortcut(candidate); _shortcut.Text = candidate; } catch (ArgumentException) { }
            };
            _keys.Children.Add(_shortcut);
            var presets = new ComboBox { ItemsSource = new[] { Loc.T("选择常用快捷键…"), "Esc", "Win + S", "Win + E", "Win + D", "Win + Tab", "Alt + Tab", "Alt + F4", "Ctrl + Shift + Escape", "Win + Ctrl + Left", "Win + Ctrl + Right", "Win + A", "Win + N" }, SelectedIndex = 0, Margin = new Thickness(0, 6, 0, 0) };
            presets.SelectionChanged += (_, _) => { if (presets.SelectedIndex > 0) _shortcut.Text = (string)presets.SelectedItem; };
            _keys.Children.Add(presets);
            _program.Text = action.Program; _arguments.Text = action.Arguments;
            var row = new DockPanel(); var browse = new Button { Content = Loc.T("浏览…"), MinHeight = 42, Margin = new Thickness(8, 8, 0, 0), BorderThickness = new Thickness(1) };
            browse.SetResourceReference(ForegroundProperty, "Ink"); browse.SetResourceReference(BorderBrushProperty, "Line");
            browse.Click += (_, _) =>
            {
                var picker = new OpenFileDialog { Title = Loc.T("选择要启动的程序"), Filter = Loc.T("程序 / 快捷方式|*.exe;*.lnk;*.bat;*.cmd"), DereferenceLinks = false };
                if (picker.ShowDialog(Window.GetWindow(this)) == true) _program.Text = picker.FileName;
            };
            DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse); row.Children.Add(_program); _launch.Children.Add(row);
            _launch.Children.Add(new TextBlock { Text = Loc.T("启动参数（可选；.lnk 在自身属性中设置）"), FontSize = 11, Margin = new Thickness(0, 7, 0, 0) }); _launch.Children.Add(_arguments);
            _kind.SelectionChanged += (_, _) => UpdateKind(); _kind.SelectedIndex = Math.Clamp((int)action.Kind, 0, 2); UpdateKind();
        }
        private void UpdateKind() { _keys.Visibility = _kind.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed; _launch.Visibility = _kind.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed; }
        public GestureAction Read() => new() { Kind = (GestureActionKind)_kind.SelectedIndex, Shortcut = _shortcut.Text, Program = _program.Text.Trim(), Arguments = _arguments.Text };
    }
}
