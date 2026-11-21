namespace Groma;

/// <summary>
/// Puts each link's label on the link's own line, after the lines are drawn. Every grid position along the
/// line is a candidate; each is scored, the labels with the fewest good positions choose first, and then each
/// label gets a chance to move now that it can see the others. Candidate positions scored and chosen greedily
/// follow Kakoulis and Tollis; the improvement passes follow the discrete descent of Christensen, Marks and
/// Shieber. The weights are ours. A label with no clear position is placed at its least bad one.
/// </summary>
// ponytail: every candidate is checked against every box, line and label; fine up to a few hundred links.
static class Labels
{
    // What a position costs. Each line is worth more than everything below it put together in practice.
    const int OverBoxOrLabel = 100000; // covers a box, or another label
    const int OverOtherLine = 5000;    // another link's line runs through it
    const int OverOwnBend = 400;       // its own line turns underneath it
    const int Overhang = 200;          // longer than the stretch of line it sits on
    const int OverOutline = 100;       // straddles a container outline
    const int Upright = 60;            // sits on a vertical stretch, where text lies across the line
    const int PerStepFromMiddle = 1;   // distance from the middle of the link

    readonly record struct Seg(string Link, bool Horizontal, int Track, int A, int B);
    readonly record struct Candidate(Rect Rect, int Base);

    /// <param name="settled">Links that are already final: their lines and labels are obstacles and do not change.</param>
    public static Dictionary<string, Rect> Place(
        int width, int height, IReadOnlyList<PlacedBox> boxes,
        IReadOnlyList<(Link Link, IReadOnlyList<Point> Points)> wanted, IReadOnlyList<Route> settled)
    {
        var holders = boxes.Where(b => b.Parent is not null).Select(b => b.Parent!).ToHashSet(StringComparer.Ordinal);
        var leaves = boxes.Where(b => !holders.Contains(b.Id)).Select(b => b.Rect).ToList();
        var outlines = boxes.Where(b => holders.Contains(b.Id)).Select(b => b.Rect).ToList();
        var segs = new List<Seg>();
        foreach (var r in settled) segs.AddRange(Segments(r.Id, r.Points));
        foreach (var (l, pts) in wanted) segs.AddRange(Segments(l.Id, pts));
        var placed = new Dictionary<string, Rect>(StringComparer.Ordinal);
        var fixedLabels = settled.Where(r => r.Label is not null).Select(r => r.Label!.Value).ToList();

        var todo = wanted.Where(x => x.Link.LabelWidth > 0 && x.Link.LabelHeight > 0 && x.Points.Count >= 2).ToList();
        var candidates = new Dictionary<string, List<Candidate>>(StringComparer.Ordinal);
        foreach (var (l, pts) in todo)
        {
            var mine = Segments(l.Id, pts).ToList();
            // Only what lies near this link's line can ever be under its label.
            var reach = new Rect(pts.Min(p => p.X) - l.LabelWidth, pts.Min(p => p.Y) - l.LabelHeight,
                pts.Max(p => p.X) - pts.Min(p => p.X) + 2 * l.LabelWidth, pts.Max(p => p.Y) - pts.Min(p => p.Y) + 2 * l.LabelHeight);
            var nearBoxes = leaves.Where(b => Overlap(reach, b)).ToList();
            var nearFixed = fixedLabels.Where(f => Overlap(reach, f)).ToList();
            var nearLines = segs.Where(s => s.Link != l.Id && Runs(s, reach)).ToList();
            var nearOutlines = outlines.Where(o => Overlap(reach, o)).ToList();
            int total = mine.Sum(s => s.B - s.A), walked = 0;
            var list = new List<Candidate>();
            for (int i = 1; i < pts.Count; i++)
            {
                Point a = pts[i - 1], b = pts[i];
                if ((a.X == b.X) == (a.Y == b.Y)) continue;
                bool h = a.Y == b.Y;
                int len = Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y), dx = Math.Sign(b.X - a.X), dy = Math.Sign(b.Y - a.Y);
                for (int t = 0; t <= len; t++)
                {
                    var rect = new Rect(a.X + dx * t - l.LabelWidth / 2, a.Y + dy * t - l.LabelHeight / 2, l.LabelWidth, l.LabelHeight);
                    if (rect.X < 0 || rect.Y < 0 || rect.Right > width || rect.Bottom > height) continue;
                    int cost = PerStepFromMiddle * Math.Abs(2 * (walked + t) - total) / 2;
                    if (!h) cost += Upright;
                    int along = h ? l.LabelWidth : l.LabelHeight;
                    if (t < along / 2 || len - t < (along + 1) / 2) cost += Overhang;
                    cost += OverBoxOrLabel * (nearBoxes.Count(b => Overlap(rect, b)) + nearFixed.Count(f => Overlap(rect, f)));
                    cost += OverOtherLine * nearLines.Count(s => Runs(s, rect));
                    // Its own line passes through it once by design; any more is a bend underneath.
                    cost += OverOwnBend * Math.Max(0, mine.Count(s => Runs(s, rect)) - 1);
                    cost += OverOutline * nearOutlines.Count(o => Straddles(rect, o));
                    list.Add(new Candidate(rect, cost));
                }
                walked += len;
            }
            if (list.Count == 0) // the sheet is smaller than the label: clamp it on at the start of the line
                list.Add(new Candidate(new Rect(Math.Max(0, Math.Min(pts[0].X, width - l.LabelWidth)), Math.Max(0, Math.Min(pts[0].Y, height - l.LabelHeight)), l.LabelWidth, l.LabelHeight), 0));
            candidates[l.Id] = list;
        }

