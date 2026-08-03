using ProcPoke.Generation;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Overworld;

/// <summary>Resolved destination after the active party loses a battle.</summary>
public sealed record WhiteoutResult(
    int FromAreaId,
    int DestinationAreaId,
    bool WasAlreadyAtCenter);

/// <summary>
/// Chooses the last visited town as the Pokémon Center return point. The current MVP has no separate
/// building registration, so StartTown and Town areas are the generated Center locations.
/// </summary>
public static class WhiteoutResolver
{
    public static WhiteoutResult Resolve(
        GeneratedRegion region,
        int fromAreaId,
        int lastPokemonCenterAreaId)
    {
        ArgumentNullException.ThrowIfNull(region);
        var from = FindArea(region, fromAreaId);
        var center = FindArea(region, lastPokemonCenterAreaId);
        if (!IsPokemonCenter(center))
            throw new ArgumentException(
                $"Area {lastPokemonCenterAreaId} is not a Pokémon Center location.",
                nameof(lastPokemonCenterAreaId));

        return new WhiteoutResult(from.Id, center.Id, from.Id == center.Id);
    }

    /// <summary>The current MVP's Pokémon Center locations: every StartTown/Town area.</summary>
    public static bool IsPokemonCenter(Area area) => area.Archetype is AreaArchetype.StartTown or AreaArchetype.Town;

    private static Area FindArea(GeneratedRegion region, int areaId)
        => region.Graph.Areas.FirstOrDefault(area => area.Id == areaId)
            ?? throw new ArgumentOutOfRangeException(nameof(areaId), areaId, "area is not in this region");
}
