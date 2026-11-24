namespace Groma;

/// <summary>
/// Arranges one group of sibling boxes for a fresh layout, by the layered method: turn any loops round, put the
/// boxes in columns so that links run one way, order each column to uncross the links, then line boxes up with
/// what they link to. The stages follow Gansner, Koutsofios, North and Vo, "A Technique for Drawing Directed
/// Graphs" (1993), each in its simplest adequate form: depth-first loop breaking in input order, longest-path
/// columns, averaged-position sweeps with adjacent swaps, and median alignment in place of their network simplex.
/// "Main" is the direction links flow in; "cross" is the other one.
/// </summary>
static class Layered
{
    const int Sweeps = 24; // the cap Graphviz dot uses

    /// <param name="fold">Fold a tall drawing into bands; only the sheet itself is, not the inside of a container.</param>
    /// <param name="lean">For each box, links it sends out of the group minus links it receives from outside.</param>
    public static (int X, int Y)[] Arrange((int W, int H)[] size, IReadOnlyList<(int From, int To)> links, int[] lean, Options o, bool fold = false)
    {
        int n = size.Length, gap = o.BoxGap;
        bool right = o.Direction == Direction.Right;
        int Main(int v) => right ? size[v].W : size[v].H;
        int Cross(int v) => right ? size[v].H : size[v].W;

        // ---- 1. loops: walk depth-first in input order and turn round every link that points back up the walk
        var outs = new SortedSet<int>[n];
        for (int v = 0; v < n; v++) outs[v] = [];
        foreach (var (a, b) in links) if (a != b) outs[a].Add(b);
        var edges = new SortedSet<(int, int)>();
        var state = new int[n];
        void Walk(int v)
        {
            state[v] = 1;
            foreach (int w in outs[v])
            {
                if (state[w] == 1) edges.Add((w, v));
                else
                {
                    edges.Add((v, w));
                    if (state[w] == 0) Walk(w);
                }
            }
            state[v] = 2;
        }
        for (int v = 0; v < n; v++) if (state[v] == 0) Walk(v);

        var ins = new List<int>[n];
        var nexts = new List<int>[n];
        for (int v = 0; v < n; v++) (ins[v], nexts[v]) = ([], []);
        foreach (var (a, b) in edges) { nexts[a].Add(b); ins[b].Add(a); }

        // ---- 2. columns: each box one column after the latest box that links to it
        var column = new int[n];
        var waiting = ins.Select(i => i.Count).ToArray();
        var ready = new SortedSet<int>(Enumerable.Range(0, n).Where(v => waiting[v] == 0));
        var sorted = new List<int>();
        while (ready.Count > 0)
        {
            int v = ready.Min;
            ready.Remove(v);
            sorted.Add(v);
            foreach (int w in nexts[v])
            {
                column[w] = Math.Max(column[w], column[v] + 1);
                if (--waiting[w] == 0) ready.Add(w);
            }
        }
        // A box nothing links to would otherwise sit in the first column however late its target is.
        foreach (int v in sorted.AsEnumerable().Reverse())
            if (ins[v].Count == 0 && nexts[v].Count > 0) column[v] = nexts[v].Min(w => column[w]) - 1;

        var linked = Enumerable.Range(0, n).Where(v => ins[v].Count + nexts[v].Count > 0).ToList();
        var loose = Enumerable.Range(0, n).Where(v => ins[v].Count + nexts[v].Count == 0).ToList();
        var main = new int[n];
        var cross = new int[n];
        int crossEnd = 0;

        if (linked.Count > 0)
        {
            // ---- 3. a link that skips columns gets a stand-in point in each column it passes
            int columns = linked.Max(v => column[v]) + 1;
            var col = new List<int>(column);            // node -> column, stand-ins appended after the boxes
            var before = new List<List<int>>();         // node -> its neighbours one column back
            var after = new List<List<int>>();
            for (int v = 0; v < n; v++) { before.Add([]); after.Add([]); }
            foreach (var (a, b) in edges)
            {
                int from = a;
                for (int c = column[a] + 1; c < column[b]; c++)
                {
                    int d = col.Count;
                    col.Add(c); before.Add([]); after.Add([]);
                    after[from].Add(d); before[d].Add(from);
                    from = d;
                }
                after[from].Add(b); before[b].Add(from);
            }
            var order = new List<int>[columns];
            for (int c = 0; c < columns; c++) order[c] = [];
            foreach (int v in linked) order[column[v]].Add(v);
            for (int d = n; d < col.Count; d++) order[col[d]].Add(d);
            var place = new int[col.Count];
            void Renumber() { foreach (var list in order) for (int i = 0; i < list.Count; i++) place[list[i]] = i; }
            Renumber();

            // ---- 4. order within columns: sweep back and forth, moving each node to the average place of its
            // neighbours, then swap neighbours wherever that removes a crossing; keep the best order seen
            int Inversions(List<int> a, List<int> b) => a.Sum(x => b.Count(y => place[x] > place[y]));
            int Crossings()
            {
                int total = 0;
                foreach (var list in order)
                    for (int i = 0; i < list.Count; i++)
                        for (int j = i + 1; j < list.Count; j++)
                            total += Inversions(after[list[i]], after[list[j]]);
                return total;
            }
            void Reorder(int c, List<List<int>> toward)
            {
                order[c] = [.. order[c]
                    .OrderBy(v => toward[v].Count == 0 ? place[v] * 1000 : toward[v].Sum(u => place[u]) * 1000 / toward[v].Count)
                    .ThenBy(v => place[v])];
                Renumber();
            }
            void Swaps()
            {
                for (int round = 0, improved = 1; improved > 0 && round < 10; round++)
                {
                    improved = 0;
                    foreach (var list in order)
                        for (int i = 0; i + 1 < list.Count; i++)
                        {
                            int u = list[i], v = list[i + 1];
                            int now = Inversions(before[u], before[v]) + Inversions(after[u], after[v]);
                            int swapped = Inversions(before[v], before[u]) + Inversions(after[v], after[u]);
                            if (swapped >= now) continue;
                            (list[i], list[i + 1]) = (v, u);
                            (place[u], place[v]) = (i + 1, i);
                            improved++;
                        }
                }
            }
            var best = order.Select(l => l.ToList()).ToArray();
            int fewest = Crossings();
            for (int sweep = 0; sweep < Sweeps && fewest > 0; sweep++)
            {
                if (sweep % 2 == 0) for (int c = 1; c < columns; c++) Reorder(c, before);
                else for (int c = columns - 2; c >= 0; c--) Reorder(c, after);
                Swaps();
                int count = Crossings();
                if (count < fewest) (best, fewest) = (order.Select(l => l.ToList()).ToArray(), count);
            }
            order = best;
            Renumber();

            // ---- 5. positions across: stack each column in order, then pull each node level with the middle
            // of its neighbours without changing the order or closing the gaps
            int Size(int v) => v < n ? Cross(v) : 0;
            int Between(int u, int v) => u < n && v < n ? gap : 2;
            var at = new int[col.Count];
            foreach (var list in order)
                for (int i = 1; i < list.Count; i++)
                    at[list[i]] = at[list[i - 1]] + Size(list[i - 1]) + Between(list[i - 1], list[i]);
            void Align(List<int> list, Func<int, IEnumerable<int>> neighbours)
            {
                if (list.Count == 0) return;
                var want = list.Select(v =>
                {
                    var mids = neighbours(v).Select(u => 2 * at[u] + Size(u)).Order().ToList();
                    return mids.Count == 0 ? at[v] : ((mids[(mids.Count - 1) / 2] + mids[mids.Count / 2]) / 2 - Size(v)) >> 1;
                }).ToArray();
                // The highest and the lowest arrangement that honour every wish they can; halfway between
                // them keeps the order and the gaps and favours neither end.
                var lo = (int[])want.Clone();
                var hi = (int[])want.Clone();
                for (int i = 1; i < list.Count; i++)
                    lo[i] = Math.Max(lo[i], lo[i - 1] + Size(list[i - 1]) + Between(list[i - 1], list[i]));
                for (int i = list.Count - 2; i >= 0; i--)
                    hi[i] = Math.Min(hi[i], hi[i + 1] - Size(list[i]) - Between(list[i], list[i + 1]));
                for (int i = 0; i < list.Count; i++) at[list[i]] = (lo[i] + hi[i]) >> 1;
            }
            for (int round = 0; round < 4; round++)
            {
                for (int c = 1; c < columns; c++) Align(order[c], v => before[v]);
                for (int c = columns - 2; c >= 0; c--) Align(order[c], v => after[v]);
            }
            for (int c = 0; c < columns; c++) Align(order[c], v => before[v].Concat(after[v]));

            int top = linked.Min(v => at[v]);
            int start = 0;
            for (int c = 0; c < columns; c++)
            {
                foreach (int v in order[c].Where(v => v < n)) (main[v], cross[v]) = (start, at[v] - top);
                start += order[c].Where(v => v < n).Select(Main).DefaultIfEmpty(0).Max() + 2 * gap;
            }
            if (fold) Fold(linked, main, cross, Main, Cross, right, 2 * gap);
            crossEnd = linked.Max(v => cross[v] + Cross(v)) + gap;
        }

        // ---- boxes with no links at this level: a plain grid after everything else. Links that leave the
        // group still count for something here: boxes fed from outside take the side links come in from,
        // and boxes that feed out take the far side.
        loose = [.. loose.OrderBy(v => Math.Sign(lean[v])).ThenBy(v => v)];
        // As many columns as make the grid nearest to square on the sheet, whatever shape the boxes are.
        int across = 1, deep = loose.Count;
        if (loose.Count > 0)
        {
            int widest = loose.Max(Main) + gap, tallest = loose.Max(Cross) + gap;
            long Lopsided(int columns) => Math.Abs((long)columns * widest - (long)((loose.Count + columns - 1) / columns) * tallest);
            for (int columns = 2; columns <= loose.Count; columns++)
                if (Lopsided(columns) < Lopsided(across)) across = columns;
            deep = (loose.Count + across - 1) / across;
        }
        for (int k = 0, offset = 0; k * deep < loose.Count; k++)
        {
            var line = loose.Skip(k * deep).Take(deep).ToList();
            int place = crossEnd;
            foreach (int v in line)
            {
                (main[v], cross[v]) = (offset, place);
                place += Cross(v) + gap;
            }
            offset += line.Max(Main) + gap;
        }

        return [.. Enumerable.Range(0, n).Select(v => right ? (main[v], cross[v]) : (cross[v], main[v]))];
    }

