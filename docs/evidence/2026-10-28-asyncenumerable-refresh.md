# Gotcha: `Feed.AsyncEnumerable` cannot be refreshed, so FeedView Retry is dead after a stream fault

Measured 2026-10-28. Uno.Sdk 6.7.30, Uno.Extensions.Reactive 7.3.6, `net10.0-desktop` on Linux X11 (Xvfb).

## Symptom

`ConsoleModel.View` combined `Snapshot => Feed.AsyncEnumerable(_telemetry.Stream)` with several states. When the stream threw:

- `FeedView` showed its ErrorTemplate.
- The Retry button there (`Command="{Binding Refresh, ElementName=ConsoleFeed}"`) greyed out after one click and the page never recovered.

There was no error and no log line. Captures: `docs/audit/2026-10-28/fault-error-state.jpg`, `retry-dead-before.jpg`.

## Why (source)

Decompiled `Uno.Extensions.Reactive.Sources.AsyncEnumerableFeed<T>.GetSource` (7.3.6):

- On an exception it yields one message with `Error(ex)` and then `yield break`, so the source is finished.
- It never reads `SourceContext` requests, so a refresh request is never answered with a token. That is why the Refresh command stays "executing" (greyed out).

The docs' architecture page describes refresh tokens for `AsyncFeed` only.

## Repro

1. A model with `IFeed<T> X => Feed.AsyncEnumerable(Stream)`, where `Stream` yields twice and then throws.
2. `FeedView Source="{Binding X}"` with an ErrorTemplate Retry bound to `{Binding Refresh, ElementName=...}`.
3. Click Retry: the button disables and the error stays.

In this app: `PAC_TELEMETRY_FAULT=4 dotnet run --project PacConsole -f net10.0-desktop` (Debug).

## Fix that worked

Build the feed with `Feed.Create(Func<CancellationToken, IAsyncEnumerable<Message<T>>>)`:

1. Forward each item as `message.With().Data(item).Error(null)`.
2. On an exception, emit `message.With().Error(ex)` and keep the enumerator alive.
3. Wait for a model command (`RetryTelemetry`) to complete a `TaskCompletionSource`, then resubscribe to the source.

Bind Retry to the model command through the FeedView: `{Binding DataContext.RetryTelemetry, ElementName=ConsoleFeed}`.

Proven: after Retry the console comes back and snapshots keep advancing (`retry-recovered.jpg`; snapshot time 16:05:57 → 16:06:03).

Code: `PacConsole/Presentation/ConsoleModel.cs` (`Snapshot`, `Telemetry`, `RetryTelemetry`).

## Rule

`FeedView.Refresh` re-runs only feeds that answer refresh requests (`Feed.Async`, and operators over them). A stream feed needs its own restart path.
