namespace PacConsole.Presentation;

/// <summary>The header band: the station, its output against target, its mode and data age, and the menu. Markup only: it binds to the console FeedView's state it sits in (see ConsoleHeader.xaml).</summary>
public sealed partial class ConsoleHeader : UserControl
{
    public ConsoleHeader()
    {
        InitializeComponent();
    }
}
