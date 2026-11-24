# How boxes are placed

## Fresh layout (`Plan.Survey`)

Containers are laid out innermost first, and each is then treated as one box by the level above (Sander, "Layout of Compound Directed Graphs", 1996). Each group of sibling boxes is arranged by the layered method of Gansner, Koutsofios, North and Vo (1993), each stage in its simplest adequate form (`src/Groma/Layered.cs`):

1. **Loops.** Walk the links depth-first in id order and turn round every link that points back up the walk.
2. **Columns.** Each box goes one column after the latest box that links to it. A box that nothing links to is moved to just before its earliest target.
3. **Order within columns.** Up to 24 sweeps move each box to the average place of its neighbours in the next column, then swap adjacent boxes where that removes a crossing. The best order seen is kept.
4. **Positions.** Each box is pulled level with the middle of its neighbours without changing the order or closing the gaps.

5. **Folding.** On the sheet itself (not inside a container), a drawing taller than it is wide is cut across into bands set side by side, like a long text set in columns, so the sheet comes out nearest to 3:2 landscape. Cuts fall only where no box spans them; linked boxes are already level, so most links stay inside their band.

Boxes with no links at their level are set out in a grid with as many columns as make it nearest to square. Columns are a double gap apart, which leaves room for lines and labels.

A link counts at a level only if both its ends are inside different members of that group. Links that leave a container shape its inside in one way only: among the boxes set out in a grid, those fed from outside take the side links come in from and those that feed out take the far side. Otherwise the inside of a container is laid out without regard to what is outside it; this is the known cost of laying out containers one at a time.

Each container keeps one spare row along its bottom, so that boxes added later fit without the container having to grow.

## Incremental layout (`Plan.Update`)

No published method covers this case; these rules are the library's own (`src/Groma/Placer.cs`).

- Every surviving box keeps its top-left corner. A box that holds nothing takes its size from the new graph; a container keeps its old outline and never shrinks. If a box inside a container grew past the outline, the container grows right or down to hold it where its neighbours allow.
- The sheet never shrinks and grows only right and down.
- A new box on the open sheet goes to the free slot nearest the middle of the boxes it links to. Only slots lined up with a box already there are tried, which keeps new boxes in rows and columns with their neighbours. A new box with no links goes to the right of everything on the top row.
- A new box inside an existing container takes the first free slot in reading order. If there is none, the container grows right, and failing that down, growing the container around it too if needed. If a neighbour blocks both, `Update` throws `ResurveyNeededException`.
- A new container arrives as one block, already laid out inside as in a fresh layout.
- At the end, if any two boxes overlap or a box sticks out of its container (a surviving box grew, or was moved to another container), `Update` throws `ResurveyNeededException`.

After placement every line is routed again and every label placed again.

## Labels

Each label sits on its own link's line (`src/Groma/Labels.cs`). Every grid position along the line is a candidate, scored for covering a box or another label, lying over another link's line, lying over its own bend, overhanging its stretch of line, and distance from the middle of the link. Labels with the fewest clear positions choose first; then each may move for a strict improvement. A label with no clear position is placed at its least bad one and counted in "label clashes".
