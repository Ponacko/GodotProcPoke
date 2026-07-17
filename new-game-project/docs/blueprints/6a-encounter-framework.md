# Blueprint — Ticket 6a: encounter framework (records, level curve, method assignment)

Spec + acceptance asserts: `../../tickets.md` §6a. This blueprint locks the **records** (consumed by 6b,
7a, 7b, 7c) and the **`LevelCurve`** helper (reused by 7a/7b/7c). 6a emits tables with correct methods and
per-area base levels but **empty slot lists** — slot filling and overlays are ticket 6b.

Depends on ticket 4a-1's `AvailabilityOrder`.

---

## 1. Records

```csharp
// FILE: domain/ProcPoke.Generation/Encounters/EncounterPlan.cs   (new file)
namespace ProcPoke.Generation.Encounters;

public enum EncounterMethod { Land, Surf, Fishing }

/// <summary>One slot in a Gen 5 style table. Percent is the fixed slot weight; the species and level band
/// are filled by ticket 6b (empty/zeroed until then).</summary>
public sealed record EncounterSlot(int Percent, int SpeciesId, int MinLevel, int MaxLevel);

/// <summary>One method's table for an area. HiddenAbilityChance is 0 for base tables; only a Special
/// Encounter Overlay (6b) sets it (GDD §6.3 / §3 — overlays are the sole hidden-ability source).</summary>
public sealed record EncounterTable(
    EncounterMethod Method,
    IReadOnlyList<EncounterSlot> Slots,
    double HiddenAbilityChance = 0);

/// <summary>Every encounter table for one area, plus its optional rare-spawn overlay (6b).</summary>
public sealed record AreaEncounters(int AreaId, IReadOnlyList<EncounterTable> Tables, EncounterTable? Overlay);

public sealed record EncounterPlan
{
    public required IReadOnlyDictionary<int, AreaEncounters> ByArea { get; init; }
    public AreaEncounters? Of(int areaId) => ByArea.TryGetValue(areaId, out var e) ? e : null;
}

/// <summary>Canonical Gen 5 slot layouts (GDD §6.3). Percentages sum to 100.</summary>
public static class SlotLayouts
{
    public static readonly IReadOnlyList<int> Land    = [20, 20, 10, 10, 10, 10, 5, 5, 4, 4, 1, 1];
    public static readonly IReadOnlyList<int> Surf    = [60, 30, 5, 4, 1];
    public static readonly IReadOnlyList<int> Fishing = [60, 30, 5, 4, 1]; // B2W2 Super Rod only; per-tier rods deferred
}
```

## 2. `LevelCurve`

```csharp
// FILE: domain/ProcPoke.Generation/Encounters/LevelCurve.cs   (new file)
using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Encounters;

/// <summary>
/// The §7.1 progression curve, expressed against progression fraction so any badge count works. Shared by
/// wild-encounter levels (§6.3), trainer levels (7a), and boss levels (7b) so they all ride one curve.
/// </summary>
public static class LevelCurve
{
    /// <summary>Gym leader ace level: ~14 at the first badge, ~50 at the last (GDD §7.1).
    /// <paramref name="i"/> is the 0-based gym index, <paramref name="gymCount"/> the total.</summary>
    public static int GymAce(int i, int gymCount)
        => gymCount <= 1 ? 50 : (int)Math.Round(14 + 36.0 * i / (gymCount - 1));

    /// <summary>Base wild level for an area = (next gym's ace − 5), with +2 for destination dungeons
    /// (DeepCave/Tower/VillainHideout). The ±2 per-slot jitter is applied in 6b, not here.</summary>
    /// <param name="gymPathIndices">The critical-path PathIndex of each gym city, ascending (the keys of
    /// RegionIdentity.GymTypes mapped through graph[id].PathIndex, sorted).</param>
    /// <param name="offSpineAnchors">areaId → anchor PathIndex (GatingGenerator.OffSpineAnchors).</param>
    public static int WildLevel(
        Area area, RegionGraph graph, Biome biome,
        IReadOnlyList<int> gymPathIndices, IReadOnlyDictionary<int, int> offSpineAnchors)
    {
        // >>> IMPLEMENT(1):
        //   - pos = area.OnCriticalPath ? area.PathIndex : offSpineAnchors[area.Id]
        //   - find the first gym PathIndex STRICTLY GREATER than pos; its index j into gymPathIndices gives
        //     the gym's ace = GymAce(j, gymPathIndices.Count). Base wild = ace - 5.
        //   - if no gym is after pos (area sits past the last gym), base wild = GymAce(last) - 5, but never
        //     below the last computed band — clamp to at least (GymAce(count-1) - 5).  [i.e. 45 at 8 badges]
        //   - if area.Archetype is DeepCave, Tower, or VillainHideout, add 2.
        //   - return the result (min 2).
        throw new NotImplementedException();
    }
}
```

