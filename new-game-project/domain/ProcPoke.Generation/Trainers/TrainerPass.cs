using ProcPoke.Data;
using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Encounters;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Identity;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Roster;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Trainers;

/// <summary>
/// Populates carved trainer posts, gym-city trainers, and item balls (GDD §7.1). All choices use the
/// named <c>"trainers"</c> stream, and post scanning is explicitly row-major so tile output is stable.
/// </summary>
public static class TrainerPass
{
    private static readonly HashSet<AreaArchetype> LandArchetypes =
        [AreaArchetype.Route, AreaArchetype.Forest, AreaArchetype.StandardCave,
         AreaArchetype.MountainPath, AreaArchetype.DeepCave, AreaArchetype.VictoryRoad, AreaArchetype.Tower];

    public static TrainerPlan Generate(
        RegionGraph graph,
        IReadOnlyDictionary<int, CarvedArea> carved,
        BiomeMap biomes,
        RegionIdentity identity,
        DexPlan dex,
        GameData data,
        RngStreams streams)
    {
        var rng = streams.Stream("trainers");
        var anchors = GatingGenerator.OffSpineAnchors(graph);
        var availability = AvailabilityOrder.Of(graph, anchors);
        var availabilityIndex = availability
            .Select((id, index) => (id, index))
            .ToDictionary(x => x.id, x => x.index);
        var availableSpecies = AvailableSpeciesByArea(availability, dex);
        var gymPathIndices = identity.GymTypes.Keys
            .Select(id => graph[id].PathIndex)
            .OrderBy(pathIndex => pathIndex)
            .ToList();

        var trainersByArea = new Dictionary<int, IReadOnlyList<TrainerEncounter>>();
        var itemsByArea = new Dictionary<int, IReadOnlyList<ItemBallPlacement>>();
        var hideoutIndex = 0;

        foreach (var areaId in availability)
        {
            var area = graph[areaId];
            var fraction = AvailabilityFraction(availabilityIndex[areaId], availability.Count);
            var pool = availableSpecies[areaId];
            var trainers = new List<TrainerEncounter>();
            var areaAssignment = area.Archetype == AreaArchetype.VillainHideout
                ? ClassFor(area, biomes.Of(areaId), identity, ref hideoutIndex, rng)
                : null;

            foreach (var position in TrainerPosts(carved[areaId].Grid))
            {
                var assignment = areaAssignment
                    ?? ClassFor(area, biomes.Of(areaId), identity, ref hideoutIndex, rng);
                trainers.Add(CreateTrainer(area, areaId, position, assignment, pool, fraction,
                    graph, biomes, gymPathIndices, anchors, data, rng));
            }

            if (identity.GymTypes.TryGetValue(areaId, out var gymType))
                for (var i = 0; i < 2; i++)
                    trainers.Add(CreateGymTrainer(area, areaId, gymType, pool, fraction, graph, biomes,
                        gymPathIndices, anchors, data, rng));

            trainersByArea[areaId] = trainers;

            var items = ItemBalls(carved[areaId].Grid)
                .Select(position => new ItemBallPlacement(areaId, position, PickItem(fraction, data, rng)))
                .ToList();
            itemsByArea[areaId] = items;
        }

        return new TrainerPlan { ByArea = trainersByArea, ItemsByArea = itemsByArea };
    }

    private sealed record ClassAssignment(string ClassName, string? TeamName, IReadOnlySet<PokeType> Bias);

