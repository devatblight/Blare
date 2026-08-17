using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Blight.Blare.App.Controls;

/// <summary>
/// The app's motion vocabulary in one place so everything that moves agrees on
/// timing and easing.
///
/// Durations are deliberately short. This is a control surface: a fader that
/// takes half a second to acknowledge a click reads as broken, not as polished.
/// Everything here animates opacity or a render transform, which the compositor
/// handles off the UI thread — nothing animates layout, which would jank while
/// meters are running.
///
/// Every storyboard stops itself and writes its end value as a plain local value
/// when it finishes. A completed storyboard otherwise keeps hold of the property
/// and silently swallows the next direct assignment, which shows up much later
/// as an element that mysteriously refuses to change.
/// </summary>
internal static class Motion
{
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(110);
    public static readonly TimeSpan Normal = TimeSpan.FromMilliseconds(180);
    public static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(260);

    /// <summary>
    /// When the user has asked Windows to reduce motion, every animation still
    /// runs but lands immediately.
    ///
    /// Honoured by jumping to the end value rather than by skipping the call, so
    /// no caller has to know about it and nothing is left half-animated.
    /// </summary>
    public static bool Reduced { get; set; }

    public static void FadeTo(UIElement element, double opacity, TimeSpan? duration = null)
    {
        var storyboard = new Storyboard();
        storyboard.Children.Add(To(element, "Opacity", opacity, duration ?? Fast));

        Run(storyboard, () => element.Opacity = opacity);
    }

    /// <summary>Fades a layer to a resting opacity and keeps it out of the tree while hidden.</summary>
    public static void ToggleLayer(UIElement layer, bool visible, TimeSpan? duration = null, double shownOpacity = 1)
    {
        if (visible)
        {
            layer.Visibility = Visibility.Visible;
            FadeTo(layer, shownOpacity, duration);
            return;
        }

        var storyboard = new Storyboard();
        storyboard.Children.Add(To(layer, "Opacity", 0, duration ?? Fast));

        Run(storyboard, () =>
        {
            layer.Opacity = 0;
            layer.Visibility = Visibility.Collapsed;
        });
    }

    /// <summary>
    /// Places an element at an offset and lets it glide back to zero. Used for
    /// "settle into place": the caller moves something in layout, then calls this
    /// with the inverse of the jump, so the element appears to stay put and then
    /// travel to its new home rather than teleporting.
    /// </summary>
    public static void SettleFrom(TranslateTransform transform, double fromX, double fromY)
    {
        if (Math.Abs(fromX) < 0.5 && Math.Abs(fromY) < 0.5)
        {
            transform.X = 0;
            transform.Y = 0;
            return;
        }

        transform.X = fromX;
        transform.Y = fromY;

        var storyboard = new Storyboard();
        storyboard.Children.Add(To(transform, "X", 0, Settle));
        storyboard.Children.Add(To(transform, "Y", 0, Settle));

        Run(storyboard, () =>
        {
            transform.X = 0;
            transform.Y = 0;
        });
    }

    public static void ScaleTo(ScaleTransform transform, double scale, TimeSpan? duration = null)
    {
        var storyboard = new Storyboard();
        storyboard.Children.Add(To(transform, "ScaleX", scale, duration ?? Fast));
        storyboard.Children.Add(To(transform, "ScaleY", scale, duration ?? Fast));

        Run(storyboard, () =>
        {
            transform.ScaleX = scale;
            transform.ScaleY = scale;
        });
    }

    /// <summary>
    /// Fades an element up from slightly below, staggered by index for a list.
    /// Takes the transform to drive rather than creating one, so it can share
    /// whatever transform the element already uses for other gestures.
    /// </summary>
    public static void EnterStaggered(UIElement element, TranslateTransform offset, int index)
    {
        element.Opacity = 0;
        offset.Y = 10;

        // Capped so a busy dashboard still finishes appearing promptly.
        var storyboard = new Storyboard
        {
            BeginTime = TimeSpan.FromMilliseconds(Math.Min(index, 6) * 35),
        };

        storyboard.Children.Add(To(element, "Opacity", 1, Normal));
        storyboard.Children.Add(To(offset, "Y", 0, Normal));

        Run(storyboard, () =>
        {
            element.Opacity = 1;
            offset.Y = 0;
        });
    }

    private static void Run(Storyboard storyboard, Action settle)
    {
        if (Reduced)
        {
            settle();
            return;
        }

        storyboard.Completed += (_, _) =>
        {
            storyboard.Stop();
            settle();
        };

        storyboard.Begin();
    }

    private static DoubleAnimation To(DependencyObject target, string property, double value, TimeSpan duration)
    {
        var animation = new DoubleAnimation
        {
            To = value,
            Duration = duration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }
}
