using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Groma;

/// <summary>Reads graphs and layouts, and writes a layout as its one canonical text.</summary>
public static class LayoutFile
{
    public static Graph ReadGraph(string json) => Parse(json, root =>
    {
        var boxes = root.GetProperty("boxes").EnumerateArray().Select(b => new Box(
            Str(b, "id") ?? throw new GromaInputException("A box has no \"id\"."),
            Int(b, "width"), Int(b, "height"), Str(b, "parent"))).ToList();
        var links = root.GetProperty("links").EnumerateArray().Select(l =>
        {
            var id = Str(l, "id") ?? throw new GromaInputException("A link has no \"id\".");
            return new Link(id, Str(l, "from"), Str(l, "to"), Edge(l, "fromEdge", id), Edge(l, "toEdge", id),
                Int(l, "labelWidth"), Int(l, "labelHeight"));
        }).ToList();
        var graph = new Graph(boxes, links);
        GraphCheck.Run(graph);
        return graph;
    });

    public static Layout Read(string json) => Parse(json, root =>
    {
        if (Int(root, "version") != 1) throw new GromaInputException("Only layout files of \"version\": 1 can be read.");
        var boxes = root.GetProperty("boxes").EnumerateArray().Select(b => new PlacedBox(
            Str(b, "id")!, Int(b, "x"), Int(b, "y"), Int(b, "width"), Int(b, "height"), Str(b, "parent"))).ToList();
        var links = root.GetProperty("links").EnumerateArray().Select(l =>
        {
            var id = Str(l, "id")!;
            var points = l.GetProperty("points").EnumerateArray().Select(p => new Point(p[0].GetInt32(), p[1].GetInt32())).ToList();
            Rect? label = l.TryGetProperty("label", out var r) ? new Rect(Int(r, "x"), Int(r, "y"), Int(r, "width"), Int(r, "height")) : null;
            return new Route(id, Str(l, "from"), Str(l, "to"), points, Edge(l, "fromEdge", id), Edge(l, "toEdge", id), label);
        }).ToList();
        return new Layout(Int(root, "width"), Int(root, "height"), boxes, links);
    });

    /// <summary>The canonical bytes: fixed key order, boxes and links sorted by id, "\n" line ends, integers only.</summary>
    public static string Write(Layout layout)
    {
        var sb = new StringBuilder();
        sb.Append("{\n  \"version\": 1,\n");
        sb.Append($"  \"width\": {layout.Width},\n  \"height\": {layout.Height},\n");
        Items(sb, "boxes", layout.Boxes.OrderBy(b => b.Id, StringComparer.Ordinal).Select(b =>
            $"{{ \"id\": {Q(b.Id)}{(b.Parent is null ? "" : $", \"parent\": {Q(b.Parent)}")}, \"x\": {b.X}, \"y\": {b.Y}, \"width\": {b.Width}, \"height\": {b.Height} }}"));
        sb.Append(",\n");
        Items(sb, "links", layout.Links.OrderBy(l => l.Id, StringComparer.Ordinal).Select(l =>
            $"{{ \"id\": {Q(l.Id)}, {End("from", l.From, l.FromEdge)}, {End("to", l.To, l.ToEdge)}, " +
            $"\"points\": [{string.Join(", ", l.Points.Select(p => $"[{p.X}, {p.Y}]"))}]" +
            (l.Label is { } r ? $", \"label\": {{ \"x\": {r.X}, \"y\": {r.Y}, \"width\": {r.Width}, \"height\": {r.Height} }}" : "") + " }"));
        sb.Append("\n}\n");
        return sb.ToString();
    }

    static void Items(StringBuilder sb, string name, IEnumerable<string> lines)
    {
        var all = lines.ToList();
        sb.Append($"  \"{name}\": [");
        sb.Append(all.Count == 0 ? "]" : "\n    " + string.Join(",\n    ", all) + "\n  ]");
    }

    static string End(string key, string? box, Side? edge) =>
        box is not null ? $"\"{key}\": {Q(box)}" : $"\"{key}Edge\": \"{edge.ToString()!.ToLowerInvariant()}\"";

    // Escapes only what JSON requires, so ids stay readable in a diff.
    static string Q(string s) => "\"" + JsonEncodedText.Encode(s, JavaScriptEncoder.UnsafeRelaxedJsonEscaping) + "\"";

    static T Parse<T>(string json, Func<JsonElement, T> read)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return read(doc.RootElement);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or IndexOutOfRangeException)
        {
            throw new GromaInputException("The JSON is not in the expected shape: " + e.Message);
        }
    }

    static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetString() : null;

    static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetInt32() : 0;

    static Side? Edge(JsonElement e, string name, string linkId) => Str(e, name) switch
    {
        null => null,
        "top" => Side.Top,
        "bottom" => Side.Bottom,
        "left" => Side.Left,
        "right" => Side.Right,
        var other => throw new GromaInputException($"Link '{linkId}' names sheet edge '{other}'; use top, bottom, left or right."),
    };
}

/// <summary>Rejects graphs the layout cannot honour, naming the offending box or link. Nothing is repaired.</summary>
static class GraphCheck
{
    public static void Run(Graph g)
    {
        var boxes = new Dictionary<string, Box>(StringComparer.Ordinal);
        foreach (var b in g.Boxes)
        {
            if (!boxes.TryAdd(b.Id, b)) Fail($"Box id '{b.Id}' appears more than once.");
            if (b.Width < 1 || b.Height < 1) Fail($"Box '{b.Id}' needs a width and height of at least 1.");
        }
        foreach (var b in g.Boxes)
        {
            int depth = 0;
            for (var p = b.Parent; p is not null; p = boxes[p].Parent)
            {
                if (!boxes.ContainsKey(p)) Fail($"Box '{b.Id}' names parent '{p}', which is not a box.");
                if (++depth > 2) Fail($"Box '{b.Id}' is nested more than two deep, or is its own ancestor.");
            }
        }
        var links = new HashSet<string>(StringComparer.Ordinal);
        foreach (var l in g.Links)
        {
            if (!links.Add(l.Id)) Fail($"Link id '{l.Id}' appears more than once.");
            if (l.From is null == l.FromEdge is null) Fail($"Link '{l.Id}' needs exactly one of \"from\" and \"fromEdge\".");
            if (l.To is null == l.ToEdge is null) Fail($"Link '{l.Id}' needs exactly one of \"to\" and \"toEdge\".");
            if (l.From is null && l.To is null) Fail($"Link '{l.Id}' runs from one sheet edge to another; one end must be a box.");
            foreach (var end in new[] { l.From, l.To })
                if (end is not null && !boxes.ContainsKey(end)) Fail($"Link '{l.Id}' names box '{end}', which is not in the graph.");
            if (l.LabelWidth < 0 || l.LabelHeight < 0) Fail($"Link '{l.Id}' has a negative label size.");
        }
    }

    static void Fail(string message) => throw new GromaInputException(message);
}
