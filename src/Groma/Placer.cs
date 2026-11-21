namespace Groma;

/// <summary>
/// Decides where boxes go. A fresh layout arranges everything; an incremental one copies every surviving box
/// and finds free space for the new ones near what they link to. No published algorithm covers the incremental
/// case with sized boxes and containers: the rules here are this library's own (see docs/placement.md).
/// </summary>
sealed class Placer
{
    readonly Graph graph;
    readonly Options options;
    readonly Dictionary<string, Box> box;
    readonly Dictionary<string, List<string>> kids = new(StringComparer.Ordinal);
    readonly List<string> top = [];
    readonly Dictionary<string, Rect> at = new(StringComparer.Ordinal);

    int Gap => options.BoxGap;
    int Pad => options.ContainerPadding;

    /// <param name="graph">Already checked, with boxes and links in id order.</param>
    public Placer(Graph graph, Options options)
    {
        (this.graph, this.options) = (graph, options);
        box = graph.Boxes.ToDictionary(b => b.Id, StringComparer.Ordinal);
        foreach (var b in graph.Boxes) kids[b.Id] = [];
        foreach (var b in graph.Boxes) (b.Parent is null ? top : kids[b.Parent]).Add(b.Id);
    }

    public Dictionary<string, Rect> Survey()
    {
        var (_, _, items) = Arrange(top);
        foreach (var (id, r) in items) at[id] = r with { X = r.X + Gap, Y = r.Y + Gap };
        return at;
    }

    public Dictionary<string, Rect> Update(Layout previous)
    {
        var was = previous.Boxes.ToDictionary(b => b.Id, StringComparer.Ordinal);
        foreach (var b in graph.Boxes)
            if (was.TryGetValue(b.Id, out var p))
                at[b.Id] = kids[b.Id].Count > 0 ? p.Rect : new Rect(p.X, p.Y, b.Width, b.Height);

        // A new box whose parent is also new arrives inside its parent's block; the rest are placed one by one.
        foreach (var b in graph.Boxes)
            if (!at.ContainsKey(b.Id) && (b.Parent is null || was.ContainsKey(b.Parent)))
                Place(b);
        Check();
        return at;
    }

    // ---- blocks: a box with everything inside it, laid out relative to its own corner ----

    (int W, int H, List<(string Id, Rect R)> Items) Block(string id)
    {
        var b = box[id];
        if (kids[id].Count == 0) return (b.Width, b.Height, [(id, new Rect(0, 0, b.Width, b.Height))]);
        var (iw, ih, inner) = Arrange(kids[id]);
        // Spare room for one more row of plain boxes along the bottom: boxes added later then fit without the container
        // having to grow, which its neighbours may not allow. (The idea of reserving room in advance is
        // Frishman and Tal's; the amount is ours.)
        int spare = Math.Min(inner.Where(i => box[i.Id].Parent == id).Min(i => i.R.Height), Gap) + Gap;
        int w = Math.Max(b.Width, iw + 2 * Pad), h = b.Height + ih + spare + 2 * Pad;
        var items = new List<(string, Rect)> { (id, new Rect(0, 0, w, h)) };
        items.AddRange(inner.Select(i => (i.Id, i.R with { X = i.R.X + Pad, Y = i.R.Y + b.Height + Pad })));
        return (w, h, items);
    }

    // Lays a group of siblings out among themselves. A link counts at this level when its two ends lie in
    // two different members of the group, however deep inside them; links that leave the group do not.
    (int W, int H, List<(string Id, Rect R)> Items) Arrange(List<string> members)
    {
        var blocks = members.Select(Block).ToList();
        var memberOf = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < blocks.Count; i++)
            foreach (var (id, _) in blocks[i].Items) memberOf[id] = i;
        var links = new List<(int, int)>();
        foreach (var l in graph.Links)
            if (l.From is not null && l.To is not null && memberOf.TryGetValue(l.From, out int a) && memberOf.TryGetValue(l.To, out int b))
                links.Add((a, b));
        var spots = Layered.Arrange([.. blocks.Select(b => (b.W, b.H))], links, options);

