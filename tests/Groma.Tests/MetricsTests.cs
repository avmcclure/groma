using static Groma.Tests.Draw;

namespace Groma.Tests;

/// <summary>Tiny hand-drawn layouts whose answer is obvious by eye.</summary>
static class Draw
{
    public static PlacedBox B(string id, int x, int y, int w, int h, string? parent = null) => new(id, x, y, w, h, parent);

    public static Route R(string id, string? from, string? to, params (int x, int y)[] pts) =>
        new(id, from, to, pts.Select(p => new Point(p.x, p.y)).ToList());

    public static Layout Sheet(PlacedBox[] boxes, params Route[] links) => new(100, 100, boxes, links);

    // Two boxes far apart, used as line ends when the test is about the lines.
    public static readonly PlacedBox[] None = [];
}

public class CrossingTests
{
    [Fact]
    public void A_horizontal_and_a_vertical_line_of_different_links_cross_once()
    {
        var r = Metrics.Measure(Sheet(None,
            R("h", null, null, (0, 5), (10, 5)),
            R("v", null, null, (5, 0), (5, 10))));
        Assert.Equal(1, r.Crossings);
    }

    [Fact]
    public void A_line_ending_on_another_is_not_a_crossing()
    {
        var r = Metrics.Measure(Sheet(None,
            R("h", null, null, (0, 5), (10, 5)),
            R("v", null, null, (5, 0), (5, 5))));
        Assert.Equal(0, r.Crossings);
    }

    [Fact]
    public void A_link_crossing_itself_is_not_counted()
    {
        var r = Metrics.Measure(Sheet(None,
            R("loop", null, null, (0, 5), (10, 5), (10, 0), (5, 0), (5, 10))));
        Assert.Equal(0, r.Crossings);
    }

    [Fact]
    public void Each_crossing_pair_of_segments_counts()
    {
        var r = Metrics.Measure(Sheet(None,
            R("h1", null, null, (0, 3), (10, 3)),
            R("h2", null, null, (0, 6), (10, 6)),
            R("v", null, null, (5, 0), (5, 10))));
        Assert.Equal(2, r.Crossings);
    }
}

public class OnTopTests
{
    [Fact]
    public void Two_links_sharing_a_stretch_of_line_are_on_top_of_each_other()
    {
        var r = Metrics.Measure(Sheet(None,
            R("a", null, null, (0, 5), (10, 5)),
            R("b", null, null, (4, 5), (14, 5))));
        Assert.Equal(1, r.OnTop);
    }

    [Fact]
    public void Lines_meeting_end_to_end_are_not_on_top()
    {
        var r = Metrics.Measure(Sheet(None,
            R("a", null, null, (0, 5), (10, 5)),
            R("b", null, null, (10, 5), (20, 5))));
        Assert.Equal(0, r.OnTop);
    }

    [Fact]
    public void Parallel_lines_one_step_apart_are_not_on_top()
    {
        var r = Metrics.Measure(Sheet(None,
            R("a", null, null, (5, 0), (5, 10)),
            R("b", null, null, (6, 0), (6, 10))));
        Assert.Equal(0, r.OnTop);
    }

    [Fact]
    public void Vertical_lines_sharing_a_stretch_are_on_top()
    {
        var r = Metrics.Measure(Sheet(None,
            R("a", null, null, (5, 0), (5, 10)),
            R("b", null, null, (5, 9), (5, 20))));
        Assert.Equal(1, r.OnTop);
    }
}

public class ThroughBoxTests
{
    static readonly PlacedBox[] Boxes = [B("a", 0, 4, 4, 2), B("mid", 10, 2, 4, 6), B("b", 20, 4, 4, 2)];

    [Fact]
    public void A_line_through_an_unrelated_box_counts()
    {
        var r = Metrics.Measure(Sheet(Boxes, R("l", "a", "b", (4, 5), (20, 5))));
        Assert.Equal(1, r.ThroughBoxes);
    }

    [Fact]
    public void A_line_running_along_the_side_of_an_unrelated_box_counts()
    {
        var r = Metrics.Measure(Sheet(Boxes, R("l", "a", "b", (4, 5), (6, 5), (6, 2), (18, 2), (18, 5), (20, 5))));
        Assert.Equal(1, r.ThroughBoxes);
    }

    [Fact]
    public void A_line_passing_clear_of_the_box_does_not_count()
    {
        var r = Metrics.Measure(Sheet(Boxes, R("l", "a", "b", (4, 5), (6, 5), (6, 1), (18, 1), (18, 5), (20, 5))));
        Assert.Equal(0, r.ThroughBoxes);
    }

