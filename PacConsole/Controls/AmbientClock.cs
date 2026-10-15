namespace PacConsole.Controls;

/// <summary>
/// The one clock for decorative, always-on motion: the status-dot pulse, the condenser fans and the
/// pipe flow marks. One shared 20 fps <see cref="DispatcherTimer"/>, so every subscriber updates in
/// the same tick and the window renders one frame for all of them. It runs only while someone listens.
/// <para>
/// Why a timer: an element drawn with <c>SKCanvasElement</c> redraws only on <c>Invalidate()</c>, and
/// a held <c>CompositionTarget.Rendering</c> subscription forces frames at display rate for as long
/// as it is held (skiasharp-uno, Performance; uno-runtime-gotchas, Performance and frame pacing).
/// Rendering stays the clock only while the camera or a lift is moving.
/// </para>
/// </summary>
public static class AmbientClock
{
    /// <summary>50 ms: smooth for a turning fan and a dash moving 12 px/s, a third of the frames of 60 Hz.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

    private static readonly List<Action> Listeners = [];
    private static DispatcherTimer? _timer;

    public static void Subscribe(Action listener)
    {
        if (Listeners.Contains(listener)) return;
        Listeners.Add(listener);
        _timer ??= CreateTimer();
        if (!_timer.IsEnabled) _timer.Start();
    }

    public static void Unsubscribe(Action listener)
    {
        Listeners.Remove(listener);
        if (Listeners.Count == 0) _timer?.Stop();
    }

    private static DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer { Interval = Interval };
        timer.Tick += (_, _) =>
        {
            // A copy: a listener may unsubscribe itself while ticking.
            foreach (var listener in Listeners.ToArray()) listener();
        };
        return timer;
    }
}
