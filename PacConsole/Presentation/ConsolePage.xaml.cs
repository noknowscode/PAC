namespace PacConsole.Presentation;

public sealed partial class ConsolePage : Page
{
    public ConsolePage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// A desktop or Windows window can go full screen. A phone or tablet app already fills the screen, and a
    /// browser tab has its own full-screen key, so there the button is not offered at all.
    /// </summary>
    private static bool FullScreenAvailable =>
#if __DESKTOP__ || WINDOWS
        true;
#else
        false;
#endif

    // xaml-lint: allow codebehind - the full-screen button sits inside the FeedView template and exists only where a window can go full screen.
    private void OnFullScreenLoaded(object sender, RoutedEventArgs e)
    {
        if (!FullScreenAvailable && sender is UIElement button) button.Visibility = Visibility.Collapsed;
    }

    // xaml-lint: allow codebehind - full screen changes the window's presenter, which has no XAML surface.
    private void OnFullScreenClick(object sender, RoutedEventArgs e)
    {
#if __DESKTOP__ || WINDOWS
        if (App.Shell?.AppWindow is not { } window) return;
        window.SetPresenter(window.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen
            ? Microsoft.UI.Windowing.AppWindowPresenterKind.Default
            : Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
#endif
    }
}
