using Microsoft.UI.Windowing;
using PacConsole.Controls;
using PacConsole.Core.Geometry;

namespace PacConsole.Presentation;

public sealed partial class ConsolePage : Page
{
    private PlantViewport? _viewport;

    public ConsolePage()
    {
        InitializeComponent();
    }

    // xaml-lint: allow codebehind - keeps the viewport, which sits inside the FeedView template, for the Reset menu item.
    private void OnViewportLoaded(object sender, RoutedEventArgs e) => _viewport = sender as PlantViewport;

    // xaml-lint: allow codebehind - the camera is view state owned by PlantViewport, not domain data.
    private void OnResetViewClick(object sender, RoutedEventArgs e) => _viewport?.GoTo(CameraPose.Iso);

    // xaml-lint: allow codebehind - full screen changes the window's presenter, which has no XAML surface.
    private void OnFullScreenClick(object sender, RoutedEventArgs e)
    {
        if (App.Shell?.AppWindow is not { } window) return;
        window.SetPresenter(window.Presenter.Kind == AppWindowPresenterKind.FullScreen
            ? AppWindowPresenterKind.Default
            : AppWindowPresenterKind.FullScreen);
    }
}
