using ProcPoke.Generation.Carving;
using ProcPoke.Overworld;
using Xunit;

namespace ProcPoke.Generation.Tests;

public sealed class MovementTests
{
    [Fact]
    public void DirectionalInputUpdatesFacingEvenWhenBlocked()
    {
        var grid = new TileGrid(3, 1);
        grid[1, 0] = LogicalTile.Wall;
        var player = new PlayerState(new GridPosition(0, 0), Facing.South);

        var result = MovementResolver.Resolve(
            grid, player, MovementIntent.MoveEast, new MovementCapabilities());

        Assert.Equal(MovementResultKind.Blocked, result.Result);
        Assert.Equal(Facing.East, result.State.Facing);
        Assert.Equal(player.Position, result.State.Position);
        Assert.Equal(LogicalTile.Wall, result.BlockingTile);
    }

    [Fact]
    public void ConfirmChecksFacingTileBeforeAnyMovement()
    {
        var grid = new TileGrid(3, 1);
        var player = new PlayerState(new GridPosition(0, 0), Facing.East);
        var interacted = new List<GridPosition>();

        var result = MovementResolver.Resolve(
            grid, player, MovementIntent.Confirm, new MovementCapabilities(),
            position =>
            {
                interacted.Add(position);
                return true;
            });

        Assert.Equal(MovementResultKind.Interacted, result.Result);
        Assert.Equal([new GridPosition(1, 0)], interacted);
        Assert.Equal(player, result.State);
    }

    [Fact]
    public void LedgesOnlyAllowNorthToSouthEntry()
    {
        var grid = new TileGrid(2, 3);
        grid[0, 1] = LogicalTile.Ledge;
        var player = new PlayerState(new GridPosition(0, 0));

        var north = MovementResolver.Resolve(
            grid, player with { Position = new GridPosition(0, 2), Facing = Facing.North },
            MovementIntent.MoveNorth, new MovementCapabilities());
        var south = MovementResolver.Resolve(
            grid, player, MovementIntent.MoveSouth, new MovementCapabilities());

        Assert.Equal(MovementResultKind.Blocked, north.Result);
        Assert.Equal(MovementResultKind.Moved, south.Result);
        Assert.Equal(new GridPosition(0, 1), south.State.Position);
    }

    [Fact]
    public void WaterNeedsSurfAndDebugUnlockOverridesAllFieldObstacles()
    {
        var grid = new TileGrid(4, 1);
        grid[1, 0] = LogicalTile.Water;
        grid[2, 0] = LogicalTile.Boulder;
        grid[3, 0] = LogicalTile.GateObstacle;
        var player = new PlayerState(new GridPosition(0, 0), Facing.East);

        Assert.Equal(MovementResultKind.Blocked,
            MovementResolver.Resolve(grid, player, MovementIntent.MoveEast, new MovementCapabilities()).Result);
        Assert.Equal(MovementResultKind.ModeChanged,
            MovementResolver.Resolve(grid, player, MovementIntent.ToggleSurf, new MovementCapabilities(HasSurf: true)).Result);

        var surfing = player with { Surfing = true };
        Assert.Equal(new GridPosition(1, 0),
            MovementResolver.Resolve(grid, surfing, MovementIntent.MoveEast, new MovementCapabilities()).State.Position);
        Assert.Equal(MovementResultKind.Blocked,
            MovementResolver.Resolve(grid, surfing with { Position = new GridPosition(1, 0) },
                MovementIntent.ToggleSurf, new MovementCapabilities()).Result);
        Assert.Equal(MovementResultKind.Moved,
            MovementResolver.Resolve(grid, surfing with { Position = new GridPosition(1, 0) },
                MovementIntent.MoveEast, new MovementCapabilities(DebugUnlock: true)).Result);
    }

    [Fact]
    public void RunAndBicycleAreDistinctSpeedTiersAndRequireTheirRules()
    {
        var grid = new TileGrid(1, 1);
        var player = new PlayerState(new GridPosition(0, 0));

        var running = MovementResolver.Resolve(
            grid, player, MovementIntent.ToggleRun, new MovementCapabilities());
        var deniedBike = MovementResolver.Resolve(
            grid, player, MovementIntent.ToggleBicycle, new MovementCapabilities());
        var bike = MovementResolver.Resolve(
            grid, player, MovementIntent.ToggleBicycle, new MovementCapabilities(CanBicycle: true));

        Assert.Equal(MovementMode.Run, running.State.Mode);
        Assert.Equal(8, MovementResolver.TilesPerSecond(running.State.Mode));
        Assert.Equal(MovementResultKind.Blocked, deniedBike.Result);
        Assert.Equal(MovementMode.Bicycle, bike.State.Mode);
        Assert.Equal(12, MovementResolver.TilesPerSecond(bike.State.Mode));
    }

    [Fact]
    public void CameraNeverExposesSpaceOutsideTheArea()
    {
        Assert.Equal(new CameraCenter(3, 2), CameraBounds.Clamp(new GridPosition(0, 0), 20, 12, 6, 4));
        Assert.Equal(new CameraCenter(17, 10), CameraBounds.Clamp(new GridPosition(19, 11), 20, 12, 6, 4));
        Assert.Equal(new CameraCenter(1.5f, 1), CameraBounds.Clamp(new GridPosition(0, 0), 3, 2, 8, 6));
    }
}
