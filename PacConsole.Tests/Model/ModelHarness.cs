using Microsoft.Extensions.Time.Testing;
using PacConsole.Core.Persistence;
using PacConsole.Core.Telemetry;
using Uno.Extensions.Reactive;
using PacConsole.Presentation;
using PacConsole.Services;
using Uno.Extensions.Toolkit;

namespace PacConsole.Tests.Model;

/// <summary>
/// The page model over the real simulated telemetry, on a fake clock: a snapshot every 2 s of fake time, so the
/// tests step the plant exactly. MVUX's states and commands run as they do under the page.
/// </summary>
internal sealed class ModelHarness : IAsyncDisposable
{
    public FakeTimeProvider Time { get; }

    public SimulatedTelemetry Telemetry { get; }

    public FakeThemes Themes { get; }

    public FakeJournal Journal { get; }

    public FakeFullScreen FullScreen { get; } = new();

    public ConsoleModel Model { get; }

    /// <param name="journal">A real journal for relaunch tests; by default, an in-memory one over <paramref name="restored"/>.</param>
    public ModelHarness(
        AppTheme theme = AppTheme.System,
        ITelemetryService? telemetry = null,
        OperatorRecord? restored = null,
        FakeTimeProvider? time = null,
        IOperatorJournal? journal = null)
    {
        Time = time ?? new(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));
        Journal = new FakeJournal(restored);
        var used = journal ?? Journal;
        // As the app's DI does: the simulated plant resumes at the saved tick plus the time since.
        Telemetry = new SimulatedTelemetry(Time, used.Restored?.ResumeTick(Time.GetUtcNow(), SimulatedTelemetry.TickInterval) ?? Simulation.StartTick);
        Themes = new FakeThemes(theme);
        Model = new ConsoleModel(telemetry ?? Telemetry, Time, Themes, used, FullScreen);
    }

    /// <summary>Advances the fake clock one telemetry interval and lets the stream deliver.</summary>
    public async Task TickAsync(int ticks = 1)
    {
        for (var i = 0; i < ticks; i++)
        {
            Time.Advance(TimeSpan.FromSeconds(2));
            await Task.Delay(20);
        }
    }

    /// <summary>
    /// A feed's current value, as a binding would read it; fails rather than hangs when none comes. A state reaches a new
    /// subscriber a moment after the command that set it returns, so this reads until two reads 25 ms apart agree (a
    /// composed view, a new record each time, gets four tries).
    /// </summary>
    public Task<T?> Read<T>(IFeed<T> feed) where T : notnull => Within(Task.Run(async () =>
    {
        var value = await feed;
        for (var i = 0; i < 4; i++)
        {
            await Task.Delay(25);
            var again = await feed;
            if (EqualityComparer<T?>.Default.Equals(again, value)) return again;
            value = again;
        }
        return value;
    }));

    /// <summary>Fails a test that would otherwise hang on a feed or command.</summary>
    public static async Task<T> Within<T>(Task<T> task) => await task.WaitAsync(TimeSpan.FromSeconds(10));

    /// <summary>Polls until <paramref name="condition"/> holds (MVUX delivers on its own schedule), or fails.</summary>
    public static async Task UntilAsync(Func<Task<bool>> condition, string because)
    {
        for (var i = 0; i < 200; i++)
        {
            if (await Within(condition())) return;
            await Task.Delay(25);
        }
        Assert.Fail($"Timed out waiting until {because}");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Records what the model asked of the theme service.</summary>
internal sealed class FakeThemes(AppTheme theme) : IThemeService
{
    public List<AppTheme> Requests { get; } = [];

    public AppTheme Theme { get; private set; } = theme;

    public bool IsDark => Theme == AppTheme.Dark;

    public event EventHandler<AppTheme>? ThemeChanged;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task<bool> SetThemeAsync(AppTheme theme)
    {
        Requests.Add(theme);
        Theme = theme;
        ThemeChanged?.Invoke(this, theme);
        return Task.FromResult(true);
    }
}

/// <summary>Counts the model's full-screen switches.</summary>
internal sealed class FakeFullScreen : IFullScreen
{
    public int Toggles { get; private set; }

    public bool IsSupported => true;

    public Task ToggleAsync()
    {
        Toggles++;
        return Task.CompletedTask;
    }
}

/// <summary>Keeps the records the model saves, in memory; can be made to fail.</summary>
internal sealed class FakeJournal(OperatorRecord? restored) : IOperatorJournal
{
    public OperatorRecord? Restored { get; } = restored;

    public List<(OperatorRecord Record, long Version)> Saved { get; } = [];

    public bool Fails { get; set; }

    public OperatorRecord? Last => Saved.Count == 0 ? null : Saved[^1].Record;

    public ValueTask<bool> SaveAsync(OperatorRecord record, long version, CancellationToken ct)
    {
        if (Fails) return ValueTask.FromResult(false);
        lock (Saved) Saved.Add((record, version));
        return ValueTask.FromResult(true);
    }
}
