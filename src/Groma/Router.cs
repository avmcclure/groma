namespace Groma;

/// <summary>
/// Routes right-angled lines over boxes that do not move: a shortest-path search over the grid points of the
/// sheet, one link at a time, where bends, crossings, shared runs and foreign container outlines all cost extra.
/// After Wybrow, Marriott and Stuckey, "Orthogonal Connector Routing" (2009), cut down: a uniform grid stands in
/// for their visibility graph, and lines are kept apart by charging for shared grid edges rather than by nudging.
/// </summary>
// ponytail: uniform one-step grid, memory and time grow with sheet area; switch to a grid of interesting
// coordinates only (box sides and gap midlines) if sheets get much beyond a few hundred steps a side.
sealed class Router
{
    // Costs, in tenths of a grid step. Tuned on the benchmark; see docs/routing.md before changing.
    const int Step = 10;        // one grid step of line
    const int Bend = 40;        // a corner
    const int Cross = 400;      // crossing another line
    const int Touch = 300;      // meeting another line's corner or end at a point
    const int Share = 1000;     // one step run on top of another line
    const int Border = 400;     // stepping over the outline of a container the link has no end in
    const int Beside = 1;       // one step run right alongside another line, with no clear track between
    const int Title = 30;       // one step along the row where a container's title is written
    const int Through = 100000; // one step inside a box: only when there is no other way at all

    const int Right = 0, Down = 1, Left = 2, Up = 3;

    readonly int w, h, w1;
    readonly int[] delta;
    readonly bool[] wall, blocked, noRight, noDown, title;
    readonly short[] inner;   // the innermost container strictly around each grid point, or -1
    readonly int[][] chain;   // container -> itself and the containers around it
    readonly byte[] useRight, useDown;
    readonly Dictionary<string, PlacedBox> byId;
    readonly Dictionary<string, int> containerIndex = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<int>> drawn = new(StringComparer.Ordinal);

    readonly int[] cost, from, seen, goalDir, goalSeen, goalBias;
    readonly bool[] own;
    readonly PriorityQueue<int, int> open = new();
    int stamp;

    /// <param name="titles">The height of each container's title strip, where known.</param>
    public Router(int width, int height, IReadOnlyList<PlacedBox> boxes, IReadOnlyDictionary<string, int>? titles = null)
    {
        (w, h, w1) = (width, height, width + 1);
        int n = w1 * (h + 1);
        delta = [1, w1, -1, -w1];
        (wall, blocked, noRight, noDown, title) = (new bool[n], new bool[n], new bool[n], new bool[n], new bool[n]);
        (useRight, useDown, inner) = (new byte[n], new byte[n], new short[n]);
        (cost, from, seen) = (new int[n * 4], new int[n * 4], new int[n * 4]);
        (goalDir, goalSeen, goalBias) = (new int[n], new int[n], new int[n]);
        Array.Fill(inner, (short)-1);
        byId = boxes.ToDictionary(b => b.Id, StringComparer.Ordinal);

        int Depth(PlacedBox b) => b.Parent is not null && byId.TryGetValue(b.Parent, out var p) ? 1 + Depth(p) : 0;
        var holders = boxes.Where(b => b.Parent is not null).Select(b => b.Parent!).ToHashSet(StringComparer.Ordinal);
        var containers = boxes.Where(b => holders.Contains(b.Id)).OrderBy(Depth).ThenBy(b => b.Id, StringComparer.Ordinal).ToList();
        for (int i = 0; i < containers.Count; i++) containerIndex[containers[i].Id] = i;
        chain = new int[containers.Count][];
        own = new bool[containers.Count];
        for (int i = 0; i < containers.Count; i++)
        {
            var c = containers[i];
            chain[i] = [.. Around(c.Id)];
            for (int y = Math.Max(c.Y + 1, 0); y < Math.Min(c.Y + c.Height, h + 1); y++)
                for (int x = Math.Max(c.X + 1, 0); x < Math.Min(c.X + c.Width, w + 1); x++)
                    inner[y * w1 + x] = (short)i; // deeper containers come later and overwrite
            for (int y = c.Y + 1; titles is not null && y < c.Y + titles.GetValueOrDefault(c.Id); y++)
                for (int x = c.X + 1; x < c.X + c.Width; x++)
                    if (InSheet(x, y)) title[y * w1 + x] = true;
            foreach (int y in new[] { c.Y, c.Y + c.Height })
                for (int x = c.X; x < c.X + c.Width; x++)
                    if (InSheet(x, y)) noRight[y * w1 + x] = true;
            foreach (int x in new[] { c.X, c.X + c.Width })
                for (int y = c.Y; y < c.Y + c.Height; y++)
                    if (InSheet(x, y)) noDown[y * w1 + x] = true;
        }
        foreach (var b in boxes.Where(b => !holders.Contains(b.Id)))
            for (int y = Math.Max(b.Y, 0); y <= Math.Min(b.Y + b.Height, h); y++)
                for (int x = Math.Max(b.X, 0); x <= Math.Min(b.X + b.Width, w); x++)
                    blocked[y * w1 + x] = true;
        for (int x = 0; x <= w; x++) wall[x] = wall[h * w1 + x] = true;
        for (int y = 0; y <= h; y++) wall[y * w1] = wall[y * w1 + w] = true;
    }

