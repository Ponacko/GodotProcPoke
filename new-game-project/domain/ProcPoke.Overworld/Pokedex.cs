using ProcPoke.Generation;

namespace ProcPoke.Overworld;

/// <summary>
/// The player's own record of what they have met and owned — session state, never a property of the
/// generated region. Numbering belongs to the region's <see cref="Generation.Roster.DexPlan"/>; this only
/// tracks which species the player has ticked off, so the Phase 6 Pokédex screen and the completion
/// diploma read from one place.
/// </summary>
public sealed class PokedexState
{
    private readonly HashSet<int> _seen = [];
    private readonly HashSet<int> _caught = [];

    public IReadOnlySet<int> Seen => _seen;
    public IReadOnlySet<int> Caught => _caught;

    public bool HasSeen(int speciesId) => _seen.Contains(speciesId);
    public bool HasCaught(int speciesId) => _caught.Contains(speciesId);

    /// <summary>Met in battle, whether or not it was caught.</summary>
    public void MarkSeen(int speciesId) => _seen.Add(speciesId);

    /// <summary>Owned. Catching implies having seen it, so callers never need both.</summary>
    public void MarkCaught(int speciesId)
    {
        _seen.Add(speciesId);
        _caught.Add(speciesId);
    }
}

/// <summary>
/// The dex-completion payoff (GDD §11a): the professor's diploma for owning every species in the region's
/// dex. The regional dex is the numbered plan plus the four legendaries appended after it (§5.5), which is
/// why this asks the region rather than counting <see cref="PokedexState.Caught"/> — a Pokémon from another
/// region's evolution line can be owned without being a dex slot. Nothing hands the diploma out yet: the
/// professor exists only in the opening scene, with no overworld NPC to visit, so this states the rule that
/// visit will ask about.
/// </summary>
public static class DexDiploma
{
    /// <summary>Every species the regional dex numbers, in dex order.</summary>
    public static IReadOnlyList<int> RegionalDex(GeneratedRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);
        return region.Dex.Entries.OrderBy(entry => entry.Number).Select(entry => entry.SpeciesId)
            .Concat(region.Special.Legendaries.OrderBy(l => l.DexNumber).Select(l => l.SpeciesId))
            .Distinct()
            .ToArray();
    }

    /// <summary>How many of the region's dex slots the player owns.</summary>
    public static int CaughtCount(GeneratedRegion region, PokedexState pokedex)
    {
        ArgumentNullException.ThrowIfNull(pokedex);
        return RegionalDex(region).Count(pokedex.HasCaught);
    }

    public static bool IsEarned(GeneratedRegion region, PokedexState pokedex)
        => CaughtCount(region, pokedex) == RegionalDex(region).Count;
}
