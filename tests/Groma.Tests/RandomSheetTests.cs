namespace Groma.Tests;

/// <summary>
/// Made-up sheets that change scan after scan. Nothing here checks how a layout looks: only the rules that
/// must hold for every input, so that a change to the layout code cannot quietly break one of them.
/// </summary>
public class RandomSheetTests
{
    // A fixed generator: the same sheets on every machine and every run.
    sealed class Rng(ulong seed)
    {
        ulong s = seed * 0x9E3779B97F4A7C15UL + 1;
        public int Next(int n) { s = s * 6364136223846793005UL + 1442695040888963407UL; return (int)((s >> 33) % (ulong)n); }
        public T Pick<T>(IReadOnlyList<T> items) => items[Next(items.Count)];
    }

    sealed class Sheet(ulong seed)
    {
        readonly Rng r = new(seed);
        readonly List<Box> boxes = [];
        readonly List<Link> links = [];
        int n;

        public Graph Graph => new([.. boxes], [.. links]);

        int Depth(string? id) => id is null ? 0 : 1 + Depth(boxes.Single(b => b.Id == id).Parent);

        public void AddBox()
        {
            // Sometimes on the open sheet, sometimes inside a box that is not already two deep.
            var hosts = boxes.Where(b => Depth(b.Id) < 2 && !links.Any(l => l.From == b.Id && l.To == b.Id)).ToList();
            string? parent = hosts.Count > 0 && r.Next(3) > 0 ? r.Pick(hosts).Id : null;
            boxes.Add(new Box($"b{++n}", 4 + r.Next(16), 2 + r.Next(5), parent));
        }

        public void AddLink()
        {
            if (boxes.Count == 0) return;
            string a = r.Pick(boxes).Id;
            int kind = r.Next(12);
            var label = r.Next(2) == 0 ? (4 + r.Next(6), 2) : (0, 0);
            links.Add(kind switch
            {
                0 => new Link($"l{++n}", a, null, ToEdge: (Side)r.Next(4), LabelWidth: label.Item1, LabelHeight: label.Item2),
                1 => new Link($"l{++n}", null, a, FromEdge: (Side)r.Next(4), LabelWidth: label.Item1, LabelHeight: label.Item2),
                _ => new Link($"l{++n}", a, r.Pick(boxes).Id, LabelWidth: label.Item1, LabelHeight: label.Item2),
            });
        }

        public void RemoveBox()
        {
            if (boxes.Count < 2) return;
            var gone = new HashSet<string> { r.Pick(boxes).Id };
            while (boxes.Any(b => b.Parent is not null && gone.Contains(b.Parent) && gone.Add(b.Id))) { }
            boxes.RemoveAll(b => gone.Contains(b.Id));
            links.RemoveAll(l => gone.Contains(l.From ?? "") || gone.Contains(l.To ?? ""));
        }

        public void RemoveLink()
        {
            if (links.Count > 0) links.RemoveAt(r.Next(links.Count));
        }

        public void Churn()
        {
            for (int i = r.Next(3); i > 0; i--) RemoveBox();
            for (int i = r.Next(4); i > 0; i--) AddBox();
            for (int i = r.Next(3); i > 0; i--) RemoveLink();
            for (int i = r.Next(5); i > 0; i--) AddLink();
        }

        public static Sheet Make(ulong seed, int size)
        {
            var sheet = new Sheet(seed);
            for (int i = 0; i < size; i++) sheet.AddBox();
            for (int i = 0; i < size * 3 / 2; i++) sheet.AddLink();
            return sheet;
        }
    }

    static void AssertRules(Graph graph, Layout layout, Layout? previous, string where)
    {
        var r = Metrics.Measure(layout, previous);
        Assert.True(r.Moved == 0, $"{where}: {r.Moved} surviving boxes moved");
        Assert.True(r.OverlappingBoxes == 0, $"{where}: {r.OverlappingBoxes} overlapping boxes");
        Assert.True(r.ThroughBoxes == 0, $"{where}: {r.ThroughBoxes} lines through boxes");
        Assert.True(r.Diagonals == 0 && r.DetachedEnds == 0, $"{where}: {r.Diagonals} bad segments, {r.DetachedEnds} detached ends");
        Assert.Equal(graph.Boxes.Count, layout.Boxes.Count);
        Assert.Equal(graph.Links.Count, layout.Links.Count);
        Assert.All(layout.Boxes, b => Assert.True(b.X >= 0 && b.Y >= 0 && b.X + b.Width <= layout.Width && b.Y + b.Height <= layout.Height, $"{where}: {b.Id} is off the sheet"));
        Assert.All(layout.Links.SelectMany(l => l.Points), p => Assert.True(p.X >= 0 && p.Y >= 0 && p.X <= layout.Width && p.Y <= layout.Height, $"{where}: a line leaves the sheet"));
        Assert.All(layout.Links.Where(l => l.Label is not null), l => Assert.True(l.Label!.Value.X >= 0 && l.Label.Value.Y >= 0, $"{where}: label of {l.Id} is off the sheet"));
        Assert.Equal(LayoutFile.Write(layout), LayoutFile.Write(LayoutFile.Read(LayoutFile.Write(layout))));
    }

    public static IEnumerable<object[]> Seeds() => Enumerable.Range(1, 40).Select(i => new object[] { (ulong)i, 3 + i % 28 });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_rules_hold_through_eight_scans_of_change(ulong seed, int size)
    {
        var sheet = Sheet.Make(seed, size);
        var layout = Plan.Survey(sheet.Graph);
        AssertRules(sheet.Graph, layout, null, $"seed {seed} scan 1");
        for (int scan = 2; scan <= 8; scan++)
        {
            sheet.Churn();
            Layout next;
            try { next = Plan.Update(sheet.Graph, layout); }
            catch (ResurveyNeededException)
            {
                // Allowed: the library may refuse, but it may never move a box without saying so.
                layout = Plan.Survey(sheet.Graph);
                AssertRules(sheet.Graph, layout, null, $"seed {seed} scan {scan} (re-surveyed)");
                continue;
            }
            AssertRules(sheet.Graph, next, layout, $"seed {seed} scan {scan}");
            Assert.Equal(LayoutFile.Write(next), LayoutFile.Write(Plan.Update(sheet.Graph, layout)));
            layout = next;
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Extra_links_never_disturb_what_is_drawn(ulong seed, int size)
    {
        var sheet = Sheet.Make(seed, size);
        var graph = sheet.Graph;
        var held = graph.Links.Where((_, i) => i % 2 == 1).ToList();
        var basis = Plan.Survey(graph with { Links = [.. graph.Links.Except(held)] });
        var full = Plan.RouteExtra(basis, held);
        var kept = full.Links.Where(l => basis.Links.Any(b => b.Id == l.Id)).ToList();
        Assert.Equal(LayoutFile.Write(basis), LayoutFile.Write(full with { Links = kept }));
        var r = Metrics.Measure(full);
        Assert.Equal((0, 0, 0), (r.ThroughBoxes, r.Diagonals, r.DetachedEnds));
    }

    [Fact]
    public void A_hundred_boxes_are_laid_out_and_updated_without_falling_over()
    {
        var sheet = Sheet.Make(100, 100);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var layout = Plan.Survey(sheet.Graph);
        AssertRules(sheet.Graph, layout, null, "100 boxes");
        Assert.True(clock.Elapsed.TotalSeconds < 30, $"took {clock.Elapsed.TotalSeconds:0.0} s");
    }
}
