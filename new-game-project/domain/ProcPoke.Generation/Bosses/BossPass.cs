using ProcPoke.Data;
using ProcPoke.Generation.Encounters;
using ProcPoke.Generation.Identity;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Roster;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Bosses;

/// <summary>Builds gym leader, Elite Four, and Champion rosters from the regional dex (GDD §7.1).</summary>
public static class BossPass
{
    private static readonly int[] ChampionLevels = [57, 57, 58, 58, 58, 60];
    private static readonly int[] PseudoLegendaryFinals = [149, 248, 373, 376, 445, 635];

    public static BossPlan Generate(
        RegionGraph graph,
        RegionIdentity identity,
        DexPlan dex,
        StarterPlan starters,
        GenerationSettings settings,
        GameData data,
        RngStreams streams)
    {
        var rng = streams.Stream("bosses");
        var dexSpecies = dex.Entries
            .Where(e => data.Species.ContainsKey(e.SpeciesId))
            .Select(e => e.SpeciesId)
            .Distinct()
            .ToList();
        var familyOf = FamilyMap(data, settings.RosterCap);
        var gymCities = identity.GymTypes
            .OrderBy(kv => graph[kv.Key].PathIndex)
            .ToList();

        var leaders = new Dictionary<int, BossTeam>();
        for (var i = 0; i < gymCities.Count; i++)
        {
            var (areaId, type) = gymCities[i];
            var fraction = gymCities.Count <= 1 ? 1 : i / (gymCities.Count - 1.0);
            var size = Math.Min(6, 2 + (int)Math.Round(4 * fraction));
            var ace = LevelCurve.GymAce(i, gymCities.Count);
            var levels = new[] { ace, ace - 2, ace - 3, ace - 4, ace - 4, ace - 5 };
            var candidates = dexSpecies.Where(id => data.Species[id].Types.Contains(type)).ToList();
            var selected = SelectLeaderSpecies(candidates, dexSpecies, size, fraction, data, familyOf);
            leaders[areaId] = new BossTeam(
                $"Gym Leader {i + 1}", type,
                selected.Select((speciesId, slot) => new BossMember(speciesId, levels[slot])).ToList());
        }

        var elite = new List<BossTeam>();
        for (var i = 0; i < identity.EliteFourTypes.Count; i++)
        {
            var type = identity.EliteFourTypes[i];
            var typed = dexSpecies.Where(id => data.Species[id].Types.Contains(type)).ToList();
            var selected = SelectTeamSpecies(typed, dexSpecies, 6, data, familyOf);
            var ace = 55 + i;
            var levels = new[] { ace - 2, ace - 2, ace - 1, ace - 1, ace - 1, ace };
            elite.Add(new BossTeam(
                $"Elite Four {i + 1}", type,
                selected.Select((speciesId, slot) => new BossMember(speciesId, levels[slot])).ToList()));
        }

        var champion = BuildChampion("Champion", dexSpecies, starters, settings, data, rng);

        return new BossPlan { GymLeaders = leaders, EliteFour = elite, Champion = champion };
    }

    /// <summary>Builds the 7b Champion formula, optionally forcing its mandatory species.</summary>
    /// <remarks>
    /// The rival pass uses this same selector for the three possible rival-Champion variants. Keeping the
    /// selector here prevents the replacement team from drifting from the generated-NPC Champion rules.
    /// </remarks>
    internal static BossTeam BuildChampion(
        string name,
        IReadOnlyList<int> dexSpecies,
        StarterPlan starters,
        GenerationSettings settings,
        GameData data,
        Pcg32 rng,
        int? forcedSpeciesId = null)
    {
        var familyOf = FamilyMap(data, settings.RosterCap);
        var championSpecies = SelectChampionSpecies(dexSpecies, starters, data, familyOf, rng, forcedSpeciesId);
        return new BossTeam(
            name, null,
            championSpecies.Select((speciesId, slot) => new BossMember(speciesId, ChampionLevels[slot])).ToList());
    }

    private static IReadOnlyDictionary<int, int> FamilyMap(GameData data, int rosterCap)
    {
        var map = new Dictionary<int, int>();
        var familyIndex = 0;
        foreach (var family in EvolutionFamilies.Build(data, rosterCap))
        {
            foreach (var speciesId in family.Members) map[speciesId] = familyIndex;
            familyIndex++;
        }
        return map;
    }

    private static IReadOnlyList<int> SelectLeaderSpecies(
        IReadOnlyList<int> typed, IReadOnlyList<int> allDex, int size, double fraction,
        GameData data, IReadOnlyDictionary<int, int> familyOf)
    {
        var bstLimit = 250 + 350 * fraction;
        var capped = typed.Where(id => StarterSelector.Bst(data.Species[id]) <= bstLimit).ToList();
        var selected = SelectHighestDistinct(capped, size, data, familyOf);
        if (selected.Count < size)
            selected = AddHighestDistinct(selected, typed, size, data, familyOf);
        if (selected.Count < size)
            selected = AddHighestDistinct(selected, allDex, size, data, familyOf);
        if (selected.Count < size)
            throw new InvalidOperationException("regional dex cannot supply a full boss team");
        return selected
            .OrderByDescending(id => StarterSelector.Bst(data.Species[id]))
            .ThenBy(id => id)
            .ToList();
    }

