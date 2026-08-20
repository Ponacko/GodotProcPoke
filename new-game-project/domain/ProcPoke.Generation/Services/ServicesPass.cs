using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Roster;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Services;

/// <summary>
/// The one-off city services (GDD §8a). Every kind is seated in a city house — the one town building
/// that carries no fixed role, so a service NPC displaces nothing. The Link Cable vendor is deliberately
/// absent: it is a Mart property, not an NPC post (see <see cref="ServicePlan.LinkCableVendorAreaId"/>).
/// </summary>
public enum ServiceKind
{
    FossilRevival,
    MoveDeleter,
    NameRater,
    MoveTutor,
}

/// <summary>
/// One service NPC, seated at the <paramref name="PostIndex"/>'th NPC post of the
/// <paramref name="HouseIndex"/>'th <see cref="BuildingKind.House"/> entrance of
/// <paramref name="AreaId"/> (both in <see cref="CarvedArea.BuildingEntrances"/> / row-major post order).
/// Indices — not tile positions — are what the plan states, so a service survives any change to how house
/// interiors are laid out.
/// </summary>
public sealed record ServicePlacement(ServiceKind Kind, int AreaId, int HouseIndex, int PostIndex);

/// <summary>Which city hosts which service. Every <see cref="ServiceKind"/> is present exactly once,
/// except <see cref="ServiceKind.MoveTutor"/>, which scales with region size.</summary>
public sealed record ServicePlan
{
    public required IReadOnlyList<ServicePlacement> Placements { get; init; }

    /// <summary>The city whose Mart stocks Link Cables regardless of progression — the "one guaranteed
    /// earlier vendor" of GDD §8a, distinct from the late-game Marts that stock them by price ceiling
    /// alone.</summary>
    public required int LinkCableVendorAreaId { get; init; }

    public IReadOnlyList<ServicePlacement> Of(int areaId)
        => Placements.Where(placement => placement.AreaId == areaId).ToArray();

    /// <summary>The services seated in one specific house, in post order (usually none — most houses are
    /// ordinary houses).</summary>
    public IReadOnlyList<ServicePlacement> InHouse(int areaId, int houseIndex)
        => Placements
            .Where(placement => placement.AreaId == areaId && placement.HouseIndex == houseIndex)
            .OrderBy(placement => placement.PostIndex)
            .ToArray();

    /// <summary>Where a service lives. Every kind is guaranteed present, so this never returns null for a
    /// plan this pass produced.</summary>
    public ServicePlacement? FirstOf(ServiceKind kind)
        => Placements.FirstOrDefault(placement => placement.Kind == kind);
}

/// <summary>
/// The services pass (GDD §8a): 0–2 one-off services per city, under per-service placement constraints —
/// fossil revival in the nearest city to the fossil cave that can seat it, the Move Deleter by mid-game (the
/// HM escape hatch must land before HM-slot regret gets expensive), the Name Rater and move tutors anywhere.
/// A city with no house hosts none — houses are where services sit, so a house-less town is skipped, and
/// every service is present as long as the region's cities offer at least one seat each (they always do at
/// the badge counts anyone plays; a two-city one-badge region can run out). Draws from the
/// <c>"services"</c> stream.
/// </summary>
public static class ServicesPass
{
    /// <summary>The GDD's per-city ceiling. Yielded only when the region's houses can't otherwise seat every
    /// guaranteed service — a two-city region at one badge has nowhere else to put them.</summary>
    private const int PreferredPerCity = 2;

    /// <summary>NPC posts a house interior offers (see the Overworld's house template). Two services can
    /// share a house, which is what keeps the smallest regions seatable at all. Public because the shared
    /// invariant checker needs the same seat arithmetic.</summary>
    public const int PostsPerHouse = 2;

    /// <summary>Availability fraction a "by mid-game" service must not exceed.</summary>
    private const double MidGameFraction = 0.5;

    private const int MaxTutors = 3;

    public static ServicePlan Generate(
        RegionGraph graph,
        IReadOnlyDictionary<int, CarvedArea> carved,
        DexPlan dex,
        RngStreams streams)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(carved);
        ArgumentNullException.ThrowIfNull(dex);
        ArgumentNullException.ThrowIfNull(streams);

        var rng = streams.Stream("services");
        var order = AvailabilityOrder.Of(graph, GatingGenerator.OffSpineAnchors(graph));
        var cities = order.Where(areaId => graph[areaId].Archetype.IsCity()).ToList();
        var houses = cities.ToDictionary(areaId => areaId, areaId => HouseCount(carved[areaId]));
        var guaranteed = Enum.GetValues<ServiceKind>();

