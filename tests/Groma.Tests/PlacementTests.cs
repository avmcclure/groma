using static Groma.Tests.Draw;

namespace Groma.Tests;

/// <summary>Incremental layout: what stays, where new boxes go, and when it must refuse.</summary>
public class PlacementTests
{
    static Graph G(Box[] boxes, params Link[] links) => new(boxes, links);
    static Box Bx(string id, int w = 8, int h = 4, string? parent = null) => new(id, w, h, parent);
    static Link L(string from, string to) => new($"{from}>{to}", from, to);
    static Layout Before(params PlacedBox[] boxes) => new(60, 40, boxes, []);
    static PlacedBox At(Layout l, string id) => l.Boxes.Single(b => b.Id == id);

    static void AssertSound(Layout layout, Layout? previous = null)
    {
        var r = Metrics.Measure(layout, previous);
        Assert.Equal((0, 0, 0, 0, 0), (r.Moved, r.OverlappingBoxes, r.ThroughBoxes, r.Diagonals, r.DetachedEnds));
    }

    static bool Clear(PlacedBox a, PlacedBox b, int gap) =>
        a.X - gap >= b.X + b.Width || b.X - gap >= a.X + a.Width || a.Y - gap >= b.Y + b.Height || b.Y - gap >= a.Y + a.Height;

    [Fact]
    public void Surviving_boxes_keep_their_exact_positions()
    {
        var before = Before(B("a", 4, 4, 8, 4), B("b", 30, 20, 8, 4));
        var after = Plan.Update(G([Bx("a"), Bx("b"), Bx("c")], L("a", "b"), L("c", "a")), before);
        Assert.Equal(before.Boxes[0], At(after, "a"));
        Assert.Equal(before.Boxes[1], At(after, "b"));
        AssertSound(after, before);
    }

    [Fact]
    public void A_new_box_lands_beside_the_box_it_links_to()
    {
        var before = Before(B("a", 4, 4, 8, 4), B("far", 50, 30, 8, 4));
        var after = Plan.Update(G([Bx("a"), Bx("far"), Bx("new")], L("new", "a")), before);
        var (a, n) = (At(after, "a"), At(after, "new"));
        Assert.True(Math.Abs(n.X - a.X) + Math.Abs(n.Y - a.Y) <= 16, $"new box at {n.X},{n.Y}");
        AssertSound(after, before);
    }

    [Fact]
    public void A_new_box_linked_to_two_boxes_lands_between_them_when_there_is_room()
    {
        var before = Before(B("a", 4, 10, 8, 4), B("b", 44, 10, 8, 4));
        var after = Plan.Update(G([Bx("a"), Bx("b"), Bx("new")], L("new", "a"), L("new", "b")), before);
        Assert.InRange(At(after, "new").X, 16, 32);
        AssertSound(after, before);
    }

    [Fact]
    public void A_new_box_keeps_the_gap_from_every_other_box()
    {
        var before = Before(B("a", 4, 4, 8, 4), B("b", 4, 12, 8, 4), B("c", 16, 4, 8, 4), B("d", 16, 12, 8, 4));
        var after = Plan.Update(G([Bx("a"), Bx("b"), Bx("c"), Bx("d"), Bx("new")], L("new", "a"), L("new", "d")), before);
        Assert.All(after.Boxes.Where(b => b.Id != "new"), b => Assert.True(Clear(At(after, "new"), b, 4), $"too close to {b.Id}"));
        AssertSound(after, before);
    }

    [Fact]
    public void A_new_box_with_no_links_goes_to_free_space_and_the_sheet_grows_to_hold_it()
    {
        var before = new Layout(20, 16, [B("a", 4, 4, 8, 4)], []);
        var after = Plan.Update(G([Bx("a"), Bx("loner")]), before);
        var n = At(after, "loner");
        Assert.True(after.Width >= n.X + n.Width + 4 && after.Height >= n.Y + n.Height + 4);
        Assert.True(n.X >= 4 && n.Y >= 4);
        AssertSound(after, before);
    }

