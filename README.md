# Groma

Lays out survey plans: ruled boxes, some holding other boxes, joined by right-angled labelled lines on a grid.
Its first rule is that a box, once placed, stays where it is.

It is a layout library only: no rendering, no UI, no general graph drawing.

## Use

```csharp
using Groma;

Graph graph = LayoutFile.ReadGraph(File.ReadAllText("graph.json"));
Layout first = Plan.Survey(graph);                    // fresh layout
Layout next  = Plan.Update(changedGraph, first);      // surviving boxes keep their positions
Layout more  = Plan.RouteExtra(next, extraLinks);     // draw more links, move nothing
File.WriteAllText("layout.json", LayoutFile.Write(next));
```

`Plan.Update` throws `ResurveyNeededException` when it cannot place something without moving a box; call `Plan.Survey` then.

The same from the command line:

```
dotnet run --project src/Groma.Cli -c Release -- survey  graph.json
dotnet run --project src/Groma.Cli -c Release -- update  graph.json --previous layout.json
dotnet run --project src/Groma.Cli -c Release -- route   layout.json --links links.json
dotnet run --project src/Groma.Cli -c Release -- measure layout.json --previous old.json
dotnet run --project src/Groma.Cli -c Release -- render  layout.json
dotnet run --project src/Groma.Cli -c Release -- bench   fixtures
```

Every number is a whole count of grid steps; the consumer decides how many pixels a step is. A link end is a box (`from`, `to`) or a sheet edge (`fromEdge`, `toEdge`: `top`, `bottom`, `left`, `right`). A container's `width` and `height` are its title strip. See `fixtures/*/1.graph.json` for input and `fixtures/*/expected/*.layout.json` for output.

The layout file is deterministic: the same graph gives the same bytes, whatever order its boxes and links arrive in.

## Changing the layout code

```
dotnet test
```

- Every scan of every fixture is compared byte for byte with `fixtures/*/expected`: the layout, its measures and an SVG picture.
- If a change is meant to alter the output, run `GROMA_ACCEPT=1 dotnet test` once and review the diff of `fixtures/*/expected`, pictures included.
- Randomised sheets check the rules that must always hold: no surviving box moves, no boxes overlap, no line passes through a box.
- `groma bench fixtures` prints every measure beside the earlier prototypes' figures; `docs/benchmarks/` keeps the run from each build step.

## More

- `docs/metrics.md`: what each measure means.
- `docs/placement.md`: how boxes are placed, fresh and incremental.
- `docs/routing.md`: how lines are routed, and how the weights were chosen.
