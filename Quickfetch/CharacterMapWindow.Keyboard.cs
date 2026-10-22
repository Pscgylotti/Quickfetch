using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Quickfetch.Core;

namespace Quickfetch;

public partial class CharacterMapWindow
{
    private readonly bool _useBuiltInKeyboard;
    private TextBox _keyboardTarget = null!;
    private FloatingKeyboardWindow? _floatingKeyboard;
    private InsertionCaret? _inputCaret;
    internal Func<bool, bool>? SystemKeyboardRequest { get; set; }
    internal FloatingKeyboardWindow? FloatingKeyboard => _floatingKeyboard;
    private void Keyboard_Click(object sender, RoutedEventArgs e)
    {
        if (_useBuiltInKeyboard && _floatingKeyboard?.IsVisible == true) HideFloatingKeyboard();
        else ShowInputKeyboard(_keyboardTarget);
    }
    private void Text_Focused(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox text) return;
        _keyboardTarget = text;
        _floatingKeyboard?.SetTarget(text == Composer);
    }
    private void Text_TouchDown(object sender, TouchEventArgs e)
    {
        if (sender is not TextBox text) return;
        ShowInputKeyboard(text);
        if (_useBuiltInKeyboard) { PlaceInputCaret(text, e.GetTouchPoint(text).Position); e.Handled = true; }
    }
    private void Text_StylusDown(object sender, StylusDownEventArgs e)
    {
        if (sender is not TextBox text || e.StylusDevice.TabletDevice.Type != TabletDeviceType.Touch) return;
        ShowInputKeyboard(text);
        if (_useBuiltInKeyboard) { PlaceInputCaret(text, e.GetPosition(text)); e.Handled = true; }
    }
    private void Text_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox text) return;
        _keyboardTarget = text;
        if (_useBuiltInKeyboard && (e.StylusDevice is not null || _floatingKeyboard?.IsVisible == true || TabletPosture.IsTablet))
        { ShowInputKeyboard(text); PlaceInputCaret(text, e.GetPosition(text)); e.Handled = true; }
    }
    internal void ShowInputKeyboard(TextBox target)
    {
        if (_closed) return;
        _keyboardTarget = target;
        if (!_useBuiltInKeyboard) { target.Focus(); ShowSystemKeyboard(); return; }
        HideSystemKeyboard();
        foreach (var input in new[] { SearchInput, Composer }) { input.IsReadOnly = true; input.Focusable = false; input.IsTabStop = false; }
        Keyboard.Focus(InputSurface);
        if (_floatingKeyboard is null)
        {
            _floatingKeyboard = new FloatingKeyboardWindow(KeyboardKeyPressed) { Owner = this };
            _floatingKeyboard.Left = Math.Clamp(Left + (ActualWidth - _floatingKeyboard.Width) / 2, SystemParameters.VirtualScreenLeft, Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - _floatingKeyboard.Width));
            var proposed = target == Composer ? Top + ActualHeight - 150 - _floatingKeyboard.Height : Top + 120;
            _floatingKeyboard.Top = Math.Clamp(proposed, SystemParameters.VirtualScreenTop, Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - _floatingKeyboard.Height));
        }
        _floatingKeyboard.SetTarget(target == Composer);
        if (!_floatingKeyboard.IsVisible) _floatingKeyboard.Show();
        UpdateInputCaret();
    }
    private void HideFloatingKeyboard()
    {
        _floatingKeyboard?.Hide();
        foreach (var input in new[] { SearchInput, Composer }) { input.IsReadOnly = false; input.Focusable = true; input.IsTabStop = true; }
        RemoveInputCaret();
    }
    private void ShowSystemKeyboard()
    {
        var accepted = SystemKeyboardRequest is { } request ? request(true) : SystemTouchKeyboard.Request(new WindowInteropHelper(this).Handle, true, out _);
        if (accepted) _systemKeyboardOwned = true; else Message.Text = Loc.T("系统键盘未打开，可使用物理键盘输入。");
    }
    private void HideSystemKeyboard()
    {
        if (!_systemKeyboardOwned) return;
        _systemKeyboardOwned = false;
        if (SystemKeyboardRequest is { } request) request(false); else SystemTouchKeyboard.Request(new WindowInteropHelper(this).Handle, false, out _);
    }
    internal void KeyboardKeyPressed(string key)
    {
        if (key == "hide") { HideFloatingKeyboard(); return; }
        if (key == "copy") { CopyText(); return; }
        if (key == "enter" && _keyboardTarget == SearchInput) { _ = RefreshResults(); return; }
        var edit = CharacterTextEditor.Apply(_keyboardTarget.Text, _keyboardTarget.SelectionStart, _keyboardTarget.SelectionLength, key);
        if (edit.Text.Length > _keyboardTarget.MaxLength) { Message.Text = Loc.T("文本已达到长度上限，请先复制或清空。"); return; }
        _keyboardTarget.Text = edit.Text; _keyboardTarget.Select(edit.Caret, 0); UpdateInputCaret();
    }
    private void PlaceInputCaret(TextBox target, Point point)
    {
        var boundaries = StringInfo.ParseCombiningCharacters(target.Text).Append(target.Text.Length);
        var best = target.Text.Length; var distance = double.PositiveInfinity;
        foreach (var index in boundaries)
        {
            var rect = target.GetRectFromCharacterIndex(index);
            if (rect.IsEmpty) continue;
            var dx = rect.X - point.X; var dy = point.Y - Math.Clamp(point.Y, rect.Top, rect.Bottom);
            if (dx * dx + dy * dy < distance) { distance = dx * dx + dy * dy; best = index; }
        }
        target.Select(best, 0); UpdateInputCaret();
    }
    private void RemoveInputCaret()
    {
        if (_inputCaret is not null) AdornerLayer.GetAdornerLayer(_inputCaret.AdornedElement)?.Remove(_inputCaret);
        _inputCaret = null;
    }
    private void UpdateInputCaret()
    {
        if (_closed || _keyboardTarget is null || _floatingKeyboard?.IsVisible != true) return;
        if (_inputCaret?.AdornedElement != _keyboardTarget)
        {
            RemoveInputCaret();
            if (AdornerLayer.GetAdornerLayer(_keyboardTarget) is { } layer) { _inputCaret = new InsertionCaret(_keyboardTarget); layer.Add(_inputCaret); }
        }
        var rect = _keyboardTarget.GetRectFromCharacterIndex(_keyboardTarget.SelectionStart);
        if (!rect.IsEmpty)
        {
            if (rect.Right > _keyboardTarget.ActualWidth - 12) _keyboardTarget.ScrollToHorizontalOffset(_keyboardTarget.HorizontalOffset + rect.Right - _keyboardTarget.ActualWidth + 12);
            else if (rect.Left < 12) _keyboardTarget.ScrollToHorizontalOffset(Math.Max(0, _keyboardTarget.HorizontalOffset + rect.Left - 12));
            var line = _keyboardTarget.GetLineIndexFromCharacterIndex(_keyboardTarget.SelectionStart);
            if (line >= 0 && (rect.Top < 0 || rect.Bottom > _keyboardTarget.ActualHeight)) _keyboardTarget.ScrollToLine(line);
        }
        _inputCaret?.InvalidateVisual();
    }
    private sealed class InsertionCaret : Adorner
    {
        private readonly TextBox target;
        internal InsertionCaret(TextBox input) : base(input) { target = input; IsHitTestVisible = false; }
        protected override void OnRender(DrawingContext drawingContext)
        {
            var rect = target.GetRectFromCharacterIndex(target.SelectionStart);
            if (rect.IsEmpty || target.SelectionLength > 0) return;
            drawingContext.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
            drawingContext.DrawLine(new Pen(target.TryFindResource("Accent") as Brush ?? Brushes.CornflowerBlue, 1.8), new Point(rect.X, rect.Top), new Point(rect.X, rect.Bottom));
            drawingContext.Pop();
        }
    }
}