    [Fact]
    public void A_sheet_does_not_shrink_when_boxes_go()
    {
        var before = Before(B("a", 4, 4, 8, 4), B("b", 40, 30, 8, 4));
        var after = Plan.Update(G([Bx("a")]), before);
        Assert.Equal((60, 40), (after.Width, after.Height));
        Assert.Single(after.Boxes);
    }

    [Fact]
    public void A_removed_box_takes_its_links_with_it_and_every_remaining_link_is_drawn()
    {
        var before = Plan.Survey(G([Bx("a"), Bx("b"), Bx("c")], L("a", "b"), L("b", "c")));
        var after = Plan.Update(G([Bx("a"), Bx("c")], L("a", "c")), before);
        Assert.Equal(["a>c"], after.Links.Select(l => l.Id));
        AssertSound(after, before);
    }

    [Fact]
    public void A_new_box_goes_inside_its_container_when_there_is_room()
    {
        var db = B("db", 4, 4, 30, 20);
        var before = Before(db, B("t1", 6, 8, 8, 4, "db"));
        var after = Plan.Update(G([Bx("db", 10, 2), Bx("t1", parent: "db"), Bx("t2", parent: "db")]), before);
        Assert.Equal(db, At(after, "db"));
        AssertSound(after, before);
    }

    [Fact]
    public void A_full_container_grows_from_its_corner_to_take_a_new_box()
    {
        var before = Before(B("db", 4, 4, 12, 10), B("t1", 6, 8, 8, 4, "db"));
        var after = Plan.Update(G([Bx("db", 10, 2), Bx("t1", parent: "db"), Bx("t2", parent: "db")]), before);
        var db = At(after, "db");
        Assert.Equal((4, 4), (db.X, db.Y));
        Assert.True(db.Width > 12 || db.Height > 10);
        AssertSound(after, before);
    }

    [Fact]
    public void A_container_grows_down_when_a_neighbour_blocks_the_right()
    {
        var before = Before(B("db", 4, 4, 12, 10), B("t1", 6, 8, 8, 4, "db"), B("neighbour", 20, 4, 8, 10));
        var after = Plan.Update(G([Bx("db", 10, 2), Bx("t1", parent: "db"), Bx("t2", parent: "db"), Bx("neighbour", 8, 10)]), before);
        var db = At(after, "db");
        Assert.Equal((12, true), (db.Width, db.Height > 10));
        AssertSound(after, before);
    }

    [Fact]
    public void A_container_hemmed_in_on_both_sides_asks_for_a_re_survey_naming_the_new_box()
    {
        var before = Before(B("db", 4, 4, 12, 10), B("t1", 6, 8, 8, 4, "db"), B("right", 20, 4, 8, 10), B("below", 4, 18, 12, 4));
        var graph = G([Bx("db", 10, 2), Bx("t1", parent: "db"), Bx("t2", parent: "db"), Bx("right", 8, 10), Bx("below", 12, 4)]);
        var ex = Assert.Throws<ResurveyNeededException>(() => Plan.Update(graph, before));
        Assert.Contains("'t2'", ex.Message);
    }

    [Fact]
    public void A_container_does_not_shrink_when_a_box_inside_it_goes()
    {
        var db = B("db", 4, 4, 30, 20);
        var before = Before(db, B("t1", 6, 8, 8, 4, "db"), B("t2", 20, 14, 8, 4, "db"));
        var after = Plan.Update(G([Bx("db", 10, 2), Bx("t1", parent: "db")]), before);
        Assert.Equal(db, At(after, "db"));
    }

    [Fact]
    public void Two_deep_an_inner_container_and_the_one_around_it_both_grow_from_their_corners()
    {
        var before = Before(B("server", 2, 2, 18, 16), B("db", 4, 6, 12, 10, "server"), B("t1", 6, 10, 8, 4, "db"));
        var after = Plan.Update(G([Bx("server", 10, 2), Bx("db", 10, 2, "server"), Bx("t1", parent: "db"), Bx("t2", parent: "db")]), before);
        Assert.Equal((2, 2, 4, 6), (At(after, "server").X, At(after, "server").Y, At(after, "db").X, At(after, "db").Y));
        AssertSound(after, before);
    }

