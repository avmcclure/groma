using static Groma.Tests.Draw;

namespace Groma.Tests;

public class LabelTests
{
    static Link L(string id, string from, string to, int w = 6, int h = 2) => new(id, from, to, LabelWidth: w, LabelHeight: h);

    static Layout Routed(PlacedBox[] boxes, params Link[] links) => Plan.RouteExtra(new Layout(60, 40, boxes, []), links);

    // Does one of the link's own segments run through the middle of its label?
    static bool OnItsLine(Route r)
    {
        var label = r.Label!.Value;
        for (int i = 1; i < r.Points.Count; i++)
        {
            Point a = r.Points[i - 1], b = r.Points[i];
            if (a.Y == b.Y && a.Y == label.Y + label.Height / 2 && Math.Min(a.X, b.X) <= label.X + label.Width / 2 && Math.Max(a.X, b.X) >= label.X + label.Width / 2) return true;
            if (a.X == b.X && a.X == label.X + label.Width / 2 && Math.Min(a.Y, b.Y) <= label.Y + label.Height / 2 && Math.Max(a.Y, b.Y) >= label.Y + label.Height / 2) return true;
        }
        return false;
    }

    [Fact]
    public void A_link_with_a_label_size_gets_a_label_of_that_size_and_one_without_gets_none()
    {
        var layout = Routed([B("a", 4, 10, 8, 4), B("b", 30, 10, 8, 4)], L("named", "a", "b", 6, 2), new Link("plain", "b", "a"));
        var label = layout.Links.Single(l => l.Id == "named").Label;
        Assert.Equal((6, 2), (label!.Value.Width, label.Value.Height));
        Assert.Null(layout.Links.Single(l => l.Id == "plain").Label);
    }

    [Fact]
    public void A_label_sits_on_its_own_line_clear_of_the_boxes()
    {
        var layout = Routed([B("a", 4, 10, 8, 4), B("b", 30, 10, 8, 4)], L("l", "a", "b"));
        Assert.True(OnItsLine(layout.Links[0]));
        Assert.Equal(new Rect(18, 11, 6, 2), layout.Links[0].Label);
        Assert.Equal(0, Metrics.Measure(layout).LabelClashes);
    }

    [Fact]
    public void A_label_sits_on_a_line_that_bends()
    {
        var layout = Routed([B("a", 4, 4, 8, 4), B("b", 40, 30, 8, 4)], L("l", "a", "b"));
        Assert.True(OnItsLine(layout.Links[0]));
        var r = Metrics.Measure(layout);
        Assert.Equal((0, 0), (r.LabelClashes, r.LabelsOverLines));
    }

    [Fact]
    public void Labels_of_neighbouring_lines_do_not_lie_over_each_other()
    {
        PlacedBox[] boxes = [B("a", 4, 10, 8, 8), B("b", 40, 10, 8, 8)];
        var layout = Routed(boxes, L("l1", "a", "b", 8, 2), L("l2", "a", "b", 8, 2), L("l3", "b", "a", 8, 2));
        Assert.All(layout.Links, l => Assert.True(OnItsLine(l)));
        Assert.Equal(0, Metrics.Measure(layout).LabelClashes);
    }

    [Fact]
    public void A_label_keeps_off_another_links_line_when_it_can()
    {
        // l2 crosses l1 near the middle, where l1's label would otherwise go.
        PlacedBox[] boxes = [B("a", 4, 18, 8, 4), B("b", 48, 18, 8, 4), B("c", 26, 4, 8, 4), B("d", 26, 32, 8, 4)];
        var layout = Routed(boxes, L("l1", "a", "b", 8, 2), new Link("l2", "c", "d"));
        Assert.Equal(1, Metrics.Measure(layout).Crossings);
        Assert.Equal(0, Metrics.Measure(layout).LabelsOverLines);
    }

    [Fact]
    public void A_label_with_nowhere_clear_to_go_is_still_placed_and_the_clash_is_reported()
    {
        var layout = Routed([B("a", 4, 10, 8, 4), B("b", 14, 10, 8, 4)], L("l", "a", "b", 10, 2));
        Assert.NotNull(layout.Links[0].Label);
        Assert.Equal(1, Metrics.Measure(layout).LabelClashes);
    }

    [Fact]
    public void A_label_stays_on_the_sheet()
    {
        var layout = Plan.RouteExtra(new Layout(30, 12, [B("a", 4, 4, 8, 4)], []), [new Link("out", "a", null, ToEdge: Side.Right, LabelWidth: 12, LabelHeight: 2)]);
        var label = layout.Links[0].Label!.Value;
        Assert.True(label.X >= 0 && label.Y >= 0 && label.Right <= 30 && label.Bottom <= 12);
    }

    [Fact]
    public void Labels_already_placed_are_left_alone_when_extra_links_arrive()
    {
        PlacedBox[] boxes = [B("a", 4, 10, 8, 4), B("b", 30, 10, 8, 4), B("c", 30, 24, 8, 4)];
        var first = Routed(boxes, L("l1", "a", "b"));
        var second = Plan.RouteExtra(first, [L("l2", "a", "c"), L("l3", "c", "b")]);
        Assert.Equal(first.Links[0].Label, second.Links.Single(l => l.Id == "l1").Label);
        Assert.All(second.Links, l => Assert.NotNull(l.Label));
        Assert.Equal(0, Metrics.Measure(second).LabelClashes);
    }

    [Fact]
    public void A_fresh_layout_and_an_update_both_place_labels()
    {
        var graph = new Graph([new Box("a", 8, 4), new Box("b", 8, 4), new Box("c", 8, 4)], [L("l1", "a", "b"), L("l2", "b", "c")]);
        var first = Plan.Survey(graph);
        Assert.All(first.Links, l => Assert.True(OnItsLine(l)));
        var second = Plan.Update(graph with { Links = [.. graph.Links, L("l3", "a", "c")] }, first);
        Assert.All(second.Links, l => Assert.True(OnItsLine(l)));
    }
}
