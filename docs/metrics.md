# What the benchmark measures

All coordinates are whole grid steps, so every test below is exact: there are no tolerances.
A "leaf" is a box that holds no other box; a "container" is one that does.

| Measure | Definition | Must be |
|---|---|---|
| Moved | Boxes present in both the previous and the new layout whose top-left corner changed. A container that grew from the same corner has not moved. | 0 on an incremental layout |
| Crossings | Pairs of one horizontal and one vertical segment, belonging to different links, that intersect strictly inside both. A line ending on another line is not a crossing. | lower is better |
| Through boxes | Pairs of (link, leaf box), where the box is not one of the link's ends and some segment of the link runs through the box or along one of its sides for a positive length. | 0 |
| Overlapping boxes | Pairs of boxes that share area, where neither contains the other; plus boxes that are not wholly inside a container they belong to. Touching sides is not overlap. | 0 |
| On top | Pairs of parallel segments of different links on the same line that share a positive length. | lower is better |
| Container cuts | Pairs of (link, container), where neither end of the link is the container or inside it, and some segment runs through the container's inside. Running along its border is not a cut. | lower is better |
| Bends | Changes of direction, summed over all links; also the average per link and the most on any one link. | lower is better |
| Length | Total length of all segments, in steps. | lower is better |
| Label clashes | Labels that share area with a leaf box or with another label. | lower is better |
| Labels over lines | Labels with a segment of some other link running through their inside. A label on its own line is expected. | lower is better |
| Sheet, area, aspect | The layout's width and height, their product, and the shorter side divided by the longer. | |
| Run time | Median wall-clock time of repeated runs after a warm-up, with the machine stated. | |

Two validity counts are also kept and must be 0: segments that are slanted or have no length ("diagonals"), and link ends that do not sit on the side of their box or on their named sheet edge ("detached ends").

## How these relate to the prototypes' numbers

- Crossings, on top and moved use the prototypes' definitions. Their half-pixel tolerances and "more than 4 px" threshold disappear on an integer grid.
- Moved counted leaf boxes only in the prototypes; here it counts containers too.
- Through boxes is stricter here: a line running along a box's side counts.
- Overlapping boxes is stricter here: the prototypes compared leaf boxes only.
- Label clashes matches the MSAGL trial's definition. Labels over lines, container cuts, bends, length, area and aspect were not measured by either prototype.