        // The 0–2 spread is the preference, not the rule: a region whose cities cannot seat every service
        // two-at-a-time fills its houses instead, because a missing Move Deleter is worse than a crowded town.
        var spreadFits = houses.Values.Sum(count => Math.Min(PreferredPerCity, count * PostsPerHouse))
            >= guaranteed.Length;
        var capacity = houses.ToDictionary(
            pair => pair.Key,
            pair => spreadFits
                ? Math.Min(PreferredPerCity, pair.Value * PostsPerHouse)
                : pair.Value * PostsPerHouse);
        var seated = cities.ToDictionary(areaId => areaId, _ => 0);
        var fractions = AvailabilityFractions(order);
        var fossilDistances = HopsFrom(graph, dex.FossilAreaId);

        var placements = new List<ServicePlacement>();

        // Most-constrained first: fossil revival takes the one city its constraint names, then the mid-game
        // service claims an early slot, and the unconstrained ones fill whatever is left. The order also
        // decides who loses out in the pathological case of a region with almost no houses at all.
        Place(ServiceKind.FossilRevival, candidates => NearestTo(candidates, fossilDistances));
        Place(ServiceKind.MoveDeleter, candidates => Early(candidates, fractions, rng));
        Place(ServiceKind.NameRater, candidates => rng.Pick(candidates));
        Place(ServiceKind.MoveTutor, candidates => rng.Pick(candidates));

        // Extra tutors are the one size-scaled service (a big region with a single tutor reads empty), but
        // they never crowd out the spread: they take only slots the guaranteed set left behind.
        var extraTutors = Math.Min(
            Math.Min(MaxTutors - 1, cities.Count / 4),
            capacity.Values.Sum() - placements.Count);
        for (var i = 0; i < extraTutors; i++)
            Place(ServiceKind.MoveTutor, candidates => rng.Pick(candidates));

        // The vendor is a Mart, so it needs no house — only a city early enough to be the "earlier vendor".
        var vendorCandidates = cities.Where(areaId => fractions[areaId] <= MidGameFraction).ToList();
        var vendor = rng.Pick(vendorCandidates.Count > 0 ? vendorCandidates : cities);

        return new ServicePlan
        {
            Placements = placements,
            LinkCableVendorAreaId = vendor,
        };

        void Place(ServiceKind kind, Func<IReadOnlyList<int>, int> choose)
        {
            var open = cities.Where(areaId => seated[areaId] < capacity[areaId]).ToList();
            // Nowhere left to sit. Generation never fails over furniture; Invariants.CheckServices reports
            // the gap, so a region this house-poor shows up in the packet sweep instead of crashing.
            if (open.Count == 0) return;

            var areaId = choose(open);
            // Seats spread across a city's houses before doubling up inside one, so two services share a
            // house only when the city has just the one.
            var seat = seated[areaId]++;
            placements.Add(new ServicePlacement(
                kind, areaId, HouseIndex: seat % houses[areaId], PostIndex: seat / houses[areaId]));
        }
    }

    private static int HouseCount(CarvedArea carved)
        => carved.BuildingEntrances.Count(entrance => entrance.Kind == BuildingKind.House);

    /// <summary>Each area's position in first-availability order, normalized to [0, 1] — the same
    /// progression yardstick the encounter and trainer passes scale by.</summary>
    private static Dictionary<int, double> AvailabilityFractions(IReadOnlyList<int> order)
        => order
            .Select((areaId, index) => (areaId, fraction: order.Count <= 1 ? 1 : index / (order.Count - 1.0)))
            .ToDictionary(pair => pair.areaId, pair => pair.fraction);

    /// <summary>The earliest city at or before mid-game, chosen at random among those that qualify. A
    /// region whose early cities are all full yields the constraint rather than dropping the service —
    /// the escape hatch existing late beats it not existing.</summary>
    private static int Early(IReadOnlyList<int> candidates, IReadOnlyDictionary<int, double> fractions, Pcg32 rng)
    {
        var early = candidates.Where(areaId => fractions[areaId] <= MidGameFraction).ToList();
        return rng.Pick(early.Count > 0 ? early : candidates);
    }

    /// <summary>Fewest graph hops from the target, ties broken by availability order (candidates arrive in
    /// it), so the choice is deterministic without touching the stream.</summary>
    private static int NearestTo(IReadOnlyList<int> candidates, IReadOnlyDictionary<int, int> hops)
        => candidates.OrderBy(areaId => hops.GetValueOrDefault(areaId, int.MaxValue)).First();

    /// <summary>Hop counts to every area reachable from <paramref name="fromAreaId"/>, ignoring gates —
    /// this is a "which city is nearest the cave" question, not a solvability one.</summary>
    private static Dictionary<int, int> HopsFrom(RegionGraph graph, int fromAreaId)
    {
        var hops = new Dictionary<int, int> { [fromAreaId] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(fromAreaId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var neighbor in graph.Neighbors(current))
            {
                if (hops.ContainsKey(neighbor.Id)) continue;
                hops[neighbor.Id] = hops[current] + 1;
                queue.Enqueue(neighbor.Id);
            }
        }
        return hops;
    }
}
