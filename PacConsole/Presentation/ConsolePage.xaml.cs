using Microsoft.UI.Windowing;

namespace PacConsole.Presentation;

public sealed partial class ConsolePage : Page
{
    public ConsolePage()
    {
        InitializeComponent();
    }

    // xaml-lint: allow codebehind - full screen changes the window's presenter, which has no XAML surface.
    private void OnFullScreenClick(object sender, RoutedEventArgs e)
    {
        if (App.Shell?.AppWindow is not { } window) return;
        window.SetPresenter(window.Presenter.Kind == AppWindowPresenterKind.FullScreen
            ? AppWindowPresenterKind.Default
            : AppWindowPresenterKind.FullScreen);
    }
}
