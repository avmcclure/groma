using System.Collections.Concurrent;

namespace Groma.Tests;

public static class Fixtures
{
    public static readonly string Root = Find();

    static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "fixtures", "make.py"))) return Path.Combine(dir.FullName, "fixtures");
        throw new DirectoryNotFoundException("fixtures folder not found above " + AppContext.BaseDirectory);
    }

    public static IEnumerable<string> Sheets() => Directory.GetDirectories(Root).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal);

    public static int Steps(string sheet) => Directory.GetFiles(Path.Combine(Root, sheet), "*.graph.json").Length;

    /// <summary>Every scan of every sheet, as (sheet, step) in a stable order.</summary>
    public static IEnumerable<object[]> Scans() => from sheet in Sheets() from step in Enumerable.Range(1, Steps(sheet)) select new object[] { sheet, step };

    public static IEnumerable<object[]> SheetNames() => Sheets().Select(s => new object[] { s });

    public static Graph Graph(string sheet, int step) =>
        LayoutFile.ReadGraph(File.ReadAllText(Path.Combine(Root, sheet, $"{step}.graph.json")));

    /// <summary>A sheet's history: scan 1 surveyed, each later scan an update of the one before.</summary>
    public static List<Layout> Chain(string sheet, Func<Graph, Graph>? rearrange = null, List<int>? resurveyed = null)
    {
        var layouts = new List<Layout>();
        for (int step = 1; step <= Steps(sheet); step++)
        {
            var graph = (rearrange ?? (g => g))(Graph(sheet, step));
            if (step == 1) layouts.Add(Plan.Survey(graph));
            else
                try { layouts.Add(Plan.Update(graph, layouts[^1])); }
                catch (ResurveyNeededException)
                {
                    // What a consumer would do. The scan then counts as re-surveyed: see ResurveysNeeded.
                    layouts.Add(Plan.Survey(graph));
                    resurveyed?.Add(step);
                }
        }
        return layouts;
    }

    static readonly ConcurrentDictionary<string, Lazy<(List<Layout> Layouts, List<int> Resurveyed)>> Cache = new();

    static (List<Layout> Layouts, List<int> Resurveyed) Cached(string sheet) => Cache.GetOrAdd(sheet, s => new(() =>
    {
        var steps = new List<int>();
        return (Chain(s, resurveyed: steps), steps);
    })).Value;

    public static List<Layout> Layouts(string sheet) => Cached(sheet).Layouts;

    /// <summary>The scans where the incremental layout had to give up and ask for a fresh one.</summary>
    public static List<int> ResurveysNeeded(string sheet) => Cached(sheet).Resurveyed;
}

/// <summary>
/// The safety net for changing the layout code. Every scan of every fixture is laid out and compared, byte for
/// byte, with the stored result. When a change is meant to alter the output, run the tests once with
/// GROMA_ACCEPT=1 and review the diff of fixtures/*/expected: the layouts, their measures and their pictures.
/// </summary>
public class StoredOutputTests
{
    static void Matches(string sheet, string file, string actual)
    {
        var path = Path.Combine(Fixtures.Root, sheet, "expected", file);
        if (Environment.GetEnvironmentVariable("GROMA_ACCEPT") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return;
        }
        Assert.True(File.Exists(path), $"{path} is missing. Run the tests once with GROMA_ACCEPT=1 to store it.");
        Assert.Equal(File.ReadAllText(path), actual);
    }

    [Theory]
    [MemberData(nameof(Fixtures.Scans), MemberType = typeof(Fixtures))]
    public void The_layout_is_byte_for_byte_the_stored_one(string sheet, int step)
    {
        Matches(sheet, $"{step}.layout.json", LayoutFile.Write(Fixtures.Layouts(sheet)[step - 1]));
    }

    [Theory]
    [MemberData(nameof(Fixtures.Scans), MemberType = typeof(Fixtures))]
    public void The_measures_are_the_stored_ones(string sheet, int step)
    {
        var layouts = Fixtures.Layouts(sheet);
        Matches(sheet, $"{step}.metrics.json", Metrics.ToJson(Metrics.Measure(layouts[step - 1], step > 1 ? layouts[step - 2] : null)));
    }

