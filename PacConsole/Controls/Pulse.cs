using Microsoft.UI.Xaml.Media.Animation;

namespace PacConsole.Controls;

/// <summary>
/// Opacity pulse (1 → 0.3 → 1, 1.6 s) for the live dot and the alert chip dot.
/// Job: says "data is live" / draws the eye to an alert. Off when the OS asks for less motion.
/// </summary>
public static class Pulse
{
    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(Pulse), new PropertyMetadata(false, OnChanged));

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true || !AnimationsEnabled()) return;

        element.Loaded += (_, _) =>
        {
            var animation = new DoubleAnimation
            {
                From = 1,
                To = 0.3,
                Duration = TimeSpan.FromMilliseconds(800),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(animation, element);
            Storyboard.SetTargetProperty(animation, nameof(UIElement.Opacity));
            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            storyboard.Begin();
            element.Unloaded += (_, _) => storyboard.Stop();
        };
    }

    private static bool AnimationsEnabled()
    {
        try { return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled; }
        catch { return true; }
    }
}
