namespace Groma;

public enum Direction { Right, Down }

/// <summary>Distances are in grid steps.</summary>
public sealed record Options
{
    /// <summary>The way links flow in a fresh layout.</summary>
    public Direction Direction { get; init; } = Direction.Right;
    /// <summary>The least clear space kept between two boxes, and around the edge of the sheet.</summary>
    public int BoxGap { get; init; } = 4;
    /// <summary>The clear space between a container's outline and what it holds.</summary>
    public int ContainerPadding { get; init; } = 2;
}

/// <summary>An incremental layout could not place something without moving a box that must stay put.</summary>
public sealed class ResurveyNeededException(string message) : Exception(message);

public static class Plan
{
    /// <summary>A fresh layout. May place every box anywhere.</summary>
    public static Layout Survey(Graph graph, Options? options = null)
    {
        var (g, o) = (Canonical(graph), options ?? new Options());
        return Finish(g, new Placer(g, o).Survey(), o, null);
    }

    /// <summary>Surviving boxes keep their positions, new boxes are placed, every line is re-routed.</summary>
    public static Layout Update(Graph graph, Layout previous, Options? options = null)
    {
        var (g, o) = (Canonical(graph), options ?? new Options());
        return Finish(g, new Placer(g, o).Update(previous), o, previous);
    }

    /// <summary>Adds routes for extra links. Boxes and existing routes come back unchanged.</summary>
    public static Layout RouteExtra(Layout layout, IReadOnlyList<Link> extra, Options? options = null)
    {
        GraphCheck.Run(new Graph([.. layout.Boxes.Select(b => new Box(b.Id, Math.Max(b.Width, 1), Math.Max(b.Height, 1), b.Parent))], extra));
        var taken = layout.Links.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var l in extra)
            if (taken.Contains(l.Id)) throw new GromaInputException($"Link '{l.Id}' is already drawn on this layout.");

        var router = new Router(layout.Width, layout.Height, layout.Boxes);
        foreach (var l in layout.Links) router.Occupy(l.Points);
        var routes = router.RouteAll(extra);
        var labels = Labels.Place(layout.Width, layout.Height, layout.Boxes, [.. extra.Select(l => (l, (IReadOnlyList<Point>)routes[l.Id]))], layout.Links);
        return layout with { Links = Sorted(layout.Links.Concat(extra.Select(l => ToRoute(l, routes[l.Id], labels)))) };
    }

    // Input order carries no meaning: everything downstream sees boxes and links in id order.
    static Graph Canonical(Graph graph)
    {
        GraphCheck.Run(graph);
        return new Graph(
            [.. graph.Boxes.OrderBy(b => b.Id, StringComparer.Ordinal)],
            [.. graph.Links.OrderBy(l => l.Id, StringComparer.Ordinal)]);
    }

    static Layout Finish(Graph g, Dictionary<string, Rect> at, Options o, Layout? previous)
    {
        var boxes = g.Boxes.Select(b => new PlacedBox(b.Id, at[b.Id].X, at[b.Id].Y, at[b.Id].Width, at[b.Id].Height, b.Parent)).ToList();
        // The sheet only ever grows, and only right and down, so a grid reference never changes meaning.
        int width = Math.Max(previous?.Width ?? 0, boxes.Select(b => b.X + b.Width + o.BoxGap).DefaultIfEmpty(0).Max());
        int height = Math.Max(previous?.Height ?? 0, boxes.Select(b => b.Y + b.Height + o.BoxGap).DefaultIfEmpty(0).Max());
        var routes = new Router(width, height, boxes).RouteAll(g.Links);
        var labels = Labels.Place(width, height, boxes, [.. g.Links.Select(l => (l, (IReadOnlyList<Point>)routes[l.Id]))], []);
        return new Layout(width, height, boxes, [.. g.Links.Select(l => ToRoute(l, routes[l.Id], labels))]);
    }

    static Route ToRoute(Link l, List<Point> points, Dictionary<string, Rect> labels) =>
        new(l.Id, l.From, l.To, points, l.FromEdge, l.ToEdge, labels.TryGetValue(l.Id, out var label) ? label : null);

    static List<T> Sorted<T>(IEnumerable<T> items) where T : class =>
        [.. items.OrderBy(i => i is Route r ? r.Id : ((PlacedBox)(object)i).Id, StringComparer.Ordinal)];
}
