using Microsoft.UI.Xaml.Automation;
using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;
using PacConsole.Core.Solids;
using PacConsole.Styles;

namespace PacConsole.Controls;

/// <summary>
/// The plant's floating unit tags: one real, focusable Button per unit on a Canvas over the drawing, laid out at the
/// size each measures with its label (code, name when selected, "Alert" when in alert) so none overlap, and styled as a
/// plate in the ground colour with the accent or alert outline. The leaders are drawn with the plant from
/// <see cref="Placements"/>.
/// </summary>
internal sealed class TagOverlay
{
    private readonly Canvas _layer;
    private readonly Dictionary<UnitId, Button> _tags = [];
    private readonly List<TagRequest> _requests = new(UnitCatalog.All.Length);

    /// <param name="layer">The canvas the tags live on.</param>
    /// <param name="select">A tag was clicked.</param>
    /// <param name="remeasured">A tag measured differently (its font loaded): lay out again.</param>
    public TagOverlay(Canvas layer, Action<UnitId> select, Action remeasured)
    {
        _layer = layer;
        foreach (var unit in UnitCatalog.All)
        {
            var tag = new Button
            {
                Style = (Style)Application.Current.Resources["TagButton"],
                Tag = unit.Id,
            };
            AutomationProperties.SetName(tag, $"Select {unit.Code} {unit.Name}");
            tag.Click += (_, _) => select(unit.Id);
            tag.SizeChanged += (_, _) => remeasured();
            _tags[unit.Id] = tag;
            layer.Children.Add(tag);
        }
    }

    /// <summary>Where each tag sits and where its leader meets its unit, from the last layout.</summary>
    public ImmutableArray<TagPlacement> Placements { get; private set; } = [];

    /// <summary>True when <paramref name="d"/> is a tag or inside one (a press there clicks the tag, not the plant).</summary>
    public bool Contains(DependencyObject? d)
    {
        for (; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (ReferenceEquals(d, _layer)) return true;
        }
        return false;
    }

    /// <summary>
    /// Lays the tags out over the drawing at <paramref name="pose"/>, with the same projection the drawing uses, so
    /// leaders land on their units at any pose. Only what changed is touched: a new label re-measures its button.
    /// </summary>
    public void Layout(CameraPose pose, IReadOnlyDictionary<UnitId, float> lift, UnitId? selected, ImmutableArray<UnitId> alerting, PlantSystem system, float width, float height)
    {
        var projection = PlantFraming.ViewProjection(pose, width, height);
        var tagHeight = 0f;
        _requests.Clear();
        foreach (var u in UnitCatalog.All)
        {
            var tag = _tags[u.Id];
            var label = Label(u, selected == u.Id, alerting.Contains(u.Id));
            if (!Equals(tag.Content, label)) tag.Content = label;
            tag.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            tagHeight = Math.Max(tagHeight, (float)tag.DesiredSize.Height);
            var anchor = PlantSolids.Default.Anchors[u.Id];
            _requests.Add(new TagRequest(u.Id, projection.Project(anchor with { Y = anchor.Y + lift[u.Id] }), (float)tag.DesiredSize.Width));
        }
        Placements = TagLayout.Solve(_requests, width, Math.Max(tagHeight, TagLayout.Height));

        foreach (var p in Placements)
        {
            var tag = _tags[p.Unit];
            var isAlert = alerting.Contains(p.Unit);
            var isSelected = selected == p.Unit;
            Canvas.SetLeft(tag, p.Left);
            Canvas.SetTop(tag, p.Top);
            ApplyState(tag, isSelected, isAlert);
            // Tags of equipment outside the system recede with their drawing.
            var opacity = isSelected || PlantSystems.Contains(system, p.Unit, alerting) ? 1.0 : 0.4;
            if (tag.Opacity != opacity) tag.Opacity = opacity;
        }
    }

    /// <summary>The callout text: the code alone, the name with it when selected, and the word "Alert" when in alert.</summary>
    internal static string Label(UnitInfo unit, bool selected, bool alert) =>
        (selected, alert) switch
        {
            (true, true) => $"{unit.Code} · {unit.Name} · Alert",
            (true, false) => $"{unit.Code} · {unit.Name}",
            (false, true) => $"{unit.Code} · Alert",
            _ => unit.Code,
        };

    private static void ApplyState(Button tag, bool selected, bool alert)
    {
        var key = selected ? (alert ? "SelectedAlert" : "Selected") : alert ? "Alert" : "Normal";
        if (Equals(tag.DataContext, key)) return; // avoid restyling every frame
        tag.DataContext = key;
        // A plate in the ground colour (so the label reads over line work). The selected plate takes the
        // slate-teal outline, an alerting one the orange outline, and its label carries the word.
        tag.Background = ThemeBrushes.Get("OverlayBrush");
        tag.BorderThickness = new Thickness(selected || alert ? 1 : 0);
        tag.BorderBrush = ThemeBrushes.Get(alert ? "AlertBorderBrush" : selected ? "AccentBrush" : "OverlayBrush");
        tag.Foreground = ThemeBrushes.Get(alert ? "AlertTextBrush" : selected ? "AccentBrush" : "TextPrimaryBrush");
    }
}