    private static IReadOnlyList<int> SelectTeamSpecies(
        IReadOnlyList<int> typed, IReadOnlyList<int> allDex, int size, GameData data,
        IReadOnlyDictionary<int, int> familyOf)
    {
        var selected = SelectHighestDistinct(typed, size, data, familyOf);
        if (selected.Count < size && typed.Count > 0)
        {
            var orderedTyped = typed
                .OrderByDescending(id => StarterSelector.Bst(data.Species[id]))
                .ThenBy(id => id)
                .ToList();
            var initialCount = selected.Count;
            for (var offset = 0; offset < size - initialCount; offset++)
                selected = selected.Append(orderedTyped[offset % orderedTyped.Count]).ToList();
        }
        if (selected.Count < size)
            selected = AddHighestDistinct(selected, allDex, size, data, familyOf);
        if (selected.Count < size)
            throw new InvalidOperationException("regional dex cannot supply a full Elite Four team");
        return selected;
    }

    private static IReadOnlyList<int> SelectHighestDistinct(
        IReadOnlyList<int> pool, int size, GameData data, IReadOnlyDictionary<int, int> familyOf)
        => AddHighestDistinct([], pool, size, data, familyOf);

    private static IReadOnlyList<int> AddHighestDistinct(
        IReadOnlyList<int> existing, IReadOnlyList<int> pool, int size, GameData data,
        IReadOnlyDictionary<int, int> familyOf)
    {
        var selected = existing.ToList();
        var usedFamilies = selected.Select(id => FamilyOf(id, familyOf)).ToHashSet();
        var candidates = pool
            .Where(id => !selected.Contains(id))
            .OrderByDescending(id => StarterSelector.Bst(data.Species[id]))
            .ThenBy(id => id)
            .ToList();

        foreach (var candidate in candidates)
        {
            var family = FamilyOf(candidate, familyOf);
            if (!usedFamilies.Add(family)) continue;
            selected.Add(candidate);
            if (selected.Count == size) break;
        }

        // A malformed or very small dex can exhaust distinct families. Keep the team complete while
        // retaining the highest-BST ordering; the normal regional-dex settings never take this branch.
        if (selected.Count < size)
            foreach (var candidate in candidates)
            {
                if (selected.Contains(candidate)) continue;
                selected.Add(candidate);
                if (selected.Count == size) break;
            }
        return selected;
    }

    private static IReadOnlyList<int> SelectChampionSpecies(
        IReadOnlyList<int> dexSpecies, StarterPlan starters, GameData data,
        IReadOnlyDictionary<int, int> familyOf, Pcg32 rng, int? forcedSpeciesId = null)
    {
        if (forcedSpeciesId is int forced && !dexSpecies.Contains(forced))
            throw new InvalidOperationException("forced Champion species must be present in the regional dex");

        var pseudo = dexSpecies
            .Where(id => PseudoLegendaryFinals.Contains(id))
            .OrderByDescending(id => StarterSelector.Bst(data.Species[id]))
            .ThenBy(id => id)
            .FirstOrDefault();
        var mandatory = forcedSpeciesId ?? (pseudo != 0
            ? pseudo
            : rng.Pick(starters.Corners.Select(c => c.FinalSpeciesId).ToList()));

        var selected = new List<int> { mandatory };
        var usedFamilies = new HashSet<int> { FamilyOf(mandatory, familyOf) };
        var typeCounts = new Dictionary<PokeType, int>();
        AddTypes(typeCounts, data.Species[mandatory]);

        var candidates = dexSpecies
            .Where(id => id != mandatory)
            .OrderByDescending(id => StarterSelector.Bst(data.Species[id]))
            .ThenBy(id => id)
            .ToList();
        foreach (var candidate in candidates)
        {
            if (selected.Count == 6) break;
            if (!usedFamilies.Add(FamilyOf(candidate, familyOf))) continue;
            if (WouldExceedTypeLimit(typeCounts, data.Species[candidate]))
            {
                usedFamilies.Remove(FamilyOf(candidate, familyOf));
                continue;
            }
            selected.Add(candidate);
            AddTypes(typeCounts, data.Species[candidate]);
        }

        // Preserve distinct families if the type-cap constraint cannot fill six slots in a small dex.
        foreach (var candidate in candidates)
        {
            if (selected.Count == 6) break;
            if (!usedFamilies.Add(FamilyOf(candidate, familyOf))) continue;
            selected.Add(candidate);
        }
        if (selected.Count < 6)
            throw new InvalidOperationException("regional dex cannot supply a full Champion team");
        return selected;
    }

    private static bool WouldExceedTypeLimit(Dictionary<PokeType, int> counts, PokemonSpecies species)
        => species.Types.Any(type => counts.GetValueOrDefault(type) >= 2);

    private static void AddTypes(Dictionary<PokeType, int> counts, PokemonSpecies species)
    {
        foreach (var type in species.Types.Distinct())
            counts[type] = counts.GetValueOrDefault(type) + 1;
    }

    private static int FamilyOf(int speciesId, IReadOnlyDictionary<int, int> familyOf)
        => familyOf.TryGetValue(speciesId, out var family) ? family : -speciesId;
}
