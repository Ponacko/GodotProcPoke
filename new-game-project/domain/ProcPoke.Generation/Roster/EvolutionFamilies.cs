using ProcPoke.Data;

namespace ProcPoke.Generation.Roster;

/// <summary>
/// A maximal evolution family within the roster cap: a connected component of the evolution graph, keeping
/// a line together so the dex can seat it in consecutive slots (GDD §5.1). Generalises the strict 3-stage
/// lines in <see cref="StarterSelector"/> to branching (Eevee) and short (1–2 stage) families.
/// </summary>
public sealed record EvolutionFamily
{
    /// <summary>Members in dex-seat order: BFS from the root (the in-cap member with no in-cap pre-evolution;
    /// lowest id if several), children visited in ascending ToSpeciesId.</summary>
    public required IReadOnlyList<int> Members { get; init; }

    /// <summary>Max BST among members with no in-cap evolution (the fully-evolved forms at this cap).</summary>
    public required int FinalBst { get; init; }

    /// <summary>Union of every member's types.</summary>
    public required IReadOnlyList<PokeType> Types { get; init; }

    public required bool IsLegendary { get; init; }
    public required bool IsFossil { get; init; }
}

public static class EvolutionFamilies
{
    /// <summary>Fossil species (GDD §5.4) — not flagged in the baked data, so hardcoded. Family membership
    /// below tags a whole family fossil if any member is in this set.</summary>
    private static readonly HashSet<int> FossilSpecies =
    [
        138, 139, // Omanyte, Omastar
        140, 141, // Kabuto, Kabutops
        142,      // Aerodactyl
        345, 346, // Lileep, Cradily
        347, 348, // Anorith, Armaldo
        408, 409, // Cranidos, Rampardos
        410, 411, // Shieldon, Bastiodon
        564, 565, // Tirtouga, Carracosta
        566, 567, // Archen, Archeops
    ];

    /// <summary>All evolution families whose species fall within <paramref name="rosterCap"/> generations.
    /// A family truncated by the cap (e.g. Electabuzz without Electivire) is a complete family at that cap.</summary>
    public static IReadOnlyList<EvolutionFamily> Build(GameData data, int rosterCap)
    {
        // Node set = species with SpeciesGeneration.Of(id) <= rosterCap.
        // Edge set = data.Evolutions rules where BOTH endpoints are in the node set. An edge is an edge
        // regardless of trigger — friendship (LevelUp + MinHappiness), stone (UseItem), and trade
        // (LinkCable) evolutions all count. "Level-only completability" is a StarterSelector concern, not a
        // family-adjacency one, so nothing is filtered by trigger here.
        var nodes = data.Species.Values.Select(s => s.Id)
            .Where(id => SpeciesGeneration.Of(id) <= rosterCap)
            .ToHashSet();

        var outgoing = nodes.ToDictionary(id => id, _ => new List<int>());
        var incoming = nodes.ToDictionary(id => id, _ => new List<int>());
        var undirected = nodes.ToDictionary(id => id, _ => new HashSet<int>());
        foreach (var e in data.Evolutions)
        {
            if (!nodes.Contains(e.FromSpeciesId) || !nodes.Contains(e.ToSpeciesId)) continue;
            outgoing[e.FromSpeciesId].Add(e.ToSpeciesId);
            incoming[e.ToSpeciesId].Add(e.FromSpeciesId);
            undirected[e.FromSpeciesId].Add(e.ToSpeciesId);
            undirected[e.ToSpeciesId].Add(e.FromSpeciesId);
        }
        foreach (var children in outgoing.Values) children.Sort();

        var seen = new HashSet<int>();
        var families = new List<EvolutionFamily>();
        foreach (var start in nodes.OrderBy(id => id))
        {
            if (!seen.Add(start)) continue;

            // The whole connected component (undirected), so a family is never split by direction.
            var component = new HashSet<int> { start };
            var stack = new Stack<int>([start]);
            while (stack.Count > 0)
                foreach (var m in undirected[stack.Pop()])
                    if (component.Add(m)) { seen.Add(m); stack.Push(m); }

            families.Add(BuildFamily(OrderMembers(component, incoming, outgoing), outgoing, data));
        }
        return families;
    }

    /// <summary>Dex-seat order for a component: BFS from the root — the member with no in-cap incoming edge
    /// (lowest id if several, or the lowest id on the impossible cycle case) — visiting children in
    /// ascending ToSpeciesId. Any member unreachable that way (a shared-target merge, which this data has
    /// none of) is appended by ascending id so every member appears exactly once.</summary>
    private static List<int> OrderMembers(
        HashSet<int> component, Dictionary<int, List<int>> incoming, Dictionary<int, List<int>> outgoing)
    {
        var root = component.Where(id => incoming[id].Count == 0)
            .DefaultIfEmpty(component.Min()).Min();

        var ordered = new List<int>();
        var visited = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(root);
        visited.Add(root);
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            ordered.Add(n);
            foreach (var child in outgoing[n]) // already ascending
                if (component.Contains(child) && visited.Add(child)) queue.Enqueue(child);
        }

        foreach (var id in component.OrderBy(x => x))
            if (visited.Add(id)) ordered.Add(id);
        return ordered;
    }

    private static EvolutionFamily BuildFamily(
        List<int> members, Dictionary<int, List<int>> outgoing, GameData data)
    {
        // Fully-evolved-at-this-cap forms = members with no in-cap outgoing edge (always ≥1: a tree has a leaf).
        var finalBst = members.Where(id => outgoing[id].Count == 0).Max(id => StarterSelector.Bst(data.Species[id]));
        return new EvolutionFamily
        {
            Members = members,
            FinalBst = finalBst,
            Types = members.SelectMany(id => data.Species[id].Types).Distinct().ToList(),
            IsLegendary = members.Any(id => data.Species[id].IsLegendary || data.Species[id].IsMythical),
            IsFossil = members.Any(FossilSpecies.Contains),
        };
    }
}
