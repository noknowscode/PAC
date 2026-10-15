using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;
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

    // xaml-lint: allow codebehind - theme switch is view-only glue: it repaints app resources and the root element theme.
    private void OnThemeClick(object sender, RoutedEventArgs e)
    {
        // The toggle sits inside the FeedView template, so it arrives as the sender, not a field.
        if (sender is not ToggleButton toggle) return;
        var dark = toggle.IsChecked == true;
        Styles.ConsoleTheme.Apply(dark, XamlRoot?.Content as FrameworkElement ?? this);
        if (toggle.Content is TextBlock label) label.Text = dark ? "Light" : "Dark";
        AutomationProperties.SetName(toggle, dark ? "Light theme" : "Dark theme");
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
