using static Groma.Tests.Draw;

namespace Groma.Tests;

public class LayoutFileWriteTests
{
    const string Canonical =
        """
        {
          "version": 1,
          "width": 96,
          "height": 60,
          "boxes": [
            { "id": "billing", "x": 40, "y": 4, "width": 12, "height": 4 },
            { "id": "sales", "x": 4, "y": 4, "width": 18, "height": 12 },
            { "id": "sales.orders", "parent": "sales", "x": 7, "y": 8, "width": 12, "height": 4 }
          ],
          "links": [
            { "id": "l1", "from": "sales.orders", "to": "billing", "points": [[19, 10], [30, 10], [30, 6], [40, 6]], "label": { "x": 22, "y": 9, "width": 6, "height": 1 } },
            { "id": "l2", "from": "sales.orders", "toEdge": "right", "points": [[19, 11], [96, 11]] }
          ]
        }

        """;

    static Layout Example() => new(96, 60,
        [B("sales.orders", 7, 8, 12, 4, "sales"), B("sales", 4, 4, 18, 12), B("billing", 40, 4, 12, 4)],
        [
            R("l2", "sales.orders", null, (19, 11), (96, 11)) with { ToEdge = Side.Right },
            R("l1", "sales.orders", "billing", (19, 10), (30, 10), (30, 6), (40, 6)) with { Label = new Rect(22, 9, 6, 1) },
        ]);

    [Fact]
    public void Writes_the_canonical_bytes_sorted_by_id()
    {
        Assert.Equal(Canonical.ReplaceLineEndings("\n"), LayoutFile.Write(Example()));
    }

    [Fact]
    public void Reading_then_writing_gives_back_the_same_bytes()
    {
        var text = Canonical.ReplaceLineEndings("\n");
        Assert.Equal(text, LayoutFile.Write(LayoutFile.Read(text)));
    }

    [Fact]
    public void Ids_sort_by_character_code_not_by_culture()
    {
        var text = LayoutFile.Write(new Layout(10, 10, [B("b", 0, 0, 1, 1), B("B", 2, 0, 1, 1), B("a", 4, 0, 1, 1), B("_", 6, 0, 1, 1)], []));
        var order = string.Concat(LayoutFile.Read(text).Boxes.Select(b => b.Id));
        Assert.Equal("B_ab", order);
    }

    [Fact]
    public void Quotes_and_slashes_in_ids_survive_and_other_characters_are_left_readable()
    {
        var layout = new Layout(10, 10, [B("db/t \"x\" > é", 0, 0, 1, 1)], []);
        var text = LayoutFile.Write(layout);
        Assert.Contains("db/t \\\"x\\\" > é", text);
        Assert.Equal("db/t \"x\" > é", LayoutFile.Read(text).Boxes[0].Id);
    }

    [Fact]
    public void An_empty_layout_still_writes_and_reads()
    {
        var text = LayoutFile.Write(new Layout(0, 0, [], []));
        Assert.Equal(text, LayoutFile.Write(LayoutFile.Read(text)));
    }

    [Fact]
    public void A_layout_of_another_version_is_refused()
    {
        var ex = Assert.Throws<GromaInputException>(() => LayoutFile.Read("""{ "version": 2, "width": 1, "height": 1, "boxes": [], "links": [] }"""));
        Assert.Contains("version", ex.Message);
    }

    [Fact]
    public void Text_that_is_not_json_is_refused_as_bad_input()
    {
        Assert.Throws<GromaInputException>(() => LayoutFile.Read("not json"));
    }
}

public class GraphReadTests
{
    static Graph Read(string boxes, string links = "") =>
        LayoutFile.ReadGraph($$"""{ "boxes": [{{boxes}}], "links": [{{links}}] }""");

    static string Refused(string boxes, string links = "") =>
        Assert.Throws<GromaInputException>(() => Read(boxes, links)).Message;

    const string AB = """{ "id": "a", "width": 4, "height": 2 }, { "id": "b", "width": 4, "height": 2 }""";