## 3. The pass

```csharp
// FILE: domain/ProcPoke.Generation/Encounters/EncounterPass.cs   (new file)
using ProcPoke.Data;
using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Roster;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Encounters;

/// <summary>
/// Per-area encounter tables (GDD §6.3), Gen 5 slot model. Ticket 6a builds the table SHAPES — correct
/// method per area and correct base levels — with empty slots; ticket 6b fills the slots from the dex and
/// adds Special Encounter Overlays.
/// </summary>
public static class EncounterPass
{
    // Land tables on these archetypes (towns/League/hideouts get none):
    private static readonly HashSet<AreaArchetype> LandArchetypes =
    [
        AreaArchetype.Route, AreaArchetype.Forest, AreaArchetype.StandardCave,
        AreaArchetype.MountainPath, AreaArchetype.DeepCave, AreaArchetype.VictoryRoad, AreaArchetype.Tower,
    ];

    public static EncounterPlan Generate(
        RegionGraph graph, BiomeMap biomes, GatingPlan gating, DexPlan dex, RngStreams streams)
    {
        var rng = streams.Stream("encounters"); // claimed now; 6b does the actual drawing
        var anchors = GatingGenerator.OffSpineAnchors(graph);
        var gymPathIndices = /* >>> IMPLEMENT(2): RegionIdentity gym area ids → graph[id].PathIndex, sorted
                                ascending. NOTE: EncounterPass needs RegionIdentity — add it as a parameter
                                and pass region identity from RegionGenerator (it's built before this pass). */
            throw new NotImplementedException();

        var byArea = new Dictionary<int, AreaEncounters>();
        foreach (var area in graph.Areas)
        {
            // >>> IMPLEMENT(3): skip StartTown, Town, League, VillainHideout (no wild tables).
            //   - baseLevel = LevelCurve.WildLevel(area, graph, biomes.Of(area.Id), gymPathIndices, anchors)
            //   - tables = []
            //   - if LandArchetypes contains area.Archetype: add EncounterTable(Land, EMPTY slots list).
            //   - if biomes.Of(area.Id)==Biome.Water OR the area appears in gating.BiomeRequirements with a
            //     water terrain tag: add Surf and Fishing tables (EMPTY slots).
            //   - store the baseLevel somewhere 6b can read it — simplest: seed each empty slot's Min/Max to
            //     (baseLevel,baseLevel) as a placeholder, OR add an int BaseLevel field to AreaEncounters.
            //     PICK ONE and note it; 6b will overwrite. (Recommended: add `int BaseLevel` to AreaEncounters.)
            //   - byArea[area.Id] = new AreaEncounters(area.Id, tables, Overlay: null).
        }
        return new EncounterPlan { ByArea = byArea };
    }
}
```

> If you take the recommended route, add `int BaseLevel` to the `AreaEncounters` record in step 1 — that is
> the one shape change 6b depends on, so make it here, not later.

## 4. Wiring

```csharp
// WIRING — FILE: domain/ProcPoke.Generation/RegionGenerator.cs
// After the dex pass (4a-2) line, add:
//     var encounters = EncounterPass.Generate(graph, biomes, gating, dex, identity, streams);
// Add `EncounterPlan Encounters` to the GeneratedRegion record + constructor call.
```

```csharp
// WIRING — FILE: domain/ProcPoke.Generation/Debug/RegionGraphText.cs
// Add a nullable `EncounterPlan? encounters = null` parameter; when non-null, print each area's methods
// and base wild level. Pass it from the MapGen harness Render(...) call.
```

## Tests

`tests/ProcPoke.Generation.Tests/EncounterFrameworkTests.cs`:

- `LevelCurve` unit: `GymAce(0,8)==14`, `GymAce(7,8)==50`, `GymAce(0,1)==50`; the sequence over i=0..G-1 is
  non-decreasing for G in {4,8,12}.
- Fuzz the standard corpus: every Route/Forest/cave-family/Tower/VictoryRoad area has a Land table; every
  Water-biome or water-gated area also has Surf + Fishing; StartTown/Town/League/VillainHideout have no
  entry (or an empty tables list). `WildLevel` is non-decreasing along `AvailabilityOrder.Of(...)` at this
  stage (no jitter yet, so exact monotonicity holds except for the dungeon +2 bumps — assert
  non-decreasing after subtracting the +2 for dungeon archetypes, or assert within a +2 tolerance).