    [Fact]
    public void A_link_is_allowed_inside_its_own_end_boxes()
    {
        var r = Metrics.Measure(Sheet(Boxes, R("l", "a", "mid", (2, 5), (12, 5))));
        Assert.Equal(0, r.ThroughBoxes);
    }

    [Fact]
    public void One_link_through_one_box_counts_once_however_many_segments_pass()
    {
        var r = Metrics.Measure(Sheet(Boxes, R("l", "a", "b", (4, 5), (12, 5), (12, 6), (20, 6))));
        Assert.Equal(1, r.ThroughBoxes);
    }

    [Fact]
    public void Containers_are_not_tested_here()
    {
        PlacedBox[] boxes = [B("a", 0, 4, 4, 2), B("db", 10, 0, 8, 10), B("t", 12, 4, 4, 2, "db"), B("b", 30, 4, 4, 2)];
        var r = Metrics.Measure(Sheet(boxes, R("l", "a", "b", (4, 9), (30, 9))));
        Assert.Equal(0, r.ThroughBoxes);
    }
}

public class ContainerCutTests
{
    // db holds t1 and t2; a and b are outside, either side of it.
    static readonly PlacedBox[] Boxes =
    [
        B("a", 0, 4, 4, 2), B("db", 10, 0, 12, 12),
        B("t1", 12, 2, 4, 2, "db"), B("t2", 12, 8, 4, 2, "db"), B("b", 30, 4, 4, 2),
    ];

    [Fact]
    public void A_line_through_a_container_it_has_no_end_in_counts()
    {
        var r = Metrics.Measure(Sheet(Boxes, R("l", "a", "b", (4, 5), (30, 5))));
        Assert.Equal(1, r.ContainerCuts);
    }

    [Fact]
    public void A_link_to_a_box_inside_the_container_does_not_count()
    {
        var r = Metrics.Measure(Sheet(Boxes, R("l", "a", "t1", (4, 5), (8, 5), (8, 3), (12, 3))));
        Assert.Equal(0, r.ContainerCuts);
    }

    [Fact]
    public void A_link_to_the_container_itself_does_not_count()
    {
        var r = Metrics.Measure(Sheet(Boxes, R("l", "a", "db", (4, 5), (10, 5))));
        Assert.Equal(0, r.ContainerCuts);
    }

    [Fact]
    public void A_link_between_two_boxes_in_the_same_container_does_not_count()
    {
        var r = Metrics.Measure(Sheet(Boxes, R("l", "t1", "t2", (14, 4), (14, 8))));
        Assert.Equal(0, r.ContainerCuts);
    }

    [Fact]
    public void A_line_running_along_the_container_border_does_not_count()
    {
        var r = Metrics.Measure(Sheet(Boxes, R("l", "a", "b", (4, 5), (8, 5), (8, 12), (26, 12), (26, 5), (30, 5))));
        Assert.Equal(0, r.ContainerCuts);
    }

    [Fact]
    public void Two_deep_a_line_through_an_outer_and_an_inner_container_counts_both()
    {
        PlacedBox[] boxes =
        [
            B("a", 0, 6, 4, 2), B("server", 10, 0, 20, 14), B("db", 12, 2, 12, 10, "server"),
            B("t", 14, 4, 4, 2, "db"), B("b", 40, 6, 4, 2),
        ];
        var r = Metrics.Measure(Sheet(boxes, R("l", "a", "b", (4, 7), (40, 7))));
        Assert.Equal(2, r.ContainerCuts);
    }
}

public class OverlappingBoxTests
{
    [Fact]
    public void Two_boxes_sharing_area_overlap()
    {
        var r = Metrics.Measure(Sheet([B("a", 0, 0, 4, 2), B("b", 3, 1, 4, 2)]));
        Assert.Equal(1, r.OverlappingBoxes);
    }

    [Fact]
    public void Boxes_that_only_touch_do_not_overlap()
    {
        var r = Metrics.Measure(Sheet([B("a", 0, 0, 4, 2), B("b", 4, 0, 4, 2)]));
        Assert.Equal(0, r.OverlappingBoxes);
    }

    [Fact]
    public void A_box_inside_its_own_container_does_not_overlap_it()
    {
        var r = Metrics.Measure(Sheet([B("db", 0, 0, 10, 10), B("t", 2, 2, 4, 2, "db")]));
        Assert.Equal(0, r.OverlappingBoxes);
    }

