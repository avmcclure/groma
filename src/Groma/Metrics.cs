namespace Groma;

/// <summary>Counts the quality measures of a layout. Every definition is spelled out in docs/metrics.md.</summary>
public static class Metrics
{
    // One straight piece of a link's line, normalised so that A <= B along its direction.
    readonly record struct Seg(int Link, bool Horizontal, int Track, int A, int B);

    public static Report Measure(Layout layout, Layout? previous = null)
    {
        var byId = layout.Boxes.ToDictionary(b => b.Id, StringComparer.Ordinal);
        var containers = layout.Boxes.Where(b => b.Parent is not null).Select(b => b.Parent!).ToHashSet(StringComparer.Ordinal);
        var leaves = layout.Boxes.Where(b => !containers.Contains(b.Id)).ToList();

        bool IsWithin(string? id, string ancestor)
        {
            for (int hops = 0; id is not null && hops <= layout.Boxes.Count; hops++)
            {
                if (id == ancestor) return true;
                id = byId.TryGetValue(id, out var b) ? b.Parent : null;
            }
            return false;
        }

        var segs = new List<Seg>();
        int diagonals = 0, bends = 0, maxBends = 0, length = 0, detached = 0;
        for (int i = 0; i < layout.Links.Count; i++)
        {
            var link = layout.Links[i];
            var pts = link.Points;
            int linkBends = 0;
            bool? last = null;
            for (int k = 1; k < pts.Count; k++)
            {
                Point a = pts[k - 1], b = pts[k];
                if ((a.X == b.X) == (a.Y == b.Y)) { diagonals++; last = null; continue; } // slanted, or a repeated point
                bool h = a.Y == b.Y;
                segs.Add(h ? new Seg(i, true, a.Y, Math.Min(a.X, b.X), Math.Max(a.X, b.X))
                           : new Seg(i, false, a.X, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y)));
                length += Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
                if (last is { } was && was != h) linkBends++;
                last = h;
            }
            bends += linkBends;
            maxBends = Math.Max(maxBends, linkBends);
            if (link.From is null && link.FromEdge is null && link.To is null && link.ToEdge is null) continue;
            if (pts.Count < 2) { detached += 2; continue; }
            if (!Attached(pts[0], link.From, link.FromEdge)) detached++;
            if (!Attached(pts[^1], link.To, link.ToEdge)) detached++;
        }

        bool Attached(Point p, string? box, Side? edge) => edge switch
        {
            Side.Left => p.X == 0,
            Side.Right => p.X == layout.Width,
            Side.Top => p.Y == 0,
            Side.Bottom => p.Y == layout.Height,
            _ => box is not null && byId.TryGetValue(box, out var b) && OnBorder(p, b.Rect),
        };

        int crossings = 0, onTop = 0;
        for (int a = 0; a < segs.Count; a++)
            for (int b = a + 1; b < segs.Count; b++)
            {
                Seg p = segs[a], q = segs[b];
                if (p.Link == q.Link) continue;
                if (p.Horizontal != q.Horizontal)
                {
                    if (q.Track > p.A && q.Track < p.B && p.Track > q.A && p.Track < q.B) crossings++;
                }
                else if (p.Track == q.Track && Math.Min(p.B, q.B) > Math.Max(p.A, q.A) && !Bundled(layout.Links[p.Link], layout.Links[q.Link])) onTop++;
            }

        int through = 0, cuts = 0, labelClashes = 0, labelsOverLines = 0;
        foreach (var group in segs.GroupBy(s => s.Link))
        {
            var link = layout.Links[group.Key];
            var mine = group.ToList();
            through += leaves.Count(box => box.Id != link.From && box.Id != link.To && mine.Any(s => Runs(s, box.Rect, closed: true)));
            cuts += containers.Count(c => byId.ContainsKey(c) && !IsWithin(link.From, c) && !IsWithin(link.To, c)
                                          && mine.Any(s => Runs(s, byId[c].Rect, closed: false)));
        }
        for (int i = 0; i < layout.Links.Count; i++)
        {
            if (layout.Links[i].Label is not { } label) continue;
            if (leaves.Any(b => Overlap(label, b.Rect)) ||
                layout.Links.Where((other, j) => j != i && other.Label is { } o && Overlap(label, o)).Any()) labelClashes++;
            if (segs.Any(s => s.Link != i && Runs(s, label, closed: false))) labelsOverLines++;
        }

