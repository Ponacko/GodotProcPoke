namespace ProcPoke.Generation.Carving;

/// <summary>Which border of an area's tile grid an opening sits on.</summary>
public enum EdgeSide { Left, Right, Top, Bottom }

/// <summary>
/// Where one area exposes its side of a Seamless connection (ADR-0001 §4.2 continuity): an edge, an
/// offset along it, and a width. Decided by <see cref="OpeningAligner"/> before either side is carved, so
/// paired areas agree on where the doorway is.
/// </summary>
public sealed record AreaOpening
{
    public required int NeighborAreaId { get; init; }
    public required EdgeSide Edge { get; init; }
    public required int Offset { get; init; }
    public int Width { get; init; } = 1;
}

public static class AreaOpeningExtensions
{
    /// <summary>This area's offset on <paramref name="edge"/>, or <paramref name="fallback"/> if it has no
    /// Seamless connection there (an unaligned edge — a Warp neighbour, or none at all).</summary>
    public static int OffsetOr(this IReadOnlyList<AreaOpening> openings, EdgeSide edge, int fallback)
        => openings.FirstOrDefault(o => o.Edge == edge)?.Offset ?? fallback;

    /// <summary>The tile this opening sits on, on a grid of the given size.</summary>
    public static (int X, int Y) TileOn(this AreaOpening opening, int width, int height) => opening.Edge switch
    {
        EdgeSide.Left => (0, opening.Offset),
        EdgeSide.Right => (width - 1, opening.Offset),
        EdgeSide.Top => (opening.Offset, 0),
        _ => (opening.Offset, height - 1),
    };
}