    [Fact]
    public void A_box_sticking_out_of_its_container_counts()
    {
        var r = Metrics.Measure(Sheet([B("db", 0, 0, 10, 10), B("t", 8, 2, 4, 2, "db")]));
        Assert.Equal(1, r.OverlappingBoxes);
    }

    [Fact]
    public void A_box_lying_over_a_container_it_does_not_belong_to_counts()
    {
        var r = Metrics.Measure(Sheet([B("db", 0, 0, 10, 10), B("t", 2, 2, 4, 2, "db"), B("x", 6, 6, 4, 2)]));
        Assert.Equal(1, r.OverlappingBoxes);
    }

    [Fact]
    public void A_box_two_deep_inside_its_grandparent_does_not_overlap_it()
    {
        var r = Metrics.Measure(Sheet([B("server", 0, 0, 20, 20), B("db", 2, 2, 10, 10, "server"), B("t", 4, 4, 4, 2, "db")]));
        Assert.Equal(0, r.OverlappingBoxes);
    }
}

public class ShapeTests
{
    [Fact]
    public void Bends_and_length_are_summed_over_links()
    {
        var r = Metrics.Measure(Sheet(None,
            R("a", null, null, (0, 0), (10, 0), (10, 5), (20, 5)),
            R("b", null, null, (0, 9), (7, 9))));
        Assert.Equal(2, r.Bends);
        Assert.Equal(2, r.MaxBends);
        Assert.Equal(25 + 7, r.Length);
        Assert.Equal(2, r.Links);
    }

    [Fact]
    public void Sheet_size_is_taken_from_the_layout()
    {
        var r = Metrics.Measure(new Layout(40, 25, [B("a", 0, 0, 4, 2)], []));
        Assert.Equal((40, 25, 1000L, 1), (r.Width, r.Height, r.Area, r.Boxes));
    }

    [Fact]
    public void A_slanted_segment_is_reported()
    {
        var r = Metrics.Measure(Sheet(None, R("a", null, null, (0, 0), (5, 5))));
        Assert.Equal(1, r.Diagonals);
    }

    [Fact]
    public void A_point_repeated_in_a_route_is_reported_as_a_bad_segment()
    {
        var r = Metrics.Measure(Sheet(None, R("a", null, null, (0, 0), (0, 0), (5, 0))));
        Assert.Equal(1, r.Diagonals);
    }
}

public class DetachedEndTests
{
    static readonly PlacedBox[] Boxes = [B("a", 0, 4, 4, 2), B("b", 20, 4, 4, 2)];

    [Fact]
    public void Ends_on_the_sides_of_their_boxes_are_attached()
    {
        var r = Metrics.Measure(Sheet(Boxes, R("l", "a", "b", (4, 5), (20, 5))));
        Assert.Equal(0, r.DetachedEnds);
    }

    [Fact]
    public void An_end_short_of_its_box_is_detached()
    {
        var r = Metrics.Measure(Sheet(Boxes, R("l", "a", "b", (4, 5), (18, 5))));
        Assert.Equal(1, r.DetachedEnds);
    }

    [Fact]
    public void An_end_inside_its_box_is_detached()
    {
        var r = Metrics.Measure(Sheet(Boxes, R("l", "a", "b", (2, 5), (20, 5))));
        Assert.Equal(1, r.DetachedEnds);
    }

    [Fact]
    public void An_end_on_the_named_sheet_edge_is_attached()
    {
        var route = R("l", "a", null, (4, 5), (100, 5)) with { ToEdge = Side.Right };
        Assert.Equal(0, Metrics.Measure(Sheet(Boxes, route)).DetachedEnds);
    }

    [Fact]
    public void An_end_on_the_wrong_sheet_edge_is_detached()
    {
        var route = R("l", "a", null, (4, 5), (100, 5)) with { ToEdge = Side.Bottom };
        Assert.Equal(1, Metrics.Measure(Sheet(Boxes, route)).DetachedEnds);
    }

    [Fact]
    public void An_end_naming_a_box_that_is_not_in_the_layout_is_detached()
    {
        var r = Metrics.Measure(Sheet(Boxes, R("l", "a", "gone", (4, 5), (20, 5))));
        Assert.Equal(1, r.DetachedEnds);
    }
}

public class LabelClashTests
{
    static readonly PlacedBox[] Boxes = [B("a", 0, 4, 4, 2), B("b", 20, 4, 4, 2), B("c", 10, 10, 4, 2)];

