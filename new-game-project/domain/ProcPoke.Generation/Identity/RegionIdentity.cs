using ProcPoke.Data;

namespace ProcPoke.Generation.Identity;

/// <summary>Gym/Elite Four/Champion type assignments (§7.1). Champion is always typeless — the broadest,
/// highest-BST team in the region (7b builds the actual team).</summary>
public sealed record RegionIdentity
{
    /// <summary>Gym-city area id → its assigned type, one per <see cref="Topology.AreaArchetype.Town"/>
    /// on the critical path, in critical-path order.</summary>
    public required IReadOnlyDictionary<int, PokeType> GymTypes { get; init; }

    /// <summary>Exactly 4 types, distinct from each other and (while the 17-type count allows) from every
    /// gym type.</summary>
    public required IReadOnlyList<PokeType> EliteFourTypes { get; init; }

    public bool ChampionIsTypeless { get; init; } = true;
}
