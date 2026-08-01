using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Overworld;

public enum TransitionResultKind
{
    None,
    Seamless,
    Warp,
    Failed,
}

public sealed record AreaArrival(
    int AreaId,
    GridPosition Position,
    Facing Facing,
    WorldOrigin WorldOrigin);

public sealed record TransitionResult(
    TransitionResultKind Kind,
    int FromAreaId,
    int ToAreaId,
    AreaArrival? Arrival = null,
    string? Failure = null)
{
    public bool Succeeded => Kind is TransitionResultKind.Seamless or TransitionResultKind.Warp;
}

/// <summary>
/// Resolves generated edge crossings. It consumes only immutable RegionGenerator output: the OpeningPlan
/// identifies paired borders, WorldCanvas identifies the area placement, and the carved grids validate arrival.
/// </summary>
public static class TransitionResolver
{
    public static TransitionResult Resolve(
        GeneratedRegion region, int fromAreaId, GridPosition position, Facing facing)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (!region.Carved.TryGetValue(fromAreaId, out var source))
            return Failure(fromAreaId, -1, position, $"source area {fromAreaId} has no carved grid");

        var opening = region.Openings.EdgesOf(fromAreaId)
            .FirstOrDefault(candidate => candidate.TileOn(source.Grid.Width, source.Grid.Height)
                == (position.X, position.Y) && OutwardFacing(candidate.Edge) == facing);
        if (opening is null) return new TransitionResult(TransitionResultKind.None, fromAreaId, -1);

        var connection = region.Graph.Connections.FirstOrDefault(candidate =>
            candidate.Touches(fromAreaId) && candidate.Other(fromAreaId) == opening.NeighborAreaId);
        if (connection is null)
            return Failure(fromAreaId, opening.NeighborAreaId, position,
                $"area {fromAreaId} opening at ({position.X},{position.Y}) points to missing connection " +
                $"{opening.NeighborAreaId}");

        if (!region.Carved.TryGetValue(opening.NeighborAreaId, out var destination))
            return Failure(fromAreaId, opening.NeighborAreaId, position,
                $"connection {fromAreaId}↔{opening.NeighborAreaId} has no destination grid");

        // Touching WorldCanvas metadata here makes the runtime adapter fail loudly if an area was loaded
        // without the same normalized placement used by the Phase 2 stitched map.
        var destinationOrigin = region.World.OriginOf(opening.NeighborAreaId);
        var paired = region.Openings.EdgesOf(opening.NeighborAreaId)
            .FirstOrDefault(candidate => candidate.NeighborAreaId == fromAreaId);
        if (paired is null)
            return Failure(fromAreaId, opening.NeighborAreaId, position,
                $"connection {fromAreaId}↔{opening.NeighborAreaId} has no paired opening");

        var arrival = FindArrival(destination, opening.NeighborAreaId, paired, facing, destinationOrigin);
        if (arrival is null)
            return Failure(fromAreaId, opening.NeighborAreaId, position,
                $"connection {fromAreaId}↔{opening.NeighborAreaId} has no walkable arrival near " +
                $"({paired.TileOn(destination.Grid.Width, destination.Grid.Height).X}," +
                $"{paired.TileOn(destination.Grid.Width, destination.Grid.Height).Y})");

        return new TransitionResult(
            connection.Kind == ConnectionKind.Seamless
                ? TransitionResultKind.Seamless
                : TransitionResultKind.Warp,
            fromAreaId,
            opening.NeighborAreaId,
            arrival);
    }

    public static Facing OutwardFacing(EdgeSide edge) => edge switch
    {
        EdgeSide.Left => Facing.West,
        EdgeSide.Right => Facing.East,
        EdgeSide.Top => Facing.North,
        EdgeSide.Bottom => Facing.South,
        _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, null),
    };

    private static AreaArrival? FindArrival(
        CarvedArea destination, int areaId, AreaOpening opening, Facing facing, WorldOrigin origin)
    {
        var border = opening.TileOn(destination.Grid.Width, destination.Grid.Height);
        var preferred = InwardFrom(opening.Edge, border, destination.Grid.Width, destination.Grid.Height);
        var candidates = Enumerable.Range(0, destination.Grid.Height)
            .SelectMany(y => Enumerable.Range(0, destination.Grid.Width).Select(x => new GridPosition(x, y)))
            .OrderBy(candidate => Manhattan(candidate, preferred))
            .ThenBy(candidate => candidate.Y)
            .ThenBy(candidate => candidate.X);

        var arrival = candidates.FirstOrDefault(candidate =>
        {
            var tile = destination.Grid[candidate.X, candidate.Y];
            return tile.IsWalkable() && tile != LogicalTile.GateObstacle;
        });
        return arrival == default && !IsValidArrival(destination.Grid, arrival)
            ? null
            : new AreaArrival(areaId, arrival, facing, origin);
    }

    private static bool IsValidArrival(TileGrid grid, GridPosition position)
        => grid.InBounds(position.X, position.Y)
            && grid[position.X, position.Y].IsWalkable()
            && grid[position.X, position.Y] != LogicalTile.GateObstacle;

    private static GridPosition InwardFrom(EdgeSide edge, (int X, int Y) border, int width, int height)
        => edge switch
        {
            EdgeSide.Left => new GridPosition(Math.Min(1, width - 1), border.Y),
            EdgeSide.Right => new GridPosition(Math.Max(0, width - 2), border.Y),
            EdgeSide.Top => new GridPosition(border.X, Math.Min(1, height - 1)),
            _ => new GridPosition(border.X, Math.Max(0, height - 2)),
        };

    private static int Manhattan(GridPosition a, GridPosition b)
        => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private static TransitionResult Failure(int fromAreaId, int toAreaId, GridPosition position, string reason)
        => new(TransitionResultKind.Failed, fromAreaId, toAreaId,
            Failure: $"transition failed at area {fromAreaId} ({position.X},{position.Y}): {reason}");
}