    private static ClassAssignment ClassFor(
        Area area, Biome biome, RegionIdentity identity, ref int hideoutIndex, Pcg32 rng)
    {
        if (area.Archetype == AreaArchetype.VillainHideout && identity.VillainTeams.Count > 0)
        {
            var team = identity.VillainTeams[hideoutIndex++ % identity.VillainTeams.Count];
            return new ClassAssignment("Grunt", team.Name, team.Motif.ToHashSet());
        }

        if (area.Archetype == AreaArchetype.VictoryRoad)
            return AnyClass("Veteran");
        if (area.Archetype == AreaArchetype.Tower)
            return BiasClass("Psychic", PokeType.Psychic, PokeType.Ghost);
        if (area.Archetype is AreaArchetype.StandardCave or AreaArchetype.MountainPath or AreaArchetype.DeepCave)
            return BiasClass("Hiker", PokeType.Rock, PokeType.Ground, PokeType.Fighting);
        if (area.Archetype == AreaArchetype.Forest || biome == Biome.Forest)
            return BiasClass("Bug Catcher", PokeType.Bug);

        return biome switch
        {
            Biome.Grassland => new ClassAssignment(rng.Pick(new[] { "Youngster", "Lass" }), null, new HashSet<PokeType>()),
            Biome.Mountain or Biome.Cave => BiasClass("Hiker", PokeType.Rock, PokeType.Ground, PokeType.Fighting),
            Biome.Water => new ClassAssignment(rng.Pick(new[] { "Swimmer", "Fisherman" }), null, new HashSet<PokeType> { PokeType.Water }),
            Biome.Desert => DesertClass(rng),
            Biome.Urban => new ClassAssignment(rng.Pick(new[] { "Gentleman", "Lady" }), null, new HashSet<PokeType>()),
            _ => AnyClass("Youngster"),
        };
    }

    private static ClassAssignment AnyClass(string className)
        => new(className, null, new HashSet<PokeType>());

    private static ClassAssignment BiasClass(string className, params PokeType[] bias)
        => new(className, null, bias.ToHashSet());

    private static ClassAssignment DesertClass(Pcg32 rng)
    {
        var className = rng.Pick(new[] { "Hiker", "Ace Trainer" });
        return className == "Hiker"
            ? BiasClass(className, PokeType.Rock, PokeType.Ground, PokeType.Fighting)
            : AnyClass(className);
    }

    private static TrainerEncounter CreateTrainer(
        Area area, int areaId, (int X, int Y) position, ClassAssignment assignment,
        IReadOnlyList<int> availableSpecies, double fraction, RegionGraph graph, BiomeMap biomes,
        IReadOnlyList<int> gymPathIndices, IReadOnlyDictionary<int, int> anchors, GameData data, Pcg32 rng)
    {
        var wild = WildLevel(area, graph, biomes, gymPathIndices, anchors);
        var ace = wild + 2 + rng.NextInt(3);
        var roster = BuildRoster(availableSpecies, assignment.Bias, RosterSize(fraction), ace, data, rng);
        return new TrainerEncounter(areaId, position, assignment.ClassName, assignment.TeamName, roster);
    }

    /// <summary>How many levels a gym's trainer band spans below its top.</summary>
    private const int GymTrainerBandDepth = 3;

    private static TrainerEncounter CreateGymTrainer(
        Area area, int areaId, PokeType gymType, IReadOnlyList<int> availableSpecies, double fraction,
        RegionGraph graph, BiomeMap biomes, IReadOnlyList<int> gymPathIndices,
        IReadOnlyDictionary<int, int> anchors, GameData data, Pcg32 rng)
    {
        var wild = GymRouteWildLevel(area, graph, biomes, gymPathIndices, anchors);
        var gymIndex = gymPathIndices.Select((pathIndex, index) => (pathIndex, index))
            .First(x => x.pathIndex == area.PathIndex).index;
        var ace = LevelCurve.GymAce(gymIndex, gymPathIndices.Count);

        // A tight band halfway between the routes outside the gym and the leader waiting inside, so the gym
        // is a step up on the way to a clear spike. Pegging the top of the band at the leader's ace minus two
        // only read correctly while wild levels ran five under the next gym; on the gentler wild curve that
        // put a gym's own trainers eight to ten levels above every route around it.
        var upper = Math.Max(wild, (wild + ace) / 2);
        var lower = Math.Max(wild, upper - GymTrainerBandDepth);

        var size = RosterSize(fraction);
        var roster = Enumerable.Range(0, size)
            .Select(_ => new TrainerMember(
                PickSpecies(availableSpecies, new HashSet<PokeType> { gymType }, data, rng),
                rng.NextIntInclusive(lower, upper)))
            .ToList();
        return new TrainerEncounter(areaId, null, "Ace Trainer", null, roster, IsGymTrainer: true);
    }

