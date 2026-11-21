namespace Groma;

// Every number is an integer count of grid steps. Origin is the sheet's top-left; x runs right, y runs down.

public enum Side { Top, Bottom, Left, Right }

/// <summary>A box to lay out. A box named as another's Parent is a container: its size is then its title strip.</summary>
public sealed record Box(string Id, int Width, int Height, string? Parent = null);

/// <summary>A directed link. Each end is a box id or a sheet edge, never both.</summary>
public sealed record Link(
    string Id, string? From, string? To,
    Side? FromEdge = null, Side? ToEdge = null,
    int LabelWidth = 0, int LabelHeight = 0);

public sealed record Graph(IReadOnlyList<Box> Boxes, IReadOnlyList<Link> Links);

public readonly record struct Point(int X, int Y);

public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

public sealed record PlacedBox(string Id, int X, int Y, int Width, int Height, string? Parent = null)
{
    public Rect Rect => new(X, Y, Width, Height);
}

/// <summary>A routed link: the corners of its line, source end first, and the rectangle reserved for its label.</summary>
public sealed record Route(
    string Id, string? From, string? To, IReadOnlyList<Point> Points,
    Side? FromEdge = null, Side? ToEdge = null, Rect? Label = null);

public sealed record Layout(int Width, int Height, IReadOnlyList<PlacedBox> Boxes, IReadOnlyList<Route> Links);

public sealed class GromaInputException(string message) : Exception(message);

/// <summary>Quality measures of one layout. Definitions are in docs/metrics.md.</summary>
public sealed record Report
{
    public int Boxes { get; init; }
    public int Links { get; init; }
    public int Survivors { get; init; }
    public int Moved { get; init; }
    public int Crossings { get; init; }
    public int ThroughBoxes { get; init; }
    public int OverlappingBoxes { get; init; }
    public int OnTop { get; init; }
    public int ContainerCuts { get; init; }
    public int Bends { get; init; }
    public int MaxBends { get; init; }
    public int Length { get; init; }
    public int LabelClashes { get; init; }
    public int LabelsOverLines { get; init; }
    public int Diagonals { get; init; }
    public int DetachedEnds { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public long Area => (long)Width * Height;
}