        int overlapping = 0;
        for (int a = 0; a < layout.Boxes.Count; a++)
            for (int b = a + 1; b < layout.Boxes.Count; b++)
            {
                PlacedBox p = layout.Boxes[a], q = layout.Boxes[b];
                if (IsWithin(q.Parent, p.Id)) { if (!Inside(q.Rect, p.Rect)) overlapping++; }
                else if (IsWithin(p.Parent, q.Id)) { if (!Inside(p.Rect, q.Rect)) overlapping++; }
                else if (Overlap(p.Rect, q.Rect)) overlapping++;
            }

        int survivors = 0, moved = 0;
        if (previous is not null)
            foreach (var was in previous.Boxes)
                if (byId.TryGetValue(was.Id, out var now))
                {
                    survivors++;
                    if (now.X != was.X || now.Y != was.Y) moved++;
                }

        return new Report
        {
            Boxes = layout.Boxes.Count, Links = layout.Links.Count,
            Survivors = survivors, Moved = moved,
            Crossings = crossings, ThroughBoxes = through, OverlappingBoxes = overlapping, OnTop = onTop,
            ContainerCuts = cuts, Bends = bends, MaxBends = maxBends, Length = length,
            LabelClashes = labelClashes, LabelsOverLines = labelsOverLines,
            Diagonals = diagonals, DetachedEnds = detached,
            Width = layout.Width, Height = layout.Height,
        };
    }

    // Lines that share an end may run together as one trunk; that is a bundle, not one line hiding another.
    static bool Bundled(Route a, Route b) => (a.From is not null && a.From == b.From) || (a.To is not null && a.To == b.To);

    public static string ToJson(Report r) => "{\n" + string.Join(",\n", new (string Name, long Value)[]
    {
        ("boxes", r.Boxes), ("links", r.Links), ("survivors", r.Survivors), ("moved", r.Moved),
        ("crossings", r.Crossings), ("throughBoxes", r.ThroughBoxes), ("overlappingBoxes", r.OverlappingBoxes),
        ("onTop", r.OnTop), ("containerCuts", r.ContainerCuts), ("bends", r.Bends), ("maxBends", r.MaxBends),
        ("length", r.Length), ("labelClashes", r.LabelClashes), ("labelsOverLines", r.LabelsOverLines),
        ("diagonals", r.Diagonals), ("detachedEnds", r.DetachedEnds),
        ("width", r.Width), ("height", r.Height), ("area", r.Area),
    }.Select(f => $"  \"{f.Name}\": {f.Value}")) + "\n}\n";

    // Does the segment run through the rectangle for a positive length?
    // closed: running along a side counts too. Open: only the inside counts.
    static bool Runs(Seg s, Rect r, bool closed)
    {
        var (lo, hi, from, to) = s.Horizontal ? (r.Y, r.Bottom, r.X, r.Right) : (r.X, r.Right, r.Y, r.Bottom);
        bool onTrack = closed ? s.Track >= lo && s.Track <= hi : s.Track > lo && s.Track < hi;
        return onTrack && Math.Min(s.B, to) > Math.Max(s.A, from);
    }

    static bool Overlap(Rect a, Rect b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    static bool Inside(Rect inner, Rect outer) =>
        inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    static bool OnBorder(Point p, Rect r) =>
        p.X >= r.X && p.X <= r.Right && p.Y >= r.Y && p.Y <= r.Bottom &&
        (p.X == r.X || p.X == r.Right || p.Y == r.Y || p.Y == r.Bottom);
}
