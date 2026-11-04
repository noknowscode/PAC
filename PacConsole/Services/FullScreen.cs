namespace PacConsole.Services;

/// <summary>The window's full-screen switch, behind the header's Full screen button.</summary>
public interface IFullScreen
{
    /// <summary>
    /// True where a window can go full screen: desktop and WinAppSDK. A phone or tablet app already fills the screen, and
    /// a browser tab has its own full-screen key, so there the button is not offered.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>Switches the window between full screen and its normal presenter.</summary>
    Task ToggleAsync();
}

#if __DESKTOP__ || WINDOWS
/// <summary>
/// The app window's presenter. The window comes from the host builder (<c>IApplicationBuilder.Configure</c> hands it
/// over), and MVUX runs commands off the UI thread, so the change is queued on the window's own thread.
/// </summary>
internal sealed class WindowFullScreen(Window window) : IFullScreen
{
    public bool IsSupported => true;

    public Task ToggleAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = window.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var appWindow = window.AppWindow;
                appWindow.SetPresenter(appWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen
                    ? Microsoft.UI.Windowing.AppWindowPresenterKind.Default
                    : Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
                done.SetResult();
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });
        if (!queued) done.SetException(new InvalidOperationException("The window's thread no longer takes work."));
        return done.Task;
    }
}
#else
/// <summary>Where the platform owns the window (phone, tablet, browser tab): no full-screen switch.</summary>
internal sealed class NoFullScreen : IFullScreen
{
    public bool IsSupported => false;

    public Task ToggleAsync() => Task.CompletedTask;
}
#endif
