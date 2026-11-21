using static Groma.Tests.Draw;

namespace Groma.Tests;

/// <summary>The router, exercised through RouteExtra over hand-placed boxes.</summary>
public class RoutingTests
{
    static Layout Routed(PlacedBox[] boxes, params Link[] links) => Plan.RouteExtra(new Layout(60, 40, boxes, []), links);

    static Link L(string id, string from, string to) => new(id, from, to);

    static void AssertSound(Layout layout)
    {
        var r = Metrics.Measure(layout);
        Assert.Equal((0, 0, 0), (r.ThroughBoxes, r.Diagonals, r.DetachedEnds));
    }

    [Fact]
    public void Two_boxes_facing_each_other_are_joined_by_one_straight_line()
    {
        var layout = Routed([B("a", 4, 10, 8, 4), B("b", 30, 10, 8, 4)], L("l", "a", "b"));
        var route = Assert.Single(layout.Links);
        Assert.Equal(2, route.Points.Count);
        Assert.Equal(18, Metrics.Measure(layout).Length);
        AssertSound(layout);
    }

    [Fact]
    public void A_line_goes_round_a_box_in_its_way()
    {
        var layout = Routed([B("a", 4, 10, 8, 4), B("wall", 20, 4, 6, 16), B("b", 34, 10, 8, 4)], L("l", "a", "b"));
        AssertSound(layout);
        Assert.True(Metrics.Measure(layout).Bends >= 2);
    }

    [Fact]
    public void Two_links_between_the_same_boxes_do_not_lie_on_top_of_each_other()
    {
        var layout = Routed([B("a", 4, 10, 8, 4), B("b", 30, 10, 8, 4)], L("l1", "a", "b"), L("l2", "b", "a"));
        var r = Metrics.Measure(layout);
        Assert.Equal((0, 0), (r.OnTop, r.Crossings));
        AssertSound(layout);
    }

    [Fact]
    public void Many_links_into_one_box_each_get_their_own_connection_point()
    {
        PlacedBox[] boxes = [B("hub", 24, 16, 12, 4), B("n1", 4, 4, 8, 4), B("n2", 44, 4, 8, 4), B("n3", 4, 30, 8, 4), B("n4", 44, 30, 8, 4), B("n5", 24, 4, 8, 4), B("n6", 24, 30, 8, 4)];
        var layout = Routed(boxes, [.. Enumerable.Range(1, 6).Select(i => L($"l{i}", $"n{i}", "hub"))]);
        Assert.Equal(6, layout.Links.Select(l => l.Points[^1]).Distinct().Count());
        Assert.Equal(0, Metrics.Measure(layout).OnTop);
        AssertSound(layout);
    }

    [Fact]
    public void A_route_starts_at_its_from_box_and_ends_at_its_to_box()
    {
        var layout = Routed([B("a", 4, 10, 8, 4), B("b", 30, 24, 8, 4)], L("l", "a", "b"));
        var route = layout.Links[0];
        Assert.InRange(route.Points[0].X, 4, 12);
        Assert.InRange(route.Points[^1].X, 30, 38);
        AssertSound(layout);
    }

    [Fact]
    public void A_link_to_a_sheet_edge_ends_on_that_edge()
    {
        var layout = Routed([B("a", 4, 10, 8, 4)], new Link("out", "a", null, ToEdge: Side.Right), new Link("in", null, "a", FromEdge: Side.Top));
        Assert.Equal(60, layout.Links.Single(l => l.Id == "out").Points[^1].X);
        Assert.Equal(0, layout.Links.Single(l => l.Id == "in").Points[0].Y);
        AssertSound(layout);
    }

    [Fact]
    public void A_line_prefers_to_go_round_a_container_it_has_no_business_in()
    {
        PlacedBox[] boxes = [B("a", 4, 12, 8, 4), B("db", 20, 6, 14, 16), B("t", 22, 10, 10, 4, "db"), B("b", 44, 12, 8, 4)];
        var layout = Routed(boxes, L("l", "a", "b"));
        Assert.Equal(0, Metrics.Measure(layout).ContainerCuts);
        AssertSound(layout);
    }

    [Fact]
    public void A_link_into_a_container_reaches_the_box_inside()
    {
        PlacedBox[] boxes = [B("a", 4, 12, 8, 4), B("db", 20, 6, 14, 16), B("t", 22, 10, 10, 4, "db")];
        var layout = Routed(boxes, L("l", "a", "t"));
        Assert.Equal(0, Metrics.Measure(layout).ContainerCuts);
        AssertSound(layout);
    }

