using System.Windows.Input;

namespace PacConsole.Presentation;

public sealed partial class ConsolePage : Page
{
    public ConsolePage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Toggles the window between full screen and its normal presenter. It changes the window, which is view glue,
    /// not console state, so it lives on the page; the header's templates reach it with an AncestorBinding.
    /// </summary>
    public ICommand ToggleFullScreen { get; } = new FullScreenCommand();

    /// <summary>
    /// A desktop or Windows window can go full screen. A phone or tablet app already fills the screen, and a
    /// browser tab has its own full-screen key, so there the button is not offered at all.
    /// </summary>
    public Visibility FullScreenVisibility =>
#if __DESKTOP__ || WINDOWS
        Visibility.Visible;
#else
        Visibility.Collapsed;
#endif

    // xaml-lint: allow codebehind - full screen changes the window's presenter, which has no XAML surface.
    private sealed class FullScreenCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
#if __DESKTOP__ || WINDOWS
            if (App.Shell?.AppWindow is not { } window) return;
            window.SetPresenter(window.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen
                ? Microsoft.UI.Windowing.AppWindowPresenterKind.Default
                : Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
#endif
        }
    }
}