    [Fact]
    public void Reads_boxes_links_edges_and_label_sizes()
    {
        var g = Read(
            AB + """, { "id": "a.t", "width": 3, "height": 1, "parent": "a" }""",
            """{ "id": "l1", "from": "a.t", "to": "b", "labelWidth": 6, "labelHeight": 1 }, { "id": "l2", "fromEdge": "left", "to": "b" }""");
        Assert.Equal(new Box("a.t", 3, 1, "a"), g.Boxes[2]);
        Assert.Equal(new Link("l1", "a.t", "b", null, null, 6, 1), g.Links[0]);
        Assert.Equal(new Link("l2", null, "b", Side.Left), g.Links[1]);
    }

    [Fact]
    public void A_duplicate_box_id_is_refused_by_name()
    {
        Assert.Contains("'a'", Refused(AB + """, { "id": "a", "width": 1, "height": 1 }"""));
    }

    [Fact]
    public void A_duplicate_link_id_is_refused_by_name()
    {
        Assert.Contains("'l1'", Refused(AB, """{ "id": "l1", "from": "a", "to": "b" }, { "id": "l1", "from": "b", "to": "a" }"""));
    }

    [Fact]
    public void An_unknown_parent_is_refused_by_name()
    {
        Assert.Contains("'ghost'", Refused("""{ "id": "a", "width": 4, "height": 2, "parent": "ghost" }"""));
    }

    [Fact]
    public void A_box_that_is_its_own_ancestor_is_refused()
    {
        Assert.Contains("'a'", Refused("""{ "id": "a", "width": 4, "height": 2, "parent": "b" }, { "id": "b", "width": 4, "height": 2, "parent": "a" }"""));
    }

    [Fact]
    public void Two_levels_of_nesting_are_accepted()
    {
        var g = Read("""{ "id": "s", "width": 4, "height": 2 }, { "id": "d", "width": 4, "height": 2, "parent": "s" }, { "id": "t", "width": 4, "height": 2, "parent": "d" }""");
        Assert.Equal(3, g.Boxes.Count);
    }

    [Fact]
    public void Three_levels_of_nesting_are_refused_naming_the_deepest_box()
    {
        Assert.Contains("'c'", Refused(
            """{ "id": "s", "width": 4, "height": 2 }, { "id": "d", "width": 4, "height": 2, "parent": "s" }, { "id": "t", "width": 4, "height": 2, "parent": "d" }, { "id": "c", "width": 1, "height": 1, "parent": "t" }"""));
    }

    [Fact]
    public void A_box_with_no_size_is_refused_by_name()
    {
        Assert.Contains("'a'", Refused("""{ "id": "a", "width": 0, "height": 2 }"""));
    }

    [Fact]
    public void A_link_to_an_unknown_box_is_refused_naming_the_link()
    {
        Assert.Contains("'l1'", Refused(AB, """{ "id": "l1", "from": "a", "to": "ghost" }"""));
    }

    [Fact]
    public void A_link_end_given_as_both_box_and_edge_is_refused()
    {
        Assert.Contains("'l1'", Refused(AB, """{ "id": "l1", "from": "a", "to": "b", "toEdge": "top" }"""));
    }

    [Fact]
    public void A_link_with_a_missing_end_is_refused()
    {
        Assert.Contains("'l1'", Refused(AB, """{ "id": "l1", "from": "a" }"""));
    }

    [Fact]
    public void A_link_from_one_sheet_edge_to_another_is_refused()
    {
        Assert.Contains("'l1'", Refused(AB, """{ "id": "l1", "fromEdge": "left", "toEdge": "right" }"""));
    }

    [Fact]
    public void An_unknown_edge_name_is_refused()
    {
        Assert.Contains("'l1'", Refused(AB, """{ "id": "l1", "from": "a", "toEdge": "north" }"""));
    }

    [Fact]
    public void A_box_without_an_id_is_refused()
    {
        Assert.Contains("id", Refused("""{ "width": 4, "height": 2 }"""));
    }

    [Fact]
    public void Text_that_is_not_json_is_refused_as_bad_input()
    {
        Assert.Throws<GromaInputException>(() => LayoutFile.ReadGraph("{"));
    }
}