    bool InSheet(int x, int y) => x >= 0 && x <= w && y >= 0 && y <= h;

    // The container indexes of a box and everything around it, innermost first.
    IEnumerable<int> Around(string? id)
    {
        for (int hops = 0; id is not null && hops < 8 && byId.TryGetValue(id, out var b); id = b.Parent, hops++)
            if (containerIndex.TryGetValue(id, out int i)) yield return i;
    }

    bool Within(string? id, string container) =>
        id is not null && containerIndex.TryGetValue(container, out int c) && Around(id).Contains(c);

    /// <summary>Marks lines that are already drawn, so that new ones keep clear of them.</summary>
    public void Occupy(IReadOnlyList<Point> points)
    {
        var path = new List<int>();
        for (int i = 1; i < points.Count; i++)
        {
            Point a = points[i - 1], b = points[i];
            if ((a.X == b.X) == (a.Y == b.Y)) continue;
            int dx = Math.Sign(b.X - a.X), dy = Math.Sign(b.Y - a.Y);
            for (int x = a.X, y = a.Y; x != b.X || y != b.Y; x += dx, y += dy)
                if (InSheet(x, y) && InSheet(x + dx, y + dy)) Use(y * w1 + x, (y + dy) * w1 + x + dx, 1);
        }
    }

    void Use(int a, int b, int by)
    {
        ref byte u = ref b == a + 1 ? ref useRight[a] : ref b == a - 1 ? ref useRight[b] : ref b == a + w1 ? ref useDown[a] : ref useDown[b];
        u = (byte)Math.Clamp(u + by, 0, 255);
    }

    void Use(List<int> path, int by)
    {
        for (int i = 1; i < path.Count; i++) Use(path[i - 1], path[i], by);
    }

    /// <summary>Routes the links, shortest first, then lets each one try again now that it can see all the others.</summary>
    public Dictionary<string, List<Point>> RouteAll(IReadOnlyList<Link> links, int passes = 2)
    {
        var order = links.OrderBy(Span).ThenBy(l => l.Id, StringComparer.Ordinal).ToList();
        foreach (var l in order) Draw(l);
        for (int pass = 0, changed = 1; pass < passes && changed > 0; pass++)
        {
            changed = 0;
            foreach (var l in order)
            {
                var before = drawn[l.Id];
                Use(before, -1);
                Draw(l);
                if (!before.SequenceEqual(drawn[l.Id])) changed++;
            }
        }
        return order.ToDictionary(l => l.Id, l => Corners(drawn[l.Id]), StringComparer.Ordinal);
    }

    void Draw(Link l)
    {
        var path = Search(l, force: false) ?? Search(l, force: true) ?? Direct(l);
        Use(path, 1);
        drawn[l.Id] = path;
    }