    [Fact]
    public void A_link_to_a_container_stops_at_its_outline()
    {
        PlacedBox[] boxes = [B("a", 4, 12, 8, 4), B("db", 20, 6, 14, 16), B("t", 22, 10, 10, 4, "db")];
        var layout = Routed(boxes, L("l", "a", "db"));
        Assert.Equal(new Point(20, 14), layout.Links[0].Points[^1]);
        AssertSound(layout);
    }

    [Fact]
    public void A_link_from_a_box_to_its_own_container_runs_inside_it()
    {
        PlacedBox[] boxes = [B("db", 20, 6, 20, 16), B("t", 24, 10, 10, 4, "db")];
        var layout = Routed(boxes, L("l", "t", "db"));
        AssertSound(layout);
        Assert.All(layout.Links[0].Points, p => Assert.InRange(p.X, 20, 40));
    }

    [Fact]
    public void A_line_does_not_run_along_a_container_outline()
    {
        PlacedBox[] boxes = [B("a", 4, 6, 8, 4), B("db", 20, 6, 14, 16), B("t", 22, 10, 10, 4, "db"), B("b", 44, 6, 8, 4)];
        var layout = Routed(boxes, L("l", "a", "b"));
        var pts = layout.Links[0].Points;
        for (int i = 1; i < pts.Count; i++)
            Assert.False(pts[i - 1].Y == pts[i].Y && (pts[i].Y == 6 || pts[i].Y == 22) && Math.Max(pts[i - 1].X, pts[i].X) > 20 && Math.Min(pts[i - 1].X, pts[i].X) < 34);
        AssertSound(layout);
    }

    [Fact]
    public void A_link_from_a_box_to_itself_makes_a_loop_outside_the_box()
    {
        var layout = Routed([B("a", 20, 10, 8, 4)], L("self", "a", "a"));
        Assert.True(layout.Links[0].Points.Count >= 4);
        AssertSound(layout);
    }

    [Fact]
    public void A_crossing_is_avoided_when_a_short_detour_exists()
    {
        // l1 runs straight across; l2 could cut through it or step round its end.
        PlacedBox[] boxes = [B("a", 4, 18, 8, 4), B("b", 30, 18, 8, 4), B("c", 14, 4, 8, 4), B("d", 44, 30, 8, 4)];
        var layout = Routed(boxes, L("l1", "a", "b"), L("l2", "c", "d"));
        Assert.Equal(0, Metrics.Measure(layout).Crossings);
        AssertSound(layout);
    }

    [Fact]
    public void A_boxed_in_link_is_still_drawn_and_shows_up_in_the_measures()
    {
        // 'in' is sealed inside a ring of boxes with no gap between them.
        PlacedBox[] boxes = [B("in", 20, 14, 6, 4), B("top", 14, 8, 18, 4), B("bottom", 14, 20, 18, 4), B("left", 14, 12, 4, 8), B("right", 28, 12, 4, 8), B("out", 44, 14, 8, 4)];
        var layout = Routed(boxes, L("l", "in", "out"));
        Assert.True(layout.Links[0].Points.Count >= 2);
        Assert.True(Metrics.Measure(layout).ThroughBoxes > 0);
    }

    [Fact]
    public void Existing_routes_and_boxes_come_back_untouched()
    {
        PlacedBox[] boxes = [B("a", 4, 10, 8, 4), B("b", 30, 10, 8, 4), B("c", 30, 24, 8, 4)];
        var first = Plan.RouteExtra(new Layout(60, 40, boxes, []), [L("l1", "a", "b")]);
        var second = Plan.RouteExtra(first, [L("l2", "a", "c"), L("l3", "c", "b")]);
        Assert.Equal(3, second.Links.Count);
        Assert.Equal(first.Links[0].Points, second.Links.Single(l => l.Id == "l1").Points);
        Assert.Equal(first.Boxes, second.Boxes);
        Assert.Equal((first.Width, first.Height), (second.Width, second.Height));
        Assert.Equal(0, Metrics.Measure(second).OnTop);
        AssertSound(second);
    }

    [Fact]
    public void An_extra_link_whose_id_is_already_drawn_is_refused()
    {
        var first = Routed([B("a", 4, 10, 8, 4), B("b", 30, 10, 8, 4)], L("l1", "a", "b"));
        Assert.Contains("'l1'", Assert.Throws<GromaInputException>(() => Plan.RouteExtra(first, [L("l1", "b", "a")])).Message);
    }

    [Fact]
    public void An_extra_link_to_a_box_that_is_not_on_the_sheet_is_refused()
    {
        var ex = Assert.Throws<GromaInputException>(() => Routed([B("a", 4, 10, 8, 4)], L("l1", "a", "ghost")));
        Assert.Contains("'ghost'", ex.Message);
    }
}
