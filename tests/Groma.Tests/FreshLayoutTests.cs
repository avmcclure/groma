namespace Groma.Tests;

/// <summary>The layered method behind a fresh layout: what its stages are supposed to achieve.</summary>
public class FreshLayoutTests
{
    static Graph G(string boxes, string links, string inside = "")
    {
        var parents = inside.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('<')).ToDictionary(p => p[0], p => p[1]);
        return new Graph(
            [.. boxes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(id => new Box(id, 8, parents.ContainsValue(id) ? 2 : 4, parents.GetValueOrDefault(id)))],
            [.. links.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(l => new Link(l, l.Split('>')[0], l.Split('>')[1]))]);
    }

    static PlacedBox At(Layout l, string id) => l.Boxes.Single(b => b.Id == id);

    static void AssertSound(Layout layout)
    {
        var r = Metrics.Measure(layout);
        Assert.Equal((0, 0, 0, 0), (r.OverlappingBoxes, r.ThroughBoxes, r.Diagonals, r.DetachedEnds));
    }

    [Fact]
    public void Links_flow_left_to_right()
    {
        var layout = Plan.Survey(G("a b c", "a>b b>c"));
        Assert.True(At(layout, "a").X < At(layout, "b").X && At(layout, "b").X < At(layout, "c").X);
        Assert.Equal(0, Metrics.Measure(layout).Bends);
        AssertSound(layout);
    }

    [Fact]
    public void Links_flow_top_to_bottom_when_asked()
    {
        var layout = Plan.Survey(G("a b c", "a>b b>c"), new Options { Direction = Direction.Down });
        Assert.True(At(layout, "a").Y < At(layout, "b").Y && At(layout, "b").Y < At(layout, "c").Y);
        Assert.Equal(At(layout, "a").X, At(layout, "c").X);
        AssertSound(layout);
    }

    [Fact]
    public void Boxes_in_a_column_are_reordered_to_uncross_their_links()
    {
        // In id order, a>d and b>c would cross.
        var layout = Plan.Survey(G("a b c d", "a>d b>c"));
        Assert.Equal(0, Metrics.Measure(layout).Crossings);
        Assert.Equal(Math.Sign(At(layout, "a").Y - At(layout, "b").Y), Math.Sign(At(layout, "d").Y - At(layout, "c").Y));
        AssertSound(layout);
    }

    [Fact]
    public void A_fan_of_links_is_drawn_without_crossings()
    {
        var layout = Plan.Survey(G("hub a b c d e", "hub>a hub>b hub>c hub>d hub>e"));
        Assert.Equal(0, Metrics.Measure(layout).Crossings);
        AssertSound(layout);
    }

    [Fact]
    public void A_loop_of_links_is_still_laid_out()
    {
        var layout = Plan.Survey(G("a b c", "a>b b>c c>a"));
        Assert.Equal(3, layout.Boxes.Select(b => b.X).Distinct().Count());
        AssertSound(layout);
    }

    [Fact]
    public void A_box_that_only_feeds_a_late_box_sits_just_before_it_not_at_the_far_left()
    {
        var layout = Plan.Survey(G("a b c d late", "a>b b>c c>d late>d"));
        Assert.Equal(At(layout, "c").X, At(layout, "late").X);
        AssertSound(layout);
    }

    [Fact]
    public void A_link_that_skips_a_column_does_not_pull_its_ends_together()
    {
        var layout = Plan.Survey(G("a b c", "a>b b>c a>c"));
        Assert.True(At(layout, "a").X < At(layout, "b").X && At(layout, "b").X < At(layout, "c").X);
        AssertSound(layout);
    }

    [Fact]
    public void A_box_lines_up_with_the_box_it_links_to()
    {
        var layout = Plan.Survey(G("a b c d", "a>b c>d"));
        Assert.Equal(At(layout, "a").Y, At(layout, "b").Y);
        Assert.Equal(At(layout, "c").Y, At(layout, "d").Y);
    }

    [Fact]
    public void Boxes_with_no_links_are_still_placed_clear_of_the_rest()
    {
        var layout = Plan.Survey(G("a b lone1 lone2 lone3", "a>b"));
        Assert.Equal(5, layout.Boxes.Count);
        AssertSound(layout);
    }

    [Fact]
    public void A_container_takes_its_place_in_the_flow_as_one_block()
    {
        var layout = Plan.Survey(G("app db t1 t2 report", "app>t1 t2>report", "t1<db t2<db"));
        var db = At(layout, "db");
        Assert.True(At(layout, "app").X + 8 <= db.X && db.X + db.Width <= At(layout, "report").X);
        AssertSound(layout);
    }

    [Fact]
    public void Links_between_boxes_in_a_container_shape_its_inside()
    {
        var layout = Plan.Survey(G("db t1 t2 t3", "t1>t2 t2>t3", "t1<db t2<db t3<db"));
        Assert.True(At(layout, "t1").X < At(layout, "t2").X && At(layout, "t2").X < At(layout, "t3").X);
        AssertSound(layout);
    }

    [Fact]
    public void Two_links_in_opposite_directions_between_two_boxes_do_not_stack_them()
    {
        var layout = Plan.Survey(G("a b", "a>b b>a"));
        Assert.NotEqual(At(layout, "a").X, At(layout, "b").X);
        AssertSound(layout);
    }
}
