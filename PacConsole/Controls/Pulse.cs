namespace PacConsole.Controls;

/// <summary>
/// Opacity pulse (1 → 0.3 → 1, sine, 1.6 s) for the live dot and the alert chip dot.
/// Job: says "data is live" / draws the eye to an alert. Off when the OS asks for less motion.
/// The shared 20 fps <see cref="AmbientClock"/> writes Opacity for every loaded dot (in the same tick
/// as the plant's fans and flow, so they share frames) and stops when none is loaded:
/// a Forever storyboard pumps frames permanently on Skia desktop, and Timeline.AutoReverse is not
/// implemented there (Uno0001), so the storyboard snapped from 0.3 straight back to 1.
/// </summary>
public static class Pulse
{
    private const double PeriodSeconds = 1.6;
    private const double Low = 0.3;

    private static readonly List<UIElement> Dots = [];
    private static readonly DateTimeOffset Start = DateTimeOffset.Now;
    private static readonly Action TickAction = Tick;

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(Pulse), new PropertyMetadata(false, OnChanged));

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true || !AnimationsEnabled()) return;

        // Loaded and Unloaded can repeat (re-parenting), so each one adds or removes the dot.
        element.Loaded += (_, _) => Add(element);
        element.Unloaded += (_, _) => Remove(element);
        if (element.IsLoaded) Add(element);
    }

    private static void Add(UIElement dot)
    {
        if (!Dots.Contains(dot)) Dots.Add(dot);
        AmbientClock.Subscribe(TickAction);
    }

    private static void Remove(UIElement dot)
    {
        Dots.Remove(dot);
        dot.Opacity = 1;
        if (Dots.Count == 0) AmbientClock.Unsubscribe(TickAction);
    }

    private static void Tick()
    {
        var phase = (DateTimeOffset.Now - Start).TotalSeconds / PeriodSeconds * 2 * Math.PI;
        var opacity = Low + (1 - Low) * (0.5 + 0.5 * Math.Cos(phase));
        foreach (var dot in Dots) dot.Opacity = opacity;
    }

    private static bool AnimationsEnabled()
    {
        try { return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled; }
        catch { return true; }
    }
}