    /// <summary>The shape, width over height, a sheet is folded towards: a screen's, roughly.</summary>
    const double Landscape = 1.5;

    /// <summary>
    /// A few columns of many boxes make a sheet far longer across than along. Such a drawing is cut across, only
    /// where no box spans the cut, into bands set side by side along the main direction, like a long text set in
    /// columns. Linked boxes are already level with each other, so most links stay within their band.
    /// </summary>
    static void Fold(List<int> boxes, int[] main, int[] cross, Func<int, int> mainSize, Func<int, int> crossSize, bool right, int between)
    {
        int length = boxes.Max(v => main[v] + mainSize(v)), span = boxes.Max(v => cross[v] + crossSize(v));
        double Miss(int bands)
        {
            double along = bands * (length + between) - between, across = (double)span / bands;
            return Math.Abs(Math.Log((right ? along / across : across / along) / Landscape));
        }
        if (Miss(1) < Math.Log(Landscape)) return;   // only a drawing taller than it is wide is folded
        int count = 1;
        while (count < boxes.Count && Miss(count + 1) < Miss(count)) count++;
        if (count == 1) return;

        // A cut may fall at the near edge of any box that no other box overlaps there.
        var free = boxes.Select(v => cross[v]).Where(c => c > 0 && boxes.All(u => cross[u] >= c || cross[u] + crossSize(u) <= c)).Distinct().Order().ToList();
        var cuts = new List<int>();
        for (int k = 1; k < count; k++)
        {
            int target = span * k / count;
            var options = free.Where(c => c > cuts.LastOrDefault()).ToList();
            if (options.Count == 0) break;
            cuts.Add(options.MinBy(c => Math.Abs(c - target)));
        }
        foreach (int v in boxes)
        {
            int band = cuts.Count(c => c <= cross[v]);
            if (band == 0) continue;
            main[v] += band * (length + between);
            cross[v] -= cuts[band - 1];
        }
    }
}
