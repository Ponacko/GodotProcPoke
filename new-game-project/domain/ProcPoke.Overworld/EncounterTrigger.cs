using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Encounters;

namespace ProcPoke.Overworld;

public sealed record EncounterTriggerSettings(
    int LandChancePercent = 20,
    int SurfChancePercent = 15)
{
    public void Validate()
    {
        if (LandChancePercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(LandChancePercent));
        if (SurfChancePercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(SurfChancePercent));
    }
}

/// <summary>Pure movement-to-encounter seam consumed by the Godot movement controller.</summary>
public static class EncounterTriggerResolver
{
    public static WildEncounter? TryRoll(
        EncounterPlan plan,
        int areaId,
        TileGrid grid,
        MovementResolution movement,
        EncounterTriggerSettings settings,
        Func<int, int> nextInt)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(movement);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(nextInt);
        settings.Validate();

        if (movement.Result != MovementResultKind.Moved || movement.Target is not { } target
            || !grid.InBounds(target.X, target.Y)) return null;

        var tile = grid[target.X, target.Y];
        var method = tile switch
        {
            LogicalTile.TallGrass => EncounterMethod.Land,
            LogicalTile.Water when movement.State.Surfing => EncounterMethod.Surf,
            _ => (EncounterMethod?)null,
        };
        if (method is null || plan.Of(areaId)?.Tables.All(table => table.Method != method.Value) != false)
            return null;

        var chance = method == EncounterMethod.Land
            ? settings.LandChancePercent
            : settings.SurfChancePercent;
        if (chance == 0 || nextInt(100) >= chance) return null;
        return WildEncounterSelector.Select(plan, areaId, method.Value, false, nextInt);
    }
}