        int Cost(string id, Candidate c) =>
            c.Base + OverBoxOrLabel * placed.Count(p => p.Key != id && Overlap(c.Rect, p.Value));

        var chosen = new Dictionary<string, int>(StringComparer.Ordinal);
        bool Choose(string id)
        {
            var list = candidates[id];
            int best = 0, bestCost = int.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                int c = Cost(id, list[i]);
                if (c < bestCost) (best, bestCost) = (i, c);
            }
            // Move only for a strict improvement, so the passes settle.
            if (chosen.TryGetValue(id, out int now) && Cost(id, list[now]) <= bestCost) return false;
            (chosen[id], placed[id]) = (best, list[best].Rect);
            return true;
        }

        // The labels with the fewest unobstructed positions choose first.
        var order = todo.Select(x => x.Link.Id)
            .OrderBy(id => candidates[id].Count(c => c.Base < OverOtherLine))
            .ThenBy(id => id, StringComparer.Ordinal).ToList();
        foreach (var id in order) Choose(id);
        for (int pass = 0; pass < 4; pass++)
        {
            bool any = false;
            foreach (var id in order) any |= Choose(id);
            if (!any) break;
        }
        return placed;
    }

    static IEnumerable<Seg> Segments(string link, IReadOnlyList<Point> pts)
    {
        for (int i = 1; i < pts.Count; i++)
        {
            Point a = pts[i - 1], b = pts[i];
            if ((a.X == b.X) == (a.Y == b.Y)) continue;
            yield return a.Y == b.Y
                ? new Seg(link, true, a.Y, Math.Min(a.X, b.X), Math.Max(a.X, b.X))
                : new Seg(link, false, a.X, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y));
        }
    }

    // Does the segment pass through the inside of the rectangle?
    static bool Runs(Seg s, Rect r)
    {
        var (lo, hi, from, to) = s.Horizontal ? (r.Y, r.Bottom, r.X, r.Right) : (r.X, r.Right, r.Y, r.Bottom);
        return s.Track > lo && s.Track < hi && Math.Min(s.B, to) > Math.Max(s.A, from);
    }

    static bool Overlap(Rect a, Rect b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    static bool Straddles(Rect label, Rect outline) =>
        Overlap(label, outline) && !(label.X >= outline.X && label.Y >= outline.Y && label.Right <= outline.Right && label.Bottom <= outline.Bottom);
}
