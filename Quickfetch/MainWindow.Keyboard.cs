using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Quickfetch;

public partial class MainWindow
{
    private bool _systemKeyboardPending, _systemKeyboardOwned;
    internal Func<bool, bool>? SystemKeyboardRequest { get; set; }
    internal int TouchCaretIndex(Point point)
    {
        if (Query.Text.Length == 0) return 0;
        // GetCharacterIndexFromPoint returns the hit character, not its nearest insertion edge.
        var hit = Math.Clamp(Query.GetCharacterIndexFromPoint(point, true), 0, Query.Text.Length);
        var boundaries = StringInfo.ParseCombiningCharacters(Query.Text).Append(Query.Text.Length).ToArray();
        var found = Array.BinarySearch(boundaries, hit);
        var center = found >= 0 ? found : Math.Max(0, ~found - 1);
        var best = 0; var distance = double.PositiveInfinity;
        foreach (var position in boundaries.Skip(Math.Max(0, center - 1)).Take(4))
        {
            var rect = Query.GetRectFromCharacterIndex(position);
            if (rect.IsEmpty) continue;
            var dx = rect.X - point.X;
            var dy = point.Y - Math.Clamp(point.Y, rect.Top, rect.Bottom);
            var score = dx * dx + dy * dy;
            if (score < distance) { distance = score; best = position; }
        }
        return best;
    }
    private void RequestSystemKeyboard()
    {
        if (_systemKeyboardPending || _disposed) return;
        _systemKeyboardPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            _systemKeyboardPending = false;
            if (_disposed || !IsVisible || _settings.UseBuiltInKeyboard) return;
            FocusSearch();
            if (SystemKeyboardRequest is { } request) { _systemKeyboardOwned = request(true); return; }
            if (SystemTouchKeyboard.Request(new WindowInteropHelper(this).Handle, true, out var error)) _systemKeyboardOwned = true;
            else SetMessage(string.IsNullOrEmpty(error) ? "Windows 未打开系统触摸键盘；请检查系统触摸键盘设置。" : "系统键盘不可用：" + error);
        });
    }
    private void HideSystemKeyboard()
    {
        if (!_systemKeyboardOwned) return;
        _systemKeyboardOwned = false;
        if (SystemKeyboardRequest is { } request) { request(false); return; }
        SystemTouchKeyboard.Request(new WindowInteropHelper(this).Handle, false, out _);
    }
    private void BuiltInKeyboard_Click(object sender, RoutedEventArgs e) => SetKeyboardPreference(BuiltInKeyboardCheck.IsChecked == true, persist: true);
    internal void SetKeyboardPreference(bool useBuiltIn, bool persist = false, bool showKeyboard = true)
    {
        var previous = _settings.UseBuiltInKeyboard;
        try
        {
            _settings.UseBuiltInKeyboard = useBuiltIn;
            if (persist) _settings.Save();
            BuiltInKeyboardCheck.IsChecked = useBuiltIn;
            if (_settings.UseBuiltInKeyboard) HideSystemKeyboard();
            SetTouchKeyboard(showKeyboard); FocusSearch();
        }
        catch (Exception ex) { _settings.UseBuiltInKeyboard = previous; BuiltInKeyboardCheck.IsChecked = previous; SetMessage(ex.Message); }
    }
}
