using System.Collections;

namespace PacConsole.Controls;

/// <summary>
/// Fills a panel with one presenter per item of <c>Slots.ItemsSource</c>, each showing <c>Slots.ItemTemplate</c>.
/// The console view is recomposed with every snapshot, so its lists are new every 2 s. An ItemsControl would
/// rebuild every row each time, dropping a press in progress, keyboard focus and a trend chart's canvas; here a new
/// list only moves each item into the presenter already at its index, so a row's element lives as long as the list
/// reaches it. Presenters are added as a list grows and removed from the end as it shrinks. In a Grid with
/// columns the items fill its cells row by row.
/// </summary>
// xaml-lint: allow builtin - ItemsControl and ItemsRepeater rebuild their rows when the source list is replaced, which this view does every snapshot.
public static class Slots
{
    public static object? GetItemsSource(DependencyObject obj) => obj.GetValue(ItemsSourceProperty);

    public static void SetItemsSource(DependencyObject obj, object? value) => obj.SetValue(ItemsSourceProperty, value);

    /// <summary>The items: any list (the view's ImmutableArrays).</summary>
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.RegisterAttached("ItemsSource", typeof(object), typeof(Slots), new PropertyMetadata(null, OnChanged));

    public static DataTemplate? GetItemTemplate(DependencyObject obj) => (DataTemplate?)obj.GetValue(ItemTemplateProperty);

    public static void SetItemTemplate(DependencyObject obj, DataTemplate? value) => obj.SetValue(ItemTemplateProperty, value);

    /// <summary>The template each item shows in.</summary>
    public static readonly DependencyProperty ItemTemplateProperty =
        DependencyProperty.RegisterAttached("ItemTemplate", typeof(DataTemplate), typeof(Slots), new PropertyMetadata(null, OnChanged));

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Panel panel || GetItemTemplate(panel) is not { } template) return;
        var items = GetItemsSource(panel) is IEnumerable source ? source.Cast<object>().ToList() : [];
        var children = panel.Children;
        while (children.Count > items.Count) children.RemoveAt(children.Count - 1);
        while (children.Count < items.Count)
        {
            var slot = new ContentPresenter
            {
                ContentTemplate = template,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            };
            Place(panel, slot, children.Count);
            children.Add(slot);
        }
        for (var i = 0; i < items.Count; i++)
        {
            var slot = (ContentPresenter)children[i];
            if (!ReferenceEquals(slot.ContentTemplate, template)) slot.ContentTemplate = template;
            slot.Content = items[i];
        }
    }

    /// <summary>In a Grid with columns, the cell for the item at <paramref name="index"/>, adding Auto rows as needed.</summary>
    private static void Place(Panel panel, FrameworkElement slot, int index)
    {
        if (panel is not Grid { ColumnDefinitions.Count: > 1 } grid) return;
        var columns = grid.ColumnDefinitions.Count;
        while (grid.RowDefinitions.Count <= index / columns) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(slot, index / columns);
        Grid.SetColumn(slot, index % columns);
    }
}
