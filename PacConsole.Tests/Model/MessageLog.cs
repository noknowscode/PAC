using Uno.Extensions.Reactive;
using Uno.Extensions.Reactive.Core;

namespace PacConsole.Tests.Model;

/// <summary>One message a feed sent, reduced to what the tests read.</summary>
internal sealed record Seen<T>(bool HasData, T? Data, bool IsNone, Exception? Error);

/// <summary>
/// Every message a feed sends while the test runs, in order: the way a FeedView sees the stream (data, none, error),
/// and the way the tests check that two states never disagree in between.
/// </summary>
internal sealed class MessageLog<T> : IAsyncDisposable
{
    private readonly List<Seen<T>> _seen = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _reading;

    public MessageLog(IFeed<T> feed, object owner)
    {
        var context = SourceContext.GetOrCreate(owner);
        _reading = Task.Run(async () =>
        {
            try
            {
                await foreach (var message in feed.GetSource(context, _stop.Token).WithCancellation(_stop.Token))
                {
                    var current = message.Current;
                    if (current.IsTransient) continue;
                    var data = current.Data;
                    var has = data.IsSome(out var value);
                    var seen = new Seen<T>(has, has ? value : default, data.IsNone(), current.Error);
                    lock (_seen) _seen.Add(seen);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private Seen<T>[] Snapshot()
    {
        lock (_seen) return [.. _seen];
    }

    public bool Any(Func<Seen<T>, bool> match) => Snapshot().Any(match);

    public IEnumerable<Seen<T>> Where(Func<Seen<T>, bool> match) => Snapshot().Where(match);

    public IEnumerable<TOut> Select<TOut>(Func<Seen<T>, TOut> map) => Snapshot().Select(map);

    public int Count(Func<Seen<T>, bool> match) => Snapshot().Count(match);

    public Seen<T> Last() => Snapshot()[^1];

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        try
        {
            await _reading.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
            // A source that ignores cancellation is left to the test host; the test's result stands.
        }
        _stop.Dispose();
    }
}

internal static class MessageLog
{
    public static MessageLog<T> Of<T>(IFeed<T> feed, object owner) => new(feed, owner);
}