        var items = new List<(string, Rect)>();
        int w = 0, h = 0;
        for (int i = 0; i < blocks.Count; i++)
        {
            var (x, y) = spots[i];
            items.AddRange(blocks[i].Items.Select(it => (it.Id, it.R with { X = it.R.X + x, Y = it.R.Y + y })));
            (w, h) = (Math.Max(w, x + blocks[i].W), Math.Max(h, y + blocks[i].H));
        }
        return (w, h, items);
    }

    // ---- incremental placement ----

    void Place(Box b)
    {
        var (w, h, items) = Block(b.Id);
        var ideal = Ideal(items);
        Rect spot;
        if (b.Parent is null)
        {
            var others = top.Where(at.ContainsKey).Select(id => at[id]).ToList();
            (int x, int y) c = ideal ?? (others.Count == 0 ? Gap + w / 2 : others.Max(r => r.Right) + Gap + w / 2, Gap + h / 2);
            spot = Nearest(w, h, c, others, null)!.Value;
        }
        else spot = Inside(b, w, h, ideal);
        foreach (var (id, r) in items) at[id] = r with { X = r.X + spot.X, Y = r.Y + spot.Y };
    }

    // The middle of the already-placed boxes that anything in the block links to.
    (int X, int Y)? Ideal(List<(string Id, Rect R)> items)
    {
        var mine = items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        long sx = 0, sy = 0, n = 0;
        foreach (var l in graph.Links)
        {
            if (l.From is null || l.To is null || mine.Contains(l.From) == mine.Contains(l.To)) continue;
            if (!at.TryGetValue(mine.Contains(l.From) ? l.To : l.From, out var r)) continue;
            (sx, sy, n) = (sx + r.X + r.Width / 2, sy + r.Y + r.Height / 2, n + 1);
        }
        return n == 0 ? null : ((int)(sx / n), (int)(sy / n));
    }

    // The free slot nearest to where the box would like to be. Only slots lined up with a box already there
    // are tried (its left side, its right side plus the gap, and so on): that keeps new boxes in rows and
    // columns with their neighbours and wastes less space than trying every grid position. The slot beyond
    // the right-most box on the top row is always free, so a box on the open sheet always finds a place.
    // With no ideal point, the first free slot in reading order is returned.
    Rect? Nearest(int w, int h, (int X, int Y)? ideal, List<Rect> others, Rect? room)
    {
        var xs = new SortedSet<int> { room?.X ?? Gap };
        var ys = new SortedSet<int> { room?.Y ?? Gap };
        foreach (var o in others)
        {
            xs.UnionWith([o.X, o.Right + Gap, o.X - Gap - w, o.Right - w]);
            ys.UnionWith([o.Y, o.Bottom + Gap, o.Y - Gap - h, o.Bottom - h]);
        }
        Rect? best = null;
        long bestD = long.MaxValue;
        foreach (int y in ys)
            foreach (int x in xs)
            {
                var c = new Rect(x, y, w, h);
                if (room is { } r ? !Within(c, r) : x < Gap || y < Gap) continue;
                long d = ideal is { } i ? Sq(x + w / 2 - i.X) + Sq(y + h / 2 - i.Y) : 0;
                if (d < bestD && !others.Any(o => Hit(c, o, Gap))) (best, bestD) = (c, d);
            }
        return best;
    }

    // Free space inside the container if there is any; otherwise the container grows right, then down.
    Rect Inside(Box b, int w, int h, (int X, int Y)? ideal)
    {
        string p = b.Parent!;
        var room = Interior(p);
        var siblings = kids[p].Where(at.ContainsKey).Select(id => at[id]).ToList();
        var (ix, iy) = ideal ?? (room.X + room.Width / 2, room.Y + room.Height / 2);

        // Inside a container the first free slot in reading order is taken, whatever the box links to:
        // going for the nearest slot instead left gaps too narrow to use and filled containers early.
        if (Nearest(w, h, null, siblings, room) is { } free) return free;

        var outline = at[p];
        int x0 = siblings.Count == 0 ? room.X : Math.Max(room.X, siblings.Max(s => s.Right) + Gap);
        int y0 = siblings.Count == 0 ? room.Y : Math.Max(room.Y, siblings.Max(s => s.Bottom) + Gap);
        Rect[] tries =
        [
            new(x0, Math.Clamp(iy - h / 2, room.Y, Math.Max(room.Y, room.Bottom - h)), w, h),
            new(Math.Clamp(ix - w / 2, room.X, Math.Max(room.X, room.Right - w)), y0, w, h),
        ];
        foreach (var c in tries)
        {
            var grown = new Dictionary<string, Rect>(StringComparer.Ordinal);
            var bigger = Stretch(outline, c.Right + Pad, c.Bottom + Pad);
            if (siblings.Any(s => Hit(c, s, Gap)) || !Grow(p, bigger, grown)) continue;
            foreach (var (id, r) in grown) at[id] = r;
            return c;
        }
        throw new ResurveyNeededException(
            $"There is no room for '{b.Id}' in '{p}', and '{p}' cannot grow right or down without moving another box.");
    }

    // Can the container take this larger outline, growing whatever holds it as well if need be?
    bool Grow(string id, Rect bigger, Dictionary<string, Rect> grown)
    {
        string? parent = box[id].Parent;
        var siblings = (parent is null ? top : kids[parent]).Where(s => s != id && at.ContainsKey(s));
        if (siblings.Any(s => Hit(bigger, at[s], Gap))) return false;
        if (parent is not null)
        {
            var room = Interior(parent);
            if (bigger.Right > room.Right || bigger.Bottom > room.Bottom)
                if (!Grow(parent, Stretch(at[parent], bigger.Right + Pad, bigger.Bottom + Pad), grown)) return false;
        }
        grown[id] = bigger;
        return true;
    }

    Rect Interior(string container)
    {
        var r = at[container];
        int title = kids[container].Count > 0 ? box[container].Height : 0;
        return new Rect(r.X + Pad, r.Y + title + Pad, r.Width - 2 * Pad, r.Height - title - 2 * Pad);
    }

    // Nothing may be moved to make room, so anything left overlapping means the sheet needs a fresh layout.
    void Check()
    {
        bool Holds(string outer, string? inner)
        {
            for (int hops = 0; inner is not null && hops < 4; inner = box[inner].Parent, hops++)
                if (inner == outer) return true;
            return false;
        }
        var ids = graph.Boxes.Select(b => b.Id).ToList();
        for (int i = 0; i < ids.Count; i++)
            for (int j = i + 1; j < ids.Count; j++)
            {
                var (a, b) = (ids[i], ids[j]);
                bool bad =
                    Holds(a, box[b].Parent) ? !Within(at[b], at[a]) :
                    Holds(b, box[a].Parent) ? !Within(at[a], at[b]) :
                    Hit(at[a], at[b], 0);
                if (bad) throw new ResurveyNeededException(
                    $"'{a}' and '{b}' would overlap, or one would stick out of the other, unless a box that must stay put were moved.");
            }
    }

    static Rect Stretch(Rect r, int right, int bottom) =>
        r with { Width = Math.Max(r.Right, right) - r.X, Height = Math.Max(r.Bottom, bottom) - r.Y };

    static bool Hit(Rect a, Rect b, int gap) =>
        a.X - gap < b.Right && a.Right + gap > b.X && a.Y - gap < b.Bottom && a.Bottom + gap > b.Y;

    static bool Within(Rect inner, Rect outer) =>
        inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    static long Sq(int v) => (long)v * v;
}
