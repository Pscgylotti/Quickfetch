using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;

namespace Quickfetch;

internal sealed class GestureNoticeWindow : Window
{
    private readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly TextBlock _message = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
    internal GestureNoticeWindow()
    {
        Title = "Quickfetch"; Width = 360; SizeToContent = SizeToContent.Height; MaxHeight = 240;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false;
        Focusable = false; IsHitTestVisible = false; Topmost = true; Background = Brushes.Transparent;
        SetResourceReference(ForegroundProperty, "Ink");
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0), CornerRadius = new CornerRadius(10) });
        var stack = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };
        var title = new TextBlock { Text = Loc.T("Quickfetch · 手势未执行"), FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); stack.Children.Add(title); stack.Children.Add(_message);
        var border = new Border { Child = stack, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), ClipToBounds = true };
        border.SetResourceReference(Border.BackgroundProperty, "Surface"); border.SetResourceReference(Border.BorderBrushProperty, "Line"); Content = border;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(handle, -20, new IntPtr(GetWindowLongPtr(handle, -20).ToInt64() | 0x080000A0));
            WindowBackdrop.Apply(handle);
        };
        SizeChanged += (_, _) => Place();
        _hide.Tick += (_, _) =>
        {
            _hide.Stop();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250)) { FillBehavior = FillBehavior.Stop };
            fade.Completed += (_, _) => Close(); BeginAnimation(OpacityProperty, fade);
        };
        Closed += (_, _) => _hide.Stop();
    }
    internal void Display(string message)
    {
        _hide.Stop(); BeginAnimation(OpacityProperty, null); _message.Text = message;
        if (!IsVisible) Show(); Place(); _hide.Start();
    }
    private void Place()
    {
        var area = SystemParameters.WorkArea; Left = Math.Max(area.Left, area.Right - Width - 16); Top = Math.Max(area.Top, area.Bottom - ActualHeight - 16);
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