    int Span(Link l)
    {
        (int x, int y) Centre(string id) => (byId[id].X + byId[id].Width / 2, byId[id].Y + byId[id].Height / 2);
        int ToEdge((int x, int y) p, Side s) => s switch { Side.Left => p.x, Side.Right => w - p.x, Side.Top => p.y, _ => h - p.y };
        if (l.From is null) return ToEdge(Centre(l.To!), l.FromEdge!.Value);
        if (l.To is null) return ToEdge(Centre(l.From), l.ToEdge!.Value);
        var (a, b) = (Centre(l.From), Centre(l.To));
        return Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);
    }

    // The grid points where a line may attach to an end, each with the direction pointing away from it
    // (out of the box, or off the sheet) and how far it is from the middle of its side, which breaks ties
    // between otherwise equal routes. Corners are left out.
    IEnumerable<(int Node, int Normal, int Off)> Ports(string? box, Side? edge)
    {
        if (box is null)
        {
            if (edge is Side.Left or Side.Right)
                for (int y = 1; y < h; y++) yield return (y * w1 + (edge == Side.Left ? 0 : w), edge == Side.Left ? Left : Right, 0);
            else
                for (int x = 1; x < w; x++) yield return ((edge == Side.Top ? 0 : h) * w1 + x, edge == Side.Top ? Up : Down, 0);
            yield break;
        }
        var b = byId[box];
        for (int x = b.X + 1; x < b.X + b.Width; x++)
        {
            int off = Math.Min(Math.Abs(2 * x - 2 * b.X - b.Width) / 2, Step - 1);
            if (InSheet(x, b.Y)) yield return (b.Y * w1 + x, Up, off);
            if (InSheet(x, b.Y + b.Height)) yield return ((b.Y + b.Height) * w1 + x, Down, off);
        }
        for (int y = b.Y + 1; y < b.Y + b.Height; y++)
        {
            int off = Math.Min(Math.Abs(2 * y - 2 * b.Y - b.Height) / 2, Step - 1);
            if (InSheet(b.X, y)) yield return (y * w1 + b.X, Left, off);
            if (InSheet(b.X + b.Width, y)) yield return (y * w1 + b.X + b.Width, Right, off);
        }
    }

    List<int>? Search(Link l, bool force)
    {
        stamp++;
        open.Clear();
        Array.Clear(own);
        foreach (int c in Around(l.From).Concat(Around(l.To))) own[c] = true;

        // A line leaves a box outwards and arrives inwards, except between a container and something inside it.
        bool fromInside = l.From is not null && l.To is not null && Within(l.From, l.To) && l.From != l.To;
        bool toInside = l.From is not null && l.To is not null && Within(l.To, l.From) && l.From != l.To;
        foreach (var (node, normal, off) in Ports(l.To, l.ToEdge))
        {
            goalSeen[node] = stamp;
            goalBias[node] = off;
            goalDir[node] = l.To is null || fromInside ? normal : normal ^ 2;
        }
        Rect goal = l.To is not null ? byId[l.To].Rect : l.ToEdge switch
        {
            Side.Left => new Rect(0, 0, 0, h),
            Side.Right => new Rect(w, 0, 0, h),
            Side.Top => new Rect(0, 0, w, 0),
            _ => new Rect(0, h, w, 0),
        };
        int Ahead(int node)
        {
            int x = node % w1, y = node / w1;
            return Step * (Math.Max(0, Math.Max(goal.X - x, x - goal.Right)) + Math.Max(0, Math.Max(goal.Y - y, y - goal.Bottom)));
        }

        foreach (var (port, normal, off) in Ports(l.From, l.FromEdge))
        {
            int dir = l.From is null || toInside ? normal ^ 2 : normal;
            if (wall[port] && l.From is not null) continue;
            int first = port + delta[dir];
            if (!Enter(port, first, dir, force, out int extra)) continue;
            Reach(first * 4 + dir, Step + extra + off, -1 - port, Ahead(first));
        }

        while (open.TryDequeue(out int state, out _))
        {
            int node = state >> 2, dir = state & 3;
            if (goalSeen[node] == stamp && goalDir[node] == dir) return Trace(state);
            if (wall[node]) continue;
            int g = cost[state];
            for (int turn = 0; turn < 3; turn++)
            {
                int d = turn == 0 ? dir : turn == 1 ? (dir + 1) & 3 : (dir + 3) & 3;
                int next = node + delta[d];
                if (!Enter(node, next, d, force, out int extra)) continue;
                Reach(next * 4 + d, g + Step + (d == dir ? 0 : Bend) + Meeting(node, dir, d) + extra, state, Ahead(next));
            }
        }
        return null;
    }

    void Reach(int state, int g, int cameFrom, int ahead)
    {
        if (seen[state] == stamp && cost[state] <= g) return;
        (seen[state], cost[state], from[state]) = (stamp, g, cameFrom);
        open.Enqueue(state, g + ahead);
    }

    // May a line step from one grid point to the next, and what does that step cost beyond its length?
    bool Enter(int node, int next, int dir, bool force, out int extra)
    {
        extra = 0;
        bool arriving = goalSeen[next] == stamp && goalDir[next] == dir;
        if (wall[next] && !arriving) return false;
        if (dir switch { Right => noRight[node], Left => noRight[next], Down => noDown[node], _ => noDown[next] }) return false;
        if (arriving) extra += goalBias[next];
        if (title[next] && (dir == Right || dir == Left)) extra += Title;
        if (blocked[next] && !arriving)
        {
            if (!force) return false;
            extra += Through;
        }
        extra += Share * (dir switch { Right => useRight[node], Left => useRight[next], Down => useDown[node], _ => useDown[next] });
        int from = dir is Right or Down ? node : next; // the grid edge being walked, named by its first point
        if (dir is Right or Left ? useRight[from - w1] + useRight[from + w1] > 0 : useDown[from - 1] + useDown[from + 1] > 0) extra += Beside;
        int a = inner[node], b = inner[next];
        if (a != b)
        {
            if (a >= 0) foreach (int c in chain[a]) if (!own[c] && (b < 0 || Array.IndexOf(chain[b], c) < 0)) extra += Border;
            if (b >= 0) foreach (int c in chain[b]) if (!own[c] && (a < 0 || Array.IndexOf(chain[a], c) < 0)) extra += Border;
        }
        return true;
    }

    // What a line pays for the other lines already at a grid point it passes through or turns at.
    int Meeting(int node, int dirIn, int dirOut)
    {
        int Used(int side) => side switch { Right => useRight[node], Down => useDown[node], Left => useRight[node - 1], _ => useDown[node - w1] };
        if (dirIn == dirOut)
        {
            int a = Used((dirIn + 1) & 3), b = Used((dirIn + 3) & 3);
            return a > 0 && b > 0 ? Cross : a + b > 0 ? Touch : 0;
        }
        // Turning: the two sides not used are straight on, and opposite the way out.
        return (Used(dirIn) > 0 ? Touch : 0) + (Used(dirOut ^ 2) > 0 ? Touch : 0);
    }

    List<int> Trace(int state)
    {
        var path = new List<int>();
        for (; state >= 0; state = from[state]) path.Add(state >> 2);
        path.Add(-1 - state);
        path.Reverse();
        return path;
    }

    // Last resort when an end cannot be reached at all: an L between the two ends, straight through whatever is there.
    List<int> Direct(Link l)
    {
        int a = Ports(l.From, l.FromEdge).Select(p => p.Node).DefaultIfEmpty(0).First();
        int b = Ports(l.To, l.ToEdge).Select(p => p.Node).DefaultIfEmpty(0).First();
        var path = new List<int> { a };
        for (int x = a % w1, y = a / w1; x != b % w1; path.Add(y * w1 + x)) x += Math.Sign(b % w1 - x);
        for (int x = b % w1, y = a / w1; y != b / w1; path.Add(y * w1 + x)) y += Math.Sign(b / w1 - y);
        return path;
    }

    List<Point> Corners(List<int> path)
    {
        var points = new List<Point>();
        for (int i = 0; i < path.Count; i++)
        {
            if (i > 0 && i < path.Count - 1 && path[i] - path[i - 1] == path[i + 1] - path[i]) continue;
            points.Add(new Point(path[i] % w1, path[i] / w1));
        }
        return points;
    }
}
