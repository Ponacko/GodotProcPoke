using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Encounters;
using ProcPoke.Overworld;
using Xunit;

namespace ProcPoke.Generation.Tests;

public sealed class EncounterTriggerTests
{
    [Fact]
    public void MovingIntoGrassRollsLandEncountersAfterTheChanceCheck()
    {
        var plan = Plan(new EncounterTable(EncounterMethod.Land,
            [new EncounterSlot(100, 25, 3, 3)]));
        var grid = new TileGrid(2, 1);
        grid[1, 0] = LogicalTile.TallGrass;
        var movement = new MovementResolution(
            new PlayerState(new GridPosition(1, 0)), MovementResultKind.Moved, new GridPosition(1, 0));
        var values = new Queue<int>([0, 0, 0]);

        var encounter = EncounterTriggerResolver.TryRoll(
            plan, 7, grid, movement, new EncounterTriggerSettings(LandChancePercent: 100),
            _ => values.Dequeue());

        Assert.NotNull(encounter);
        Assert.Equal(25, encounter.SpeciesId);
        Assert.Equal(3, encounter.Level);
        Assert.Equal(EncounterMethod.Land, encounter.Method);
    }

    [Fact]
    public void WaterOnlyTriggersSurfEncountersWhileSurfing()
    {
        var plan = Plan(new EncounterTable(EncounterMethod.Surf,
            [new EncounterSlot(100, 129, 10, 12)]));
        var grid = new TileGrid(2, 1);
        grid[1, 0] = LogicalTile.Water;
        var movement = new MovementResolution(
            new PlayerState(new GridPosition(1, 0), Surfing: true), MovementResultKind.Moved,
            new GridPosition(1, 0));
        var values = new Queue<int>([0, 0, 2]);

        var encounter = EncounterTriggerResolver.TryRoll(
            plan, 7, grid, movement, new EncounterTriggerSettings(SurfChancePercent: 100),
            _ => values.Dequeue());

        Assert.Equal(12, encounter?.Level);
        Assert.Equal(EncounterMethod.Surf, encounter?.Method);
    }

    [Fact]
    public void NonEncounterMovementAndFailedChanceDoNotConsumeTableRolls()
    {
        var plan = Plan(new EncounterTable(EncounterMethod.Land,
            [new EncounterSlot(100, 25, 3, 3)]));
        var ground = new TileGrid(2, 1);
        var movement = new MovementResolution(
            new PlayerState(new GridPosition(1, 0)), MovementResultKind.Moved, new GridPosition(1, 0));
        var called = 0;

        Assert.Null(EncounterTriggerResolver.TryRoll(
            plan, 7, ground, movement, new EncounterTriggerSettings(), _ => called++));
        Assert.Equal(0, called);

        ground[1, 0] = LogicalTile.TallGrass;
        Assert.Null(EncounterTriggerResolver.TryRoll(
            plan, 7, ground, movement, new EncounterTriggerSettings(LandChancePercent: 0),
            _ => throw new InvalidOperationException("chance-zero roll should not occur")));
    }

    private static EncounterPlan Plan(EncounterTable table)
        => new()
        {
            ByArea = new Dictionary<int, AreaEncounters>
            {
                [7] = new AreaEncounters(7, [table], null, 5),
            },
        };
}
