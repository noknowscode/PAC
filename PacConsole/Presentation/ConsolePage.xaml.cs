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

    /// <summary>
    /// Rail sections are peers inside one page, so switching is view-only visibility state:
    /// Overview shows the console, the other six show a placeholder until they are built.
    /// </summary>
    private void OnSectionChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string section } button) return;

        var overview = section == "OVERVIEW";
        SetVisible(button, "OverviewMain", overview);
        SetVisible(button, "OverviewAside", overview);
        SetVisible(button, "PlaceholderPanel", !overview);
        if (button.FindName("PlaceholderTitle") is TextBlock title) title.Text = section;
        if (button.FindName("Breadcrumb") is TextBlock crumb) crumb.Text = section;
    }

    private static void SetVisible(FrameworkElement scope, string name, bool visible)
    {
        if (scope.FindName(name) is UIElement element)
        {
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnViewportLoaded(object sender, RoutedEventArgs e) => _viewport = sender as PlantViewport;

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key }
            && CameraPresets.All.FirstOrDefault(p => p.Key == key) is { } preset)
        {
            _viewport?.GoTo(preset.Pose);
        }
    }
}