    [Fact]
    public void A_new_container_arrives_with_its_boxes_inside_it()
    {
        var before = Before(B("a", 4, 4, 8, 4));
        var after = Plan.Update(G([Bx("a"), Bx("db", 10, 2), Bx("t1", parent: "db"), Bx("t2", parent: "db")], L("a", "t1")), before);
        Assert.Equal(4, after.Boxes.Count);
        AssertSound(after, before);
    }

    [Fact]
    public void A_surviving_box_that_grew_into_its_neighbour_asks_for_a_re_survey()
    {
        var before = Before(B("a", 4, 4, 8, 4), B("b", 14, 4, 8, 4));
        var ex = Assert.Throws<ResurveyNeededException>(() => Plan.Update(G([Bx("a", 20), Bx("b")]), before));
        Assert.Contains("'a'", ex.Message);
    }

    [Fact]
    public void A_surviving_box_that_grew_with_room_to_spare_keeps_its_corner_and_takes_its_new_size()
    {
        var before = Before(B("a", 4, 4, 8, 4), B("b", 30, 4, 8, 4));
        var after = Plan.Update(G([Bx("a", 14, 6), Bx("b")], L("a", "b")), before);
        Assert.Equal(B("a", 4, 4, 14, 6), At(after, "a"));
        AssertSound(after, before);
    }

    [Fact]
    public void A_surviving_box_that_shrank_keeps_its_corner_and_its_lines_reach_its_new_sides()
    {
        var before = Plan.Survey(G([Bx("a", 16, 6), Bx("b"), Bx("c")], L("a", "b"), L("c", "a")));
        var after = Plan.Update(G([Bx("a", 6, 2), Bx("b"), Bx("c")], L("a", "b"), L("c", "a")), before);
        Assert.Equal((At(before, "a").X, At(before, "a").Y, 6, 2), (At(after, "a").X, At(after, "a").Y, At(after, "a").Width, At(after, "a").Height));
        AssertSound(after, before);
    }

    [Fact]
    public void A_box_inside_a_container_that_grew_past_the_outline_makes_the_container_grow_too()
    {
        var before = Before(B("db", 4, 4, 14, 12), B("t", 6, 8, 8, 4, "db"));
        var after = Plan.Update(G([Bx("db", 10, 2), Bx("t", 16, 4, "db")]), before);
        Assert.Equal((6, 8, 16), (At(after, "t").X, At(after, "t").Y, At(after, "t").Width));
        Assert.Equal((4, 4), (At(after, "db").X, At(after, "db").Y));
        AssertSound(after, before);
    }

    [Fact]
    public void A_box_that_becomes_a_container_keeps_its_corner_and_grows_round_its_new_contents()
    {
        var before = Before(B("a", 4, 4, 8, 4), B("b", 40, 4, 8, 4));
        var after = Plan.Update(G([Bx("a", 8, 2), Bx("inner", parent: "a"), Bx("b")], L("inner", "b")), before);
        Assert.Equal((4, 4), (At(after, "a").X, At(after, "a").Y));
        AssertSound(after, before);
    }

    [Fact]
    public void A_container_that_lost_everything_inside_it_is_kept_as_a_plain_box_at_its_corner()
    {
        var before = Before(B("db", 4, 4, 30, 20), B("t", 6, 8, 8, 4, "db"), B("b", 44, 4, 8, 4));
        var after = Plan.Update(G([Bx("db", 10, 2), Bx("b")], L("b", "db")), before);
        Assert.Equal(B("db", 4, 4, 10, 2), At(after, "db"));
        AssertSound(after, before);
    }

