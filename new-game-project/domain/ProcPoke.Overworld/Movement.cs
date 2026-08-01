using ProcPoke.Generation.Carving;

namespace ProcPoke.Overworld;

public readonly record struct GridPosition(int X, int Y)
{
    public GridPosition Step(Facing facing) => facing switch
    {
        Facing.North => new GridPosition(X, Y - 1),
        Facing.East => new GridPosition(X + 1, Y),
        Facing.South => new GridPosition(X, Y + 1),
        Facing.West => new GridPosition(X - 1, Y),
        _ => this,
    };
}

public enum Facing
{
    North,
    East,
    South,
    West,
}

public enum MovementMode
{
    Walk,
    Run,
    Bicycle,
}

public enum MovementIntent
{
    MoveNorth,
    MoveEast,
    MoveSouth,
    MoveWest,
    Confirm,
    ToggleRun,
    ToggleBicycle,
    ToggleSurf,
}

public enum MovementResultKind
{
    Moved,
    Blocked,
    Interacted,
    ModeChanged,
    NoOp,
}

public sealed record PlayerState(
    GridPosition Position,
    Facing Facing = Facing.South,
    MovementMode Mode = MovementMode.Walk,
    bool Surfing = false);

public sealed record MovementCapabilities(
    bool HasSurf = false,
    bool HasStrength = false,
    bool HasCut = false,
    bool CanBicycle = false,
    bool DebugUnlock = false)
{
    /// <summary>Gate tiles cleared by the session; generation's logical grid is never mutated.</summary>
    public IReadOnlySet<GridPosition> ClearedGatePositions { get; init; } = new HashSet<GridPosition>();
}

public sealed record MovementResolution(
    PlayerState State,
    MovementResultKind Result,
    GridPosition? Target = null,
    LogicalTile? BlockingTile = null);

/// <summary>Pure grid movement and interaction rules shared by headless tests and the Godot controller.</summary>
public static class MovementResolver
{
    public static MovementResolution Resolve(
        TileGrid grid,
        PlayerState player,
        MovementIntent intent,
        MovementCapabilities capabilities,
        Func<GridPosition, bool>? isInteractable = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(capabilities);

        if (intent == MovementIntent.Confirm)
            return ResolveInteraction(grid, player, isInteractable);

        if (intent == MovementIntent.ToggleRun)
        {
            if (player.Surfing) return new MovementResolution(player, MovementResultKind.Blocked);
            var mode = player.Mode switch
            {
                MovementMode.Run or MovementMode.Bicycle => MovementMode.Walk,
                _ => MovementMode.Run,
            };
            return new MovementResolution(player with { Mode = mode }, MovementResultKind.ModeChanged);
        }

        if (intent == MovementIntent.ToggleBicycle)
        {
            if (player.Surfing || (!capabilities.CanBicycle && !capabilities.DebugUnlock))
                return new MovementResolution(player, MovementResultKind.Blocked);
            var mode = player.Mode == MovementMode.Bicycle ? MovementMode.Walk : MovementMode.Bicycle;
            return new MovementResolution(player with { Mode = mode }, MovementResultKind.ModeChanged);
        }

        if (intent == MovementIntent.ToggleSurf)
            return ResolveSurf(grid, player, capabilities);

        var direction = DirectionFor(intent);
        var facing = direction ?? player.Facing;
        if (direction is null) return new MovementResolution(player, MovementResultKind.NoOp);

        var facingPlayer = player with { Facing = facing };
        var target = player.Position.Step(facing);
        if (!grid.InBounds(target.X, target.Y))
            return new MovementResolution(facingPlayer, MovementResultKind.Blocked, target);

        var tile = grid[target.X, target.Y];
        if (!CanEnter(tile, target, facing, player, capabilities))
            return new MovementResolution(facingPlayer, MovementResultKind.Blocked, target, tile);

        return new MovementResolution(
            facingPlayer with { Position = target }, MovementResultKind.Moved, target);
    }

    public static int TilesPerSecond(MovementMode mode) => mode switch
    {
        MovementMode.Walk => 4,
        MovementMode.Run => 8,
        MovementMode.Bicycle => 12,
        _ => 4,
    };

    private static MovementResolution ResolveInteraction(
        TileGrid grid, PlayerState player, Func<GridPosition, bool>? isInteractable)
    {
        var target = player.Position.Step(player.Facing);
        if (grid.InBounds(target.X, target.Y) && isInteractable?.Invoke(target) == true)
            return new MovementResolution(player, MovementResultKind.Interacted, target);
        return new MovementResolution(player, MovementResultKind.NoOp, target);
    }

    private static MovementResolution ResolveSurf(
        TileGrid grid, PlayerState player, MovementCapabilities capabilities)
    {
        if (player.Surfing)
        {
            var onWater = grid.InBounds(player.Position.X, player.Position.Y)
                && grid[player.Position.X, player.Position.Y] == LogicalTile.Water;
            if (onWater) return new MovementResolution(player, MovementResultKind.Blocked);
            return new MovementResolution(player with { Surfing = false }, MovementResultKind.ModeChanged);
        }
        if (!capabilities.HasSurf && !capabilities.DebugUnlock)
            return new MovementResolution(player, MovementResultKind.Blocked);
        return new MovementResolution(player with { Surfing = true }, MovementResultKind.ModeChanged);
    }

    private static bool CanEnter(
        LogicalTile tile, GridPosition target, Facing direction, PlayerState player,
        MovementCapabilities capabilities)
    {
        var collision = TileRealizerModel.CollisionFor(tile);
        if (capabilities.ClearedGatePositions.Contains(target)) return true;
        if (collision.HasFlag(TileCollision.OneWaySouth) && direction != Facing.South)
            return false;
        if (tile == LogicalTile.Water)
            return player.Surfing || capabilities.HasSurf || capabilities.DebugUnlock;
        if (collision.HasFlag(TileCollision.GateLocked))
            return capabilities.DebugUnlock;
        if (collision.HasFlag(TileCollision.StrengthRequired))
            return capabilities.HasStrength || capabilities.DebugUnlock;
        if (collision.HasFlag(TileCollision.CutRequired))
            return capabilities.HasCut || capabilities.DebugUnlock;
        return !collision.HasFlag(TileCollision.Solid);
    }

    private static Facing? DirectionFor(MovementIntent intent) => intent switch
    {
        MovementIntent.MoveNorth => Facing.North,
        MovementIntent.MoveEast => Facing.East,
        MovementIntent.MoveSouth => Facing.South,
        MovementIntent.MoveWest => Facing.West,
        _ => null,
    };
}

public readonly record struct CameraCenter(float X, float Y);

/// <summary>Clamps a camera center to an area's tile bounds, including maps smaller than the viewport.</summary>
public static class CameraBounds
{
    public static CameraCenter Clamp(
        GridPosition player, int mapWidth, int mapHeight, float viewportWidth, float viewportHeight)
    {
        if (mapWidth <= 0 || mapHeight <= 0) return new CameraCenter(0, 0);
        var centerX = ClampAxis(player.X + 0.5f, mapWidth, viewportWidth);
        var centerY = ClampAxis(player.Y + 0.5f, mapHeight, viewportHeight);
        return new CameraCenter(centerX, centerY);
    }

    private static float ClampAxis(float playerCenter, int mapSize, float viewportSize)
    {
        var halfViewport = Math.Max(0, viewportSize / 2f);
        if (mapSize <= viewportSize) return mapSize / 2f;
        return Math.Clamp(playerCenter, halfViewport, mapSize - halfViewport);
    }
}
