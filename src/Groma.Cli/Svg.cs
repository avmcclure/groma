using System.Text;
using Groma;

/// <summary>A plain picture of a layout for a person to check by eye. A debugging aid, not a renderer.</summary>
static class Svg
{
    const int Px = 10; // pixels per grid step

    public static string Render(Layout layout, Layout? previous = null)
    {
        static string Esc(string t) => t.Replace("&", "&amp;").Replace("<", "&lt;").Replace("\"", "&quot;");
        static string Tail(string id) => id[(id.LastIndexOf('/') + 1)..];
        var old = previous?.Boxes.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        var holders = layout.Boxes.Where(b => b.Parent is not null).Select(b => b.Parent!).ToHashSet(StringComparer.Ordinal);
        var boxes = layout.Boxes.OrderBy(b => b.Id, StringComparer.Ordinal).ToList();
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{layout.Width * Px}\" height=\"{layout.Height * Px}\" viewBox=\"0 0 {layout.Width * Px} {layout.Height * Px}\" font-family=\"sans-serif\" font-size=\"11\">\n");
        sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#fff\" stroke=\"#bbb\"/>\n");
        foreach (var b in boxes.Where(b => holders.Contains(b.Id)))
            sb.Append($"<rect x=\"{b.X * Px}\" y=\"{b.Y * Px}\" width=\"{b.Width * Px}\" height=\"{b.Height * Px}\" fill=\"#f3f1ea\" fill-opacity=\"0.6\" stroke=\"#8a8370\" stroke-dasharray=\"6 3\"/>" +
                      $"<text x=\"{b.X * Px + 6}\" y=\"{b.Y * Px + 14}\" font-weight=\"bold\">{Esc(Tail(b.Id))}</text>\n");
        foreach (var l in layout.Links.OrderBy(l => l.Id, StringComparer.Ordinal).Where(l => l.Points.Count > 0))
            sb.Append($"<polyline points=\"{string.Join(" ", l.Points.Select(p => $"{p.X * Px},{p.Y * Px}"))}\" fill=\"none\" stroke=\"#35506b\" stroke-width=\"1.2\"/>" +
                      $"<circle cx=\"{l.Points[^1].X * Px}\" cy=\"{l.Points[^1].Y * Px}\" r=\"2.5\" fill=\"#35506b\"/>\n");
        foreach (var b in boxes.Where(b => !holders.Contains(b.Id)))
            sb.Append($"<rect x=\"{b.X * Px}\" y=\"{b.Y * Px}\" width=\"{b.Width * Px}\" height=\"{b.Height * Px}\" fill=\"{(old is not null && !old.Contains(b.Id) ? "#ffe9a8" : "#fff")}\" stroke=\"#222\"/>" +
                      $"<text x=\"{b.X * Px + b.Width * Px / 2}\" y=\"{b.Y * Px + b.Height * Px / 2 + 4}\" text-anchor=\"middle\">{Esc(Tail(b.Id))}</text>\n");
        foreach (var l in layout.Links.OrderBy(l => l.Id, StringComparer.Ordinal))
            if (l.Label is { } r)
                sb.Append($"<rect x=\"{r.X * Px}\" y=\"{r.Y * Px}\" width=\"{r.Width * Px}\" height=\"{r.Height * Px}\" fill=\"#fff\" fill-opacity=\"0.85\" stroke=\"#c33\" stroke-width=\"0.5\"/>\n");
        return sb.Append("</svg>\n").ToString();
    }
}