    private static IReadOnlyList<TrainerMember> BuildRoster(
        IReadOnlyList<int> availableSpecies, IReadOnlySet<PokeType> bias, int size, int ace,
        GameData data, Pcg32 rng)
    {
        var roster = new List<TrainerMember>(size);
        for (var i = 0; i < size; i++)
        {
            var level = i switch { 0 => ace, 1 => ace - 2, _ => ace - 3 };
            roster.Add(new TrainerMember(PickSpecies(availableSpecies, bias, data, rng), level));
        }
        return roster;
    }

    private static int PickSpecies(
        IReadOnlyList<int> availableSpecies, IReadOnlySet<PokeType> bias, GameData data, Pcg32 rng)
    {
        var typed = bias.Count == 0
            ? availableSpecies.ToList()
            : availableSpecies.Where(id => data.Species[id].Types.Any(bias.Contains)).ToList();
        IReadOnlyList<int> candidates = typed.Count > 0 ? typed : availableSpecies;
        return rng.Pick(candidates);
    }

    private static int RosterSize(double fraction)
        => 1 + (fraction >= 0.35 ? 1 : 0) + (fraction >= 0.7 ? 1 : 0);

    private static IReadOnlyDictionary<int, IReadOnlyList<int>> AvailableSpeciesByArea(
        IReadOnlyList<int> availability, DexPlan dex)
    {
        var result = new Dictionary<int, IReadOnlyList<int>>();
        var available = new List<int>();
        foreach (var areaId in availability)
        {
            if (dex.SpeciesByArea.TryGetValue(areaId, out var here))
                available.AddRange(here);
            result[areaId] = available.ToList();
        }
        return result;
    }

    private static IEnumerable<(int X, int Y)> TrainerPosts(TileGrid grid)
    {
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
                if (grid[x, y] == LogicalTile.TrainerPost) yield return (x, y);
    }

    private static IEnumerable<(int X, int Y)> ItemBalls(TileGrid grid)
    {
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
                if (grid[x, y] == LogicalTile.ItemBall) yield return (x, y);
    }

    private static int PickItem(double fraction, GameData data, Pcg32 rng)
    {
        IReadOnlyList<Item> pool = fraction < 1.0 / 3
            ? data.Items.Values.Where(i => i.Category == "healing" && i.Cost <= 700).ToList()
            : fraction < 2.0 / 3
                ? data.Items.Values.Where(i => (i.Category == "healing" && i.Cost <= 2000)
                    || i.Category == "standard-balls").ToList()
                : data.Items.Values.Where(i => i.Category is "revival" or "held-items").ToList();

        if (rng.Chance(0.1))
        {
            var machines = data.Items.Values.Where(i => i.Category == "all-machines").ToList();
            if (machines.Count > 0) pool = machines;
        }
        if (pool.Count == 0) throw new InvalidOperationException("item pool for TrainerPass is empty");
        return rng.Pick(pool).Id;
    }

    private static int WildLevel(
        Area area, RegionGraph graph, BiomeMap biomes, IReadOnlyList<int> gymPathIndices,
        IReadOnlyDictionary<int, int> anchors)
        => LevelCurve.WildLevel(area, graph, biomes.Of(area.Id), gymPathIndices, anchors);

    private static int GymRouteWildLevel(
        Area gym, RegionGraph graph, BiomeMap biomes, IReadOnlyList<int> gymPathIndices,
        IReadOnlyDictionary<int, int> anchors)
    {
        var previous = graph.CriticalPath
            .Where(a => a.PathIndex < gym.PathIndex)
            .Reverse()
            .FirstOrDefault(a => LandArchetypes.Contains(a.Archetype));
        if (previous is not null)
            return WildLevel(previous, graph, biomes, gymPathIndices, anchors);

        var gymIndex = gymPathIndices.Select((pathIndex, index) => (pathIndex, index))
            .First(x => x.pathIndex == gym.PathIndex).index;
        return Math.Max(2, LevelCurve.GymAce(gymIndex, gymPathIndices.Count) - 5);
    }

    private static double AvailabilityFraction(int index, int count) => count <= 1 ? 1 : index / (count - 1.0);
}