    [Fact]
    public void A_surviving_box_moved_to_another_container_asks_for_a_re_survey()
    {
        var before = Before(B("db1", 4, 4, 14, 12), B("t", 6, 8, 8, 4, "db1"), B("db2", 30, 4, 14, 12), B("u", 32, 8, 8, 4, "db2"));
        var graph = G([Bx("db1", 10, 2), Bx("db2", 10, 2), Bx("t", parent: "db2"), Bx("u", parent: "db2"), Bx("v", parent: "db1")]);
        Assert.Throws<ResurveyNeededException>(() => Plan.Update(graph, before));
    }

    [Fact]
    public void Updating_with_an_unchanged_graph_changes_nothing()
    {
        var graph = G([Bx("a"), Bx("b"), Bx("db", 10, 2), Bx("t", parent: "db")], L("a", "b"), L("a", "t"));
        var first = Plan.Survey(graph);
        Assert.Equal(LayoutFile.Write(first), LayoutFile.Write(Plan.Update(graph, first)));
    }
}

/// <summary>What must hold for any fresh layout, whatever method produces it.</summary>
public class SurveyTests
{
    [Fact]
    public void An_empty_graph_gives_an_empty_sheet()
    {
        var layout = Plan.Survey(new Graph([], []));
        Assert.Empty(layout.Boxes);
    }

    [Fact]
    public void Every_box_is_placed_at_its_given_size_clear_of_the_sheet_edge()
    {
        var layout = Plan.Survey(new Graph([new Box("a", 8, 4), new Box("b", 12, 6)], [new Link("l", "a", "b")]));
        Assert.Equal([(8, 4), (12, 6)], layout.Boxes.Select(b => (b.Width, b.Height)));
        Assert.All(layout.Boxes, b => Assert.True(b.X >= 4 && b.Y >= 4 && b.X + b.Width <= layout.Width - 4 && b.Y + b.Height <= layout.Height - 4));
    }

    [Fact]
    public void A_container_is_drawn_round_its_boxes_with_room_for_its_title()
    {
        var layout = Plan.Survey(new Graph([new Box("db", 20, 2), new Box("t", 8, 4, "db")], []));
        var (db, t) = (layout.Boxes[0], layout.Boxes[1]);
        Assert.True(db.Width >= 20 && t.Y >= db.Y + 2 + 2 && t.X >= db.X + 2);
        Assert.Equal(0, Metrics.Measure(layout).OverlappingBoxes);
    }

    [Fact]
    public void Input_that_breaks_the_rules_is_refused()
    {
        Assert.Throws<GromaInputException>(() => Plan.Survey(new Graph([new Box("a", 8, 4), new Box("a", 8, 4)], [])));
    }
}

public class NestingTests
{
    [Fact]
    public void A_line_keeps_out_of_a_container_title_when_it_costs_little_to_do_so()
    {
        // The straight way in from 'a' runs along the row where the title of 'db' is written.
        var before = new Layout(60, 30, [B("a", 4, 2, 8, 4), B("db", 20, 4, 30, 16), B("t", 36, 12, 8, 4, "db")], []);
        var graph = new Graph([new Box("a", 8, 4), new Box("db", 10, 2), new Box("t", 8, 4, "db")], [new Link("l", "a", "t")]);
        var points = Plan.Update(graph, before).Links[0].Points;
        for (int i = 1; i < points.Count; i++)
            Assert.False(points[i - 1].Y == 5 && points[i].Y == 5 && Math.Max(points[i - 1].X, points[i].X) > 20, "the line runs through the title row");
    }

    [Fact]
    public void A_container_of_containers_keeps_only_a_modest_reserve()
    {
        var graph = new Graph(
            [new Box("server", 10, 2), new Box("db", 10, 2, "server"), new Box("t1", 8, 4, "db"), new Box("t2", 8, 4, "db"), new Box("t3", 8, 4, "db"), new Box("t4", 8, 4, "db")], []);
        var layout = Plan.Survey(graph);
        var (server, db) = (layout.Boxes.Single(b => b.Id == "server"), layout.Boxes.Single(b => b.Id == "db"));
        Assert.InRange(server.Y + server.Height - (db.Y + db.Height), 2, 12);
    }
}
