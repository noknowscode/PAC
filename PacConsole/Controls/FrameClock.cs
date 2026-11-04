namespace PacConsole.Controls;

/// <summary>
/// When a Skia view draws: at display rate (CompositionTarget.Rendering) while something moves, on the shared 20 fps
/// ambient clock while only decorative motion runs, and not at all while nobody can see it (the view scrolled out of the
/// page, or the window hidden). Each tick hands the owner the seconds since the last one, capped so a hitch (GC, a
/// telemetry refresh) never makes the camera or a lift jump. Time comes from a Stopwatch, not the wall clock.
/// </summary>
/// <remarks>
/// A 16 ms DispatcherTimer ticked on Windows' 15.6 ms timer grid, alternating 16 and 31 ms frames (measured p90 31 ms),
/// which read as judder; hence Rendering while moving. A held Rendering subscription would force display-rate frames
/// forever; hence the ambient clock while still.
/// </remarks>
internal sealed class FrameClock
{
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly FrameworkElement _host;
    private readonly Action<double> _tick;
    private readonly Action _changed;
    private readonly Action _ambient;
    private bool _looping;
    private bool _ambientOn;
    private double _last;

    /// <param name="host">The view: its viewport in the page decides whether it is seen.</param>
    /// <param name="tick">A frame, with the seconds since the last one (at most 0.1).</param>
    /// <param name="changed">Whether the view is seen changed; the owner should call <see cref="Run"/> again.</param>
    public FrameClock(FrameworkElement host, Action<double> tick, Action changed)
    {
        _host = host;
        _tick = tick;
        _changed = changed;
        _ambient = Tick;
        host.EffectiveViewportChanged += OnEffectiveViewportChanged;
        host.Loaded += (_, _) => AmbientClock.HostVisibilityChanged += changed;
        host.Unloaded += (_, _) =>
        {
            Stop();
            AmbientClock.HostVisibilityChanged -= changed;
        };
    }

    /// <summary>The clock frames and tweens are timed on, in seconds.</summary>
    public double Now => _clock.Elapsed.TotalSeconds;

    /// <summary>Some of the view is inside the page's scroll viewport.</summary>
    public bool InView { get; private set; } = true;

    /// <summary>Seen: in view, in a window that is shown.</summary>
    public bool Seen => InView && AmbientClock.HostVisible;

    /// <summary>
    /// Runs display-rate frames while <paramref name="moving"/> and seen; else the ambient clock while seen and
    /// <paramref name="ambient"/> (decorative motion on, the view loaded); else nothing.
    /// </summary>
    public void Run(bool moving, bool ambient)
    {
        moving = Seen && moving;
        if (moving) StartLoop(); else StopLoop();
        if (!moving && Seen && ambient)
        {
            if (!_ambientOn) _last = Now;
            _ambientOn = true;
            AmbientClock.Subscribe(_ambient);
        }
        else
        {
            _ambientOn = false;
            AmbientClock.Unsubscribe(_ambient);
        }
    }

    private void Stop()
    {
        StopLoop();
        _ambientOn = false;
        AmbientClock.Unsubscribe(_ambient);
    }

    private void StartLoop()
    {
        if (_looping) return;
        _looping = true;
        _last = Now;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopLoop()
    {
        if (!_looping) return;
        _looping = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, object e) => Tick();

    private void Tick()
    {
        var now = Now;
        var dt = Math.Min(now - _last, 0.1);
        _last = now;
        _tick(dt);
    }

    private void OnEffectiveViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs e)
    {
        // The viewport arrives in the view's coordinates (empty once it is scrolled out): in view while they overlap.
        var v = e.EffectiveViewport;
        var inView = v.Width > 0 && v.Height > 0 && v.X < _host.ActualWidth && v.Y < _host.ActualHeight && v.Right > 0 && v.Bottom > 0;
        if (inView == InView) return;
        InView = inView;
        _changed();
    }
}
