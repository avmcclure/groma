using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Groma;

try
{
    switch (args)
    {
        case ["measure", var path]:
            Console.Out.Write(Metrics.ToJson(Metrics.Measure(LayoutFile.Read(File.ReadAllText(path)))));
            return 0;
        case ["measure", var path, "--previous", var previous]:
            Console.Out.Write(Metrics.ToJson(Metrics.Measure(LayoutFile.Read(File.ReadAllText(path)), LayoutFile.Read(File.ReadAllText(previous)))));
            return 0;
        case ["survey", var graph]:
            Console.Out.Write(LayoutFile.Write(Plan.Survey(LayoutFile.ReadGraph(File.ReadAllText(graph)))));
            return 0;
        case ["update", var graph, "--previous", var previous]:
            Console.Out.Write(LayoutFile.Write(Plan.Update(LayoutFile.ReadGraph(File.ReadAllText(graph)), LayoutFile.Read(File.ReadAllText(previous)))));
            return 0;
        case ["route", var layout, "--links", var links]:
            Console.Out.Write(LayoutFile.Write(Plan.RouteExtra(LayoutFile.Read(File.ReadAllText(layout)), LayoutFile.ReadLinks(File.ReadAllText(links)))));
            return 0;
        case ["render", var layout]:
            Console.Out.Write(Svg.Render(LayoutFile.Read(File.ReadAllText(layout))));
            return 0;
        case ["render", var layout, "--previous", var previous]:
            Console.Out.Write(Svg.Render(LayoutFile.Read(File.ReadAllText(layout)), LayoutFile.Read(File.ReadAllText(previous))));
            return 0;
        case ["bench", var dir]:
            Bench(dir);
            return 0;
        default:
            Console.Error.WriteLine(
                """
                usage: groma survey  <graph.json>                           fresh layout
                       groma update  <graph.json> --previous <layout.json>  keep surviving boxes where they are
                       groma route   <layout.json> --links <links.json>     draw extra links, move nothing
                       groma measure <layout.json> [--previous <old.json>]  quality measures
                       groma render  <layout.json> [--previous <old.json>]  a picture for checking by eye
                       groma bench   <fixtures-dir>                         every measure for every fixture
                Layouts and measures are written to standard output. Exit 2: bad input. Exit 3: re-survey needed.
                """);
            return 2;
    }
}
catch (ResurveyNeededException e)
{
    Console.Error.WriteLine("groma: re-survey needed. " + e.Message);
    return 3;
}
catch (Exception e) when (e is GromaInputException or IOException)
{
    Console.Error.WriteLine("groma: " + e.Message);
    return 2;
}

// What a consumer does on each scan: update, and fall back to a fresh layout only when told it must.
static Layout? Lay(Graph graph, Layout? previous)
{
    if (previous is null) return Plan.Survey(graph);
    try { return Plan.Update(graph, previous); }
    catch (ResurveyNeededException) { return Plan.Survey(graph); }
}

// Every fixture, every scan: lay it out, measure it, time it, and print it beside the prototypes' figures.
static void Bench(string dir)
{
    const int Runs = 5;
    Console.WriteLine($"Run time is the median of {Runs} runs after one warm-up. {Environment.OSVersion}, {Environment.ProcessorCount} cores, .NET {Environment.Version}.");
    Console.WriteLine("Figures in brackets are the earlier prototypes' (see each fixture's baselines.json).");
    Console.WriteLine();
    Console.WriteLine("| Sheet | Scan | Boxes | Links | Moved | Crossings | Through boxes | Overlapping boxes | On top | Container cuts | Bends (avg, max) | Length | Label clashes | Labels over lines | Sheet | Area | Aspect | ms |");
    Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
    foreach (var sheet in Directory.GetDirectories(dir).Order(StringComparer.Ordinal))
    {
        var baselinePath = Path.Combine(sheet, "baselines.json");
        using var baselines = File.Exists(baselinePath) ? JsonDocument.Parse(File.ReadAllText(baselinePath)) : null;
        Layout? previous = null;
        for (int step = 1; File.Exists(Path.Combine(sheet, $"{step}.graph.json")); step++)
        {
            var graph = LayoutFile.ReadGraph(File.ReadAllText(Path.Combine(sheet, $"{step}.graph.json")));
            var layout = Lay(graph, previous);
            string With(string ours, string key) =>
                baselines is not null && baselines.RootElement.TryGetProperty(step.ToString(), out var b) && b.TryGetProperty(key, out var v)
                    ? $"{ours} ({v.GetString()})" : ours;

            var cells = new List<string> { Path.GetFileName(sheet), step.ToString(), graph.Boxes.Count.ToString(), graph.Links.Count.ToString() };
            if (layout is null)
                cells.AddRange(new[] { "moved", "crossings", "through", "overlapping", "onTop", "", "", "", "labelClashes", "", "sheet", "", "", "ms" }
                    .Select(key => key == "" ? "—" : With("—", key)));
            else
            {
                var times = new List<double>();
                for (int run = 0; run <= Runs; run++)
                {
                    var clock = Stopwatch.StartNew();
                    Lay(graph, previous);
                    if (run > 0) times.Add(clock.Elapsed.TotalMilliseconds);
                }
                times.Sort();
                var r = Metrics.Measure(layout, previous);
                string F(double v, string format) => v.ToString(format, CultureInfo.InvariantCulture);
                cells.AddRange(
                [
                    With(previous is null ? "" : $"{r.Moved} of {r.Survivors}", "moved"),
                    With(r.Crossings.ToString(), "crossings"), With(r.ThroughBoxes.ToString(), "through"),
                    With(r.OverlappingBoxes.ToString(), "overlapping"), With(r.OnTop.ToString(), "onTop"),
                    r.ContainerCuts.ToString(),
                    $"{r.Bends} ({F(r.Links == 0 ? 0 : (double)r.Bends / r.Links, "0.0")}, {r.MaxBends})",
                    r.Length.ToString(), With(r.LabelClashes.ToString(), "labelClashes"), r.LabelsOverLines.ToString(),
                    With($"{r.Width}x{r.Height}", "sheet"), r.Area.ToString(),
                    F(Math.Max(r.Width, r.Height) == 0 ? 1 : (double)Math.Min(r.Width, r.Height) / Math.Max(r.Width, r.Height), "0.00"),
                    With(F(times[times.Count / 2], "0.0"), "ms"),
                ]);
                if (r.Moved > 0) cells[4] += " RE-SURVEYED";
                if (r.Diagonals + r.DetachedEnds > 0) cells[0] += $" (INVALID: {r.Diagonals} bad segments, {r.DetachedEnds} detached ends)";
            }
            Console.WriteLine("| " + string.Join(" | ", cells) + " |");
            previous = layout;
        }
    }
}