    [Theory]
    [MemberData(nameof(Fixtures.Scans), MemberType = typeof(Fixtures))]
    public void The_picture_is_the_stored_one(string sheet, int step)
    {
        var layouts = Fixtures.Layouts(sheet);
        Matches(sheet, $"{step}.svg", Svg.Render(layouts[step - 1], step > 1 ? layouts[step - 2] : null));
    }
}

/// <summary>What must hold on every scan of every fixture, whatever the layout looks like.</summary>
public class FixtureRuleTests
{
    [Theory]
    [MemberData(nameof(Fixtures.Scans), MemberType = typeof(Fixtures))]
    public void The_hard_measures_are_zero(string sheet, int step)
    {
        var layouts = Fixtures.Layouts(sheet);
        var r = Metrics.Measure(layouts[step - 1], step > 1 ? layouts[step - 2] : null);
        Assert.Equal((0, 0, 0, 0, 0), (r.Moved, r.ThroughBoxes, r.OverlappingBoxes, r.Diagonals, r.DetachedEnds));
    }

    [Theory]
    [MemberData(nameof(Fixtures.Scans), MemberType = typeof(Fixtures))]
    public void Every_box_and_link_of_the_graph_is_in_the_layout(string sheet, int step)
    {
        var (graph, layout) = (Fixtures.Graph(sheet, step), Fixtures.Layouts(sheet)[step - 1]);
        Assert.Equal(graph.Boxes.Select(b => b.Id).Order(StringComparer.Ordinal), layout.Boxes.Select(b => b.Id));
        Assert.Equal(graph.Links.Select(l => l.Id).Order(StringComparer.Ordinal), layout.Links.Select(l => l.Id));
        Assert.All(layout.Links, l => Assert.True(l.Points.Count >= 2));
    }

    [Theory]
    [MemberData(nameof(Fixtures.Scans), MemberType = typeof(Fixtures))]
    public void Surviving_boxes_that_hold_nothing_keep_their_whole_rectangle(string sheet, int step)
    {
        if (step == 1) return;
        var layouts = Fixtures.Layouts(sheet);
        var holders = layouts[step - 1].Boxes.Where(b => b.Parent is not null).Select(b => b.Parent).ToHashSet();
        var before = layouts[step - 2].Boxes.ToDictionary(b => b.Id);
        foreach (var b in layouts[step - 1].Boxes.Where(b => before.ContainsKey(b.Id)))
        {
            Assert.Equal((before[b.Id].X, before[b.Id].Y), (b.X, b.Y));
            if (holders.Contains(b.Id)) Assert.True(b.Width >= before[b.Id].Width && b.Height >= before[b.Id].Height, $"{b.Id} shrank");
        }
        Assert.True(layouts[step - 1].Width >= layouts[step - 2].Width && layouts[step - 1].Height >= layouts[step - 2].Height);
    }

    [Theory]
    [MemberData(nameof(Fixtures.SheetNames), MemberType = typeof(Fixtures))]
    public void Running_again_gives_the_same_bytes(string sheet)
    {
        Assert.Equal(Fixtures.Layouts(sheet).Select(LayoutFile.Write), Fixtures.Chain(sheet).Select(LayoutFile.Write));
    }

    [Theory]
    [MemberData(nameof(Fixtures.SheetNames), MemberType = typeof(Fixtures))]
    public void The_order_of_the_input_does_not_change_the_bytes(string sheet)
    {
        var reversed = Fixtures.Chain(sheet, g => new Graph([.. g.Boxes.Reverse()], [.. g.Links.Reverse()]));
        Assert.Equal(Fixtures.Layouts(sheet).Select(LayoutFile.Write), reversed.Select(LayoutFile.Write));
    }

