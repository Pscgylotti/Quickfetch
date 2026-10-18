using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Quickfetch;

public partial class MainWindow
{
    private readonly TranslateTransform _modeSlide = new();
    private bool _modeTransitionActive;
    private int _modeAnimationVersion;
    private void StopModeTransition()
    {
        if (!_modeTransitionActive) return;
        var width = Width; var left = Left;
        ++_modeAnimationVersion; _modeTransitionActive = false;
        BeginAnimation(WidthProperty, null); BeginAnimation(LeftProperty, null);
        Width = width; Left = left;
        ResultsSurface.BeginAnimation(OpacityProperty, null); ResultsSurface.Opacity = 1;
        _modeSlide.BeginAnimation(TranslateTransform.YProperty, null); _modeSlide.Y = 0;
    }
    private void AnimateModeTransition(double previousWidth, double previousLeft)
    {
        if (!IsVisible || !SystemParameters.ClientAreaAnimation) return;
        var version = ++_modeAnimationVersion;
        _modeTransitionActive = true;
        var duration = TimeSpan.FromMilliseconds(180);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var width = new DoubleAnimation(previousWidth, Width, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
        width.Completed += (_, _) =>
        {
            if (version != _modeAnimationVersion) return;
            _modeTransitionActive = false;
            BeginAnimation(WidthProperty, null); BeginAnimation(LeftProperty, null);
            ResultsSurface.BeginAnimation(OpacityProperty, null); _modeSlide.BeginAnimation(TranslateTransform.YProperty, null);
            ScheduleBackdrop();
        };
        BeginAnimation(WidthProperty, width);
        BeginAnimation(LeftProperty, new DoubleAnimation(previousLeft, Left, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop });
        ResultsSurface.BeginAnimation(OpacityProperty, new DoubleAnimation(.2, 1, TimeSpan.FromMilliseconds(140)) { FillBehavior = FillBehavior.Stop });
        _modeSlide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(_fileMode ? 8 : -8, 0, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop });
    }
}