    static Route Labelled(string id, Rect label, params (int x, int y)[] pts) => R(id, "a", "b", pts) with { Label = label };

    [Fact]
    public void A_label_sitting_on_its_own_line_is_fine()
    {
        var r = Metrics.Measure(Sheet(Boxes, Labelled("l", new Rect(8, 4, 6, 2), (4, 5), (20, 5))));
        Assert.Equal((0, 0), (r.LabelClashes, r.LabelsOverLines));
    }

    [Fact]
    public void A_label_over_a_box_clashes()
    {
        var r = Metrics.Measure(Sheet(Boxes, Labelled("l", new Rect(9, 9, 6, 2), (4, 5), (20, 5))));
        Assert.Equal(1, r.LabelClashes);
    }

    [Fact]
    public void Two_labels_over_each_other_both_clash()
    {
        var r = Metrics.Measure(Sheet(Boxes,
            Labelled("l1", new Rect(8, 4, 6, 2), (4, 5), (20, 5)),
            Labelled("l2", new Rect(10, 5, 6, 2), (4, 6), (20, 6))));
        Assert.Equal(2, r.LabelClashes);
    }

    [Fact]
    public void Labels_that_only_touch_do_not_clash()
    {
        var r = Metrics.Measure(Sheet(Boxes,
            Labelled("l1", new Rect(6, 0, 6, 1), (4, 5), (20, 5)),
            Labelled("l2", new Rect(12, 0, 6, 1), (4, 6), (20, 6))));
        Assert.Equal(0, r.LabelClashes);
    }

    [Fact]
    public void A_label_with_another_links_line_through_it_is_counted_separately()
    {
        var r = Metrics.Measure(Sheet(Boxes,
            Labelled("l1", new Rect(8, 4, 6, 2), (4, 5), (20, 5)),
            R("l2", "c", "b", (12, 10), (12, 0), (22, 0), (22, 4))));
        Assert.Equal((0, 1), (r.LabelClashes, r.LabelsOverLines));
    }
}

public class MovedTests
{
    [Fact]
    public void A_surviving_box_whose_corner_changed_has_moved()
    {
        var before = Sheet([B("a", 0, 0, 4, 2), B("b", 10, 0, 4, 2)]);
        var after = Sheet([B("a", 0, 0, 4, 2), B("b", 11, 0, 4, 2)]);
        var r = Metrics.Measure(after, before);
        Assert.Equal((2, 1), (r.Survivors, r.Moved));
    }

    [Fact]
    public void New_and_removed_boxes_are_not_survivors()
    {
        var before = Sheet([B("a", 0, 0, 4, 2), B("old", 10, 0, 4, 2)]);
        var after = Sheet([B("a", 0, 0, 4, 2), B("new", 30, 0, 4, 2)]);
        var r = Metrics.Measure(after, before);
        Assert.Equal((1, 0), (r.Survivors, r.Moved));
    }

    [Fact]
    public void A_container_that_grew_from_the_same_corner_has_not_moved()
    {
        var before = Sheet([B("db", 0, 0, 10, 10), B("t", 2, 2, 4, 2, "db")]);
        var after = Sheet([B("db", 0, 0, 16, 10), B("t", 2, 2, 4, 2, "db"), B("u", 10, 2, 4, 2, "db")]);
        Assert.Equal(0, Metrics.Measure(after, before).Moved);
    }

    [Fact]
    public void Without_a_previous_layout_nothing_survived_or_moved()
    {
        var r = Metrics.Measure(Sheet([B("a", 0, 0, 4, 2)]));
        Assert.Equal((0, 0), (r.Survivors, r.Moved));
    }
}

public class ReportJsonTests
{
    [Fact]
    public void A_report_is_written_as_integers_in_a_fixed_order()
    {
        var report = Metrics.Measure(new Layout(40, 25, [B("a", 0, 0, 4, 2)], [R("l", null, null, (0, 5), (10, 5), (10, 9))]));
        Assert.Equal(
            """
            {
              "boxes": 1,
              "links": 1,
              "survivors": 0,
              "moved": 0,
              "crossings": 0,
              "throughBoxes": 0,
              "overlappingBoxes": 0,
              "onTop": 0,
              "containerCuts": 0,
              "bends": 1,
              "maxBends": 1,
              "length": 14,
              "labelClashes": 0,
              "labelsOverLines": 0,
              "diagonals": 0,
              "detachedEnds": 0,
              "width": 40,
              "height": 25,
              "area": 1000
            }

            """.ReplaceLineEndings("\n"), Metrics.ToJson(report));
    }
}
