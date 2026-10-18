using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Quickfetch;

// Code-driven transitions for state that templates render through setters: the setter is
// correct on first paint, and these short animations play only on real changes.
internal static class Motion
{
    internal static readonly IEasingFunction Out = new QuadraticEase { EasingMode = EasingMode.EaseOut };
    internal static bool Enabled => SystemParameters.ClientAreaAnimation;
    internal static void Play(IAnimatable target, DependencyProperty property, double from, double to, double milliseconds)
        => target.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = Out, FillBehavior = FillBehavior.Stop });
    // Template transforms may be shared and frozen; give the element its own instance first.
    internal static T Transform<T>(UIElement element) where T : Transform, new()
    {
        if (element.RenderTransform is T own && !own.IsFrozen) return own;
        var created = element.RenderTransform is T frozen ? (T)frozen.Clone() : new T();
        element.RenderTransform = created;
        return created;
    }
}