    [Theory]
    [MemberData(nameof(Fixtures.SheetNames), MemberType = typeof(Fixtures))]
    public void A_layout_read_back_from_its_file_updates_to_the_same_bytes(string sheet)
    {
        if (Fixtures.Steps(sheet) < 2) return;
        var reread = LayoutFile.Read(LayoutFile.Write(Fixtures.Layouts(sheet)[0]));
        Assert.Equal(LayoutFile.Write(Fixtures.Layouts(sheet)[1]), LayoutFile.Write(Plan.Update(Fixtures.Graph(sheet, 2), reread)));
    }

    [Theory]
    [MemberData(nameof(Fixtures.SheetNames), MemberType = typeof(Fixtures))]
    public void Extra_links_are_drawn_without_disturbing_a_byte_of_what_was_there(string sheet)
    {
        var graph = Fixtures.Graph(sheet, 1);
        var held = graph.Links.Where((_, i) => i % 3 == 2).ToList();
        var basis = Plan.Survey(graph with { Links = [.. graph.Links.Except(held)] });
        var full = Plan.RouteExtra(basis, held);

        Assert.Equal(graph.Links.Count, full.Links.Count);
        Assert.Equal(basis.Boxes, full.Boxes);
        var before = LayoutFile.Write(basis).Split('\n').Where(line => line.Contains("\"points\"")).Select(line => line.TrimEnd(','));
        var after = LayoutFile.Write(full).Split('\n').Select(line => line.TrimEnd(',')).ToHashSet();
        Assert.All(before, line => Assert.Contains(line, after));
        var r = Metrics.Measure(full);
        Assert.Equal((0, 0, 0), (r.ThroughBoxes, r.Diagonals, r.DetachedEnds));
    }

    [Theory]
    [MemberData(nameof(Fixtures.SheetNames), MemberType = typeof(Fixtures))]
    public void The_rules_also_hold_when_links_flow_top_to_bottom(string sheet)
    {
        var down = new Options { Direction = Direction.Down };
        Layout? previous = null;
        for (int step = 1; step <= Math.Min(Fixtures.Steps(sheet), 3); step++)
        {
            var graph = Fixtures.Graph(sheet, step);
            Layout layout;
            try { layout = previous is null ? Plan.Survey(graph, down) : Plan.Update(graph, previous, down); }
            catch (ResurveyNeededException) { (layout, previous) = (Plan.Survey(graph, down), null); }
            var r = Metrics.Measure(layout, previous);
            Assert.Equal((0, 0, 0, 0, 0), (r.Moved, r.ThroughBoxes, r.OverlappingBoxes, r.Diagonals, r.DetachedEnds));
            Assert.Equal(LayoutFile.Write(layout), LayoutFile.Write(previous is null ? Plan.Survey(graph, down) : Plan.Update(graph, previous, down)));
            previous = layout;
        }
    }

    [Fact]
    public void No_fixture_scan_needs_a_re_survey()
    {
        // Every entry here is a scan where a surviving box had to move. Keep the list empty; if a change
        // makes it grow, that change has made incremental layout worse.
        var needed = Fixtures.Sheets().SelectMany(s => Fixtures.ResurveysNeeded(s).Select(step => $"{s} scan {step}"));
        Assert.Empty(needed);
    }

    [Fact]
    public void The_fixtures_cover_the_cases_the_prototypes_never_had()
    {
        var all = Fixtures.Scans().Select(s => Fixtures.Graph((string)s[0], (int)s[1])).ToList();
        bool TwoDeep(Graph g) => g.Boxes.Any(b => b.Parent is not null && g.Boxes.Single(p => p.Id == b.Parent).Parent is not null);
        bool ToContainer(Graph g) => g.Links.Any(l => g.Boxes.Any(b => b.Parent == l.To || b.Parent == l.From));
        Assert.Contains(all, TwoDeep);
        Assert.Contains(all, g => g.Links.Any(l => l.ToEdge is not null));
        Assert.Contains(all, g => g.Links.Any(l => l.FromEdge is not null));
        Assert.Contains(all, ToContainer);
        Assert.Equal(37, all.Count);
    }
}
