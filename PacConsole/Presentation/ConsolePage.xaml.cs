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
        var light = toggle.IsChecked == true;
        Styles.ConsoleTheme.Apply(light, XamlRoot?.Content as FrameworkElement ?? this);
        if (toggle.Content is TextBlock label) label.Text = light ? "DARK" : "LIGHT";
        AutomationProperties.SetName(toggle, light ? "Dark theme" : "Light theme");
        // The cyan grid backdrop belongs to the dark console; the light one sits on plain slate.
        Backdrop.Visibility = light ? Visibility.Collapsed : Visibility.Visible;
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
