# How lines are routed

Boxes never move while lines are routed. The sheet is treated as a grid of points one step apart, and each link is a shortest path over it (`src/Groma/Router.cs`). The method is a cut-down version of Wybrow, Marriott and Stuckey, "Orthogonal Connector Routing" (2009): a uniform grid stands in for their visibility graph, and lines are kept apart by charging for shared grid edges, not by a separate nudging pass.

## Rules

- A line may not pass through a box that holds nothing, nor run along its side.
- A line may cross a container outline but not run along it, and not run along the edge of the sheet.
- A line attaches to any point on a box side except the corners. It leaves outwards and arrives inwards, except between a container and something inside it.
- A link to a sheet edge ends anywhere on that edge.
- If an end cannot be reached at all, the line is drawn through whatever is in the way and shows up in "lines through boxes".

## Bundles

Lines that share an end run together. For the line being routed, a grid edge used only by lines with the same from box, or only by lines with the same to box, counts as free: no charge for running on it, alongside it or meeting it. The lines out of a busy box therefore draw one trunk that branches near their far ends, instead of a parallel track each. The router keeps, for every grid edge, the sum and the sum of squares of the end keys of the lines on it; all the lines share an end exactly when those agree with one key. Lines placed by `Plan.RouteExtra` over an existing layout never bundle with it. "On top" in the measures does not count lines that share an end. A label whose branch is shorter than the label can end up on the trunk, and then counts in "labels over lines".

## Costs

A path's cost is the sum of these, in tenths of a step. They are constants in `Router.cs`, not options.

| Cost | Value | Why |
|---|---|---|
| One step of line | 10 | |
| A bend | 40 | |
| Crossing another line | 400 | The headline measure |
| Meeting another line's corner or end | 300 | Looks like a junction that is not there |
| One step on top of another line | 1000 | Two links must stay distinguishable |
| Stepping over the outline of a container the link has no end in | 400 | Paid going in and coming out |
| One step alongside another line with no clear track between | 1 | Leaves room for labels |
| One step along a container's title row | 30 | |
| Distance of the attachment point from the middle of its side | 1 per step, at most 9 | Breaks ties, spreads attachment points |

Links are routed shortest first, ties by id. Then each link is taken off and routed again with all the others in place, for at most two passes.

## How the values were chosen

By running `groma bench` and comparing totals over three scans of each sheet. Crossings and container cuts for the candidates tried, with the layered first layout in place:

| Crossing / container cost | Real sheet crossings | Small sheets crossings | Small sheets container cuts | Large sheet crossings |
|---|---|---|---|---|
| 200 / 200 | 6, 10, 16 | 71 | 8 | 2027 |
| 400 / 200 | 4, 9, 12 | 29 | 36 | 1886 |
| 400 / 400 (chosen) | 4, 9, 12 | 47 | 5 | 1906 |
| 600 / 600 | 4, 10, 15 | 50 | 3 | 1841 |

- A bend cost of 20 or 70 instead of 40 changed crossings by less than 10%.
- One re-routing pass instead of two gave 66 crossings on the small sheets instead of 47; four passes gave 47 and took 50% longer.
- The alongside cost of 1 cut label clashes on the small sheets from 10 to 2 for 6% more bends; a cost of 4 added 16% more bends for no further gain.

Routing one link at a time depends on the order, so a change of weights moves these figures by a few crossings either way. Treat differences of that size as noise.

## Known limits

- Time and memory grow with the area of the sheet, because the grid is uniform.
- Lines are not centred in the space between boxes.
- Attachment points are spread only by the tie-break above, not evenly.
