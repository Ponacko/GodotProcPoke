# Blueprint — Ticket 4a-1: evolution-family pool & availability order

Spec + acceptance asserts: `../../tickets.md` §4a-1. These two helpers are **pure functions** (no RNG, no
pass, no `GeneratedRegion` change) and are the shared foundation for 4a-2 (dex), 4c (fossils/legendaries),
6a/6b (encounters) and 7a/b/c (trainers/bosses). Get the shapes exactly right — everything downstream is
written against them.

Reference for idioms: `Roster/StarterSelector.cs` already builds 3-stage lines and has the `Bst` helper and
the friendship-detection subtlety; `EvolutionFamilies` generalises that from strict 3-stage chains to
**connected components** of the evolution graph (so Eevee, Wurmple, and 1/2-stage lines are handled).

---

## 1. `EvolutionFamilies`

```csharp
// FILE: domain/ProcPoke.Generation/Roster/EvolutionFamilies.cs   (new file)
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
        // Edge set = data.Evolutions rules where BOTH FromSpeciesId and ToSpeciesId are in the node set.
        //   NOTE: an edge is an edge regardless of trigger — friendship (LevelUp + MinHappiness), stone
        //   (UseItem), and trade (LinkCable) evolutions ALL count here. "Level-only completability" is a
        //   STARTER concern (StarterSelector), NOT a family-adjacency concern. Do not filter by trigger.

        // >>> IMPLEMENT(1): build an undirected adjacency map over in-cap species from the in-cap edges,
        //     then find connected components (BFS/DFS + a visited set). Every in-cap species is in exactly
        //     one component (isolated species = a 1-member family).

        // >>> IMPLEMENT(2): for each component, produce an EvolutionFamily:
        //   - Members: order by BFS from the root. Root = the member with no in-cap INCOMING edge; if
        //     several (or a cycle — none exist in this data), the lowest id. When expanding a node, visit
        //     its outgoing targets (ToSpeciesId) in ascending order.
        //   - FinalBst: max Bst over members that have no in-cap OUTGOING edge. Bst = sum of the six
        //     BaseStats (copy the Bst helper from StarterSelector, or make it internal there and reuse).
        //   - Types: distinct union of every member's Types, any stable order.
        //   - IsLegendary: any member has IsLegendary || IsMythical.
        //   - IsFossil: any member id is in FossilSpecies.

        throw new NotImplementedException();
    }
}
```

## 2. `AvailabilityOrder`

Placed in `Topology/` per the ticket, but it takes the off-spine anchors **as a parameter** rather than
calling `GatingGenerator` directly — that keeps `Topology` from depending on `Gating` (the dependency runs
the other way). Callers pass `GatingGenerator.OffSpineAnchors(graph)`.

```csharp
// FILE: domain/ProcPoke.Generation/Topology/AvailabilityOrder.cs   (new file)
namespace ProcPoke.Generation.Topology;

/// <summary>
/// Canonical "first-availability" ordering of every area (GDD §5.2): the critical path in PathIndex order,
/// with each off-spine area inserted right after the critical-path area it hangs off. This is the order the
/// dex numbers by, encounters level by, and trainers scale by — one definition so every pass agrees.
/// </summary>
public static class AvailabilityOrder
{
    /// <param name="offSpineAnchors">areaId → the min PathIndex among its on-critical-path neighbours,
    /// from GatingGenerator.OffSpineAnchors(graph). Passed in to keep Topology free of a Gating reference.</param>
    /// <returns>Every area id exactly once.</returns>
    public static IReadOnlyList<int> Of(RegionGraph graph, IReadOnlyDictionary<int, int> offSpineAnchors)
    {
        // >>> IMPLEMENT(3):
        //   - Walk graph.CriticalPath (already sorted by PathIndex).
        //   - For each critical-path area at position p: append its id, then append every off-spine area
        //     whose offSpineAnchors[id] == p, ordered by ascending area id.
        //   - Result contains every area exactly once (each off-spine area has exactly one anchor).
        throw new NotImplementedException();
    }
}
```

## Tests

`tests/ProcPoke.Generation.Tests/EvolutionFamiliesTests.cs` — no fuzz needed for the family checks; they
run against the pinned `TestData.Data`:

- Eevee family at cap 5 = exactly `{133,134,135,136,196,197,470,471}` (Sylveon 700 is Gen 6, excluded); at
  cap 1 = `{133,134,135,136}`. (Asserts branching families and cap truncation.)
- A friendship evolution (e.g. Golbat→Crobat, 42→169, `MinHappiness` set) is in one family with its base —
  i.e. families ignore trigger kind.
- At cap 1 exactly 3 fossil families exist (Omanyte, Kabuto, Aerodactyl lines).
- Every in-cap species belongs to exactly one family; members unique; a hand-checked `FinalBst` (Dratini
  family 147→148→149 → 600).

`AvailabilityOrder` — fuzz the standard corpus: result is a permutation of all area ids; critical-path ids
appear in `PathIndex` order; each off-spine id appears after its anchor's id and before the next
critical-path id. Get anchors via `GatingGenerator.OffSpineAnchors(region.Graph)`.
