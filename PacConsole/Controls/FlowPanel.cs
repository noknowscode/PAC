using Windows.Foundation;

namespace PacConsole.Controls;

/// <summary>
/// Lays its children out left to right and starts a new line when the next one would pass the available width, so
/// a row of chips (the systems filter, the connected equipment) never clips or scrolls sideways at any window
/// width. Collapsed children take neither space nor a gap.
/// </summary>
// xaml-lint: allow builtin - WinUI has no wrapping panel for children of different widths: VariableSizedWrapGrid sizes every cell like the first, and ItemsRepeater needs an item source, while these are fixed RadioButtons and Buttons with their own content.
public sealed partial class FlowPanel : Panel
{
    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public static readonly DependencyProperty HorizontalSpacingProperty =
        DependencyProperty.Register(nameof(HorizontalSpacing), typeof(double), typeof(FlowPanel), new PropertyMetadata(0.0, OnSpacingChanged));

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    public static readonly DependencyProperty VerticalSpacingProperty =
        DependencyProperty.Register(nameof(VerticalSpacing), typeof(double), typeof(FlowPanel), new PropertyMetadata(0.0, OnSpacingChanged));

    private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((FlowPanel)d).InvalidateMeasure();

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return Flow(availableSize.Width, arrange: false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Flow(finalSize.Width, arrange: true);
        return finalSize;
    }

    /// <summary>Walks the children in order, wrapping before one that would pass <paramref name="width"/>; returns the extent used.</summary>
    private Size Flow(double width, bool arrange)
    {
        double x = 0, y = 0, lineHeight = 0, extent = 0;
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > width)
            {
                y += lineHeight + VerticalSpacing;
                x = 0;
                lineHeight = 0;
            }
            if (arrange) child.Arrange(new Rect(x, y, Math.Min(size.Width, Math.Max(0, width)), size.Height));
            extent = Math.Max(extent, x + size.Width);
            x += size.Width + HorizontalSpacing;
            lineHeight = Math.Max(lineHeight, size.Height);
        }
        return new Size(extent, y + lineHeight);
    }
}
