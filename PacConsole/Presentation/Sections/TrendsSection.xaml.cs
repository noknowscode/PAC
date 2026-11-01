namespace PacConsole.Presentation;

/// <summary>The synchronized trends band: range, one row per series and the shared cursor. Markup only: it binds to the console FeedView's state it sits in (see TrendsSection.xaml).</summary>
public sealed partial class TrendsSection : UserControl
{
    public TrendsSection()
    {
        InitializeComponent();
    }
}
