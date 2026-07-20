# Tickets: Phase 2 completion — generator go/no-go

Finish DEVELOPMENT-PLAN Phase 2 (the go/no-go milestone): the rest of 2b map carving, all of 2c population, and the sign-off packet. Source: `docs/DEVELOPMENT-PLAN.md` §Phase 2, the GDD sections cited per ticket, ADR-0001/0002/0004/0005. Topology, gating, biomes, first-cut carvers, and gates-on-tiles are already done and fuzz-tested.

**These tickets are sized for a mid-tier implementer.** Each one names the file(s) it lives in, sketches the algorithm, and states its acceptance as **checkable assertions or fuzz invariants** — not subjective judgment. The one genuinely subjective call ("do the maps read hand-crafted?") is deferred to the final sign-off ticket, where a human makes it. When a ticket says "deterministic," it means: same seed → identical output, and it draws only from a named RNG stream (`streams.Stream("<name>")`, ADR-0005) — never `System.Random`, never wall-clock.

**Model routing.** Every open ticket carries a `Model:` line:
- **Qwen-OK** — fully pinned (tables, formulas, ID lists, and assert definitions are in the ticket text); a local coder model of the Qwen2.5-coder class can implement it from this file plus the named source files alone.
- **Sonnet** — needs multi-file wiring, an API design call, or judgment the text can't fully pin; use a Sonnet-class model. Everything marked Qwen-OK is of course also fine for Sonnet.

**Blueprints.** Tickets whose output shape is a *shared contract* consumed by several later tickets carry a `Blueprint:` line pointing at a scaffold in `docs/blueprints/`. A blueprint is a near-complete C# skeleton — records, signatures, RNG stream names, and pass wiring already fixed — with `>>> IMPLEMENT` markers where the weak model fills bodies. Where a blueprint exists, hand the weak model the ticket **and** the blueprint; it must not change public signatures, record fields, or stream names (downstream tickets and tests are written against them). See `docs/blueprints/README.md`. Tickets without a `Blueprint:` line don't need one (the carvers, for instance, copy an existing carver in `Carving/`).

House rules that apply to every ticket:
- New generation passes hang off `RegionGenerator.Generate` and extend the `GeneratedRegion` record (`domain/ProcPoke.Generation/RegionGenerator.cs`). Follow the existing pass pattern: a `public static class XPass` with a single static `Generate(...)` taking prior-pass outputs + `RngStreams`, pulling exactly one named stream, returning one immutable record. There is no pass registry — add one line to `Generate` and one field to `GeneratedRegion`.
- Baked game data (species, moves, evolutions, types) loads through `ProcPoke.Data` — `GameDataLoader.Load(dataDir)`. A species' BST is the **sum of its six `BaseStats`** (no precomputed BST field). Legendaries carry `IsLegendary`; mythicals carry `IsMythical`. Friendship evolutions are `Trigger = LevelUp` with `MinHappiness` set; trade evolutions were rewritten to `Trigger = LinkCable` at bake.
- Add a fuzz test alongside the existing ones in `tests/ProcPoke.Generation.Tests/`; the standard corpus is `seed = 1..2000` × badge counts `{4, 8, 12}` (carving-heavy suites may use a smaller corpus, matching `CarvingTests`). Use the `TestData.Data` shared loader and end every corpus loop with a `checkedCount > 0`-style assert so a silently-empty corpus fails.
- New debug output goes through `RegionGraphText` (add a nullable parameter + conditional block) or `MapImage`/`AsciiRenderer` (maps) so it shows up in the MapGen harness.
- The 7 area biomes are `Biome { Grassland, Forest, Cave, Mountain, Water, Desert, Urban }`; the biome→type table is `BiomeTypeAffinity.Table` (`Identity/BiomeTypeAffinity.cs`). `Urban` has an empty affinity — fall through to neighbouring areas' biomes, the way `GymTypingPass` does.

Work the **frontier**: any ticket whose blockers are all done.

---

## 1. Gates carved onto chokepoints — ✅ DONE

Delivered in commit "Phase 2: carve gates onto verified chokepoints". `GateCarver` necks each spine exit; portable obstacles become a walled gap, water HMs become a Surf river; two-mode chokepoint fuzz invariant green; gates render in the maps.

---

## 2a. Edge-aligned seamless connections — ✅ DONE

Delivered in commit "implement phase 2". `OpeningAligner` assigns a shared edge/offset/width per Seamless
connection before carving; `RouteCarver`/`TownCarver`/`GateCarver` take the planned openings instead of
hard-coding `midY`; `OpeningAlignmentTests` (21 cases) assert offset/width agreement, warp exemption, and
that carved tiles actually land on the shared coordinate. Full suite green (64/64).

## 2b. Spatially stitched region overview — ✅ DONE

Delivered alongside 2a. `OverviewLayout.Plan` (domain, fuzz-tested in `OverviewLayoutTests`) assigns each
area a `(Col, Row)` cell: critical path left→right at `(PathIndex, 0)`, off-spine areas hung one row
above/below/further-out their `GatingGenerator.OffSpineAnchors` column. `MapImage.SaveOverview` blits every
area's carved footprint into a canvas sized per column/row band and draws a connector between each
connection's pair of openings (aligned opening tile for Seamless, area center for Warp). Verified visually
via `dotnet run --project tools/ProcPoke.MapGen -- 42 --badges 8 --png` — the overview reads as one
horizontally-flowing map with branches stacked above/below the spine, not a stack.

## 2c. Carved maps join the generator pipeline — ✅ DONE

Delivered in commit "Implement ticket 2c: carved maps join the generator pipeline". `RegionGenerator.Generate`
now carves every area right after `OpeningAligner.Plan` —
`graph.Areas.ToDictionary(a => a.Id, a => AreaCarver.Carve(a, biomes.Of(a.Id), streams.Stream("carve", a.Id), openings, AreaCarver.GateOnExitOf(a, gating)))`
— and exposes it as `IReadOnlyDictionary<int, CarvedArea> Carved` on `GeneratedRegion`. MapGen's `--carve`/`--png`
paths and `CarvingTests.CarveAll`/`CarveRoutes` read `region.Carved`; the duplicate carve loops are gone.
`CarvingIsDeterministic` now re-carves every area independently from `new RngStreams(seed).Stream("carve", id)`
and asserts the ASCII render matches `region.Carved`. Verified byte-identical: seed 42 `--png` output is
byte-for-byte the same before and after the change (36 area images + overview). Full suite green (128 tests).

## 3a. Location name generator — ✅ DONE

Delivered in commit "Implement tickets 3a/3b/4b: naming, gym/E4/Champion typing, starter triangle".
`NameParts` (`domain/ProcPoke.Generation/Identity/`) holds 4 motifs (Colors, Flora, Minerals,
WeatherLight) with ~6 roots + ~6 blends each; `NamingPass` draws one motif per region from
`streams.Stream("names")`, numbers Route areas `Route 1..N` in critical-path order, and blends
city/dungeon names, redrawing on a `name_blocklist.json` hit or in-region collision. `NamingTests`
(6 cases) fuzzes the standard corpus; `RegionGraphText` prints the motif and every area's name.

## 3b. Gym / Elite Four / Champion type assignment — ✅ DONE

Delivered alongside 3a. `BiomeTypeAffinity` encodes the §6.2 table over the 7-value `Biome` enum;
`GymTypingPass` ranks each gym city's own+neighbour biome affinity (ties shuffled off
`streams.Stream("gym-types")`), picks the highest-ranked untaken type per gym in critical-path
order, falling back to any untaken type once a city's affinity pool is exhausted, then draws 4
more distinct types for the Elite Four. Champion is unconditionally typeless. `GymTypingTests` (9
cases) fuzzes the standard corpus, including a reconstructed-fallback check for the exhaustion case.

## 3c. Villain team, rival identity & Champion-identity roll — ✅ DONE

Delivered in commit "Implement ticket 3c: villain teams, rival identity & champion roll". `IdentityPass.Generate`
(`Identity/`) layers villain teams, the rival archetype, and the Champion-identity roll onto the
`GymTypingPass` identity via `identity with { … }`, drawing from the `"identity"` stream in the pinned
order: team count (30% two), per-team name (`Team <root>`, root rejection-sampled off the canon list and the
other team's root), per-team 1–2-type motif (excluding gym types + the other team's motif, falling back to
just the other team's motif), the 25% Champion-is-rival roll, then the archetype (Underdog-weighted when the
rival is Champion, else uniform). `RegionIdentity` gains `VillainTeams`/`Rival`/`ChampionIsRival`; the new
`VillainTeam` record and `RivalArchetype` enum live alongside it. A guard turns the (astronomically unlikely)
name-redraw exhaustion into a loud failure rather than a silent duplicate. `IdentityTests` (5 cases) fuzz the
corpus for well-formed distinct non-canon teams with gym-disjoint motifs, per-seed archetype stability, the
ChampionIsRival rate ∈ [0.20,0.30] and the conditional-Underdog skew (over 1500 seeds), and determinism.
`RegionGraphText` prints teams, rival, and champion. Full suite green (156 tests).

<details><summary>Original ticket</summary>

**Model:** Qwen-OK (everything below is pinned; the only wiring is one new pass line in `RegionGenerator` and a `with`-extension of `RegionIdentity`).
**Blueprint:** `docs/blueprints/3c-identity-pass.md` (also the canonical "how to add a pass" example).

**What to build:** Extend `RegionIdentity` (produced by `GymTypingPass`) with villain teams, the rival archetype, and the Champion-identity roll. **The Champion roll lives here, not in 7c** — the roll must weight the archetype pick (rival-Champion seeds skew Underdog), so both draws share one pass. Grunt rosters and rival *teams* remain 7a/7c.

**Approach:** New `IdentityPass.Generate(RegionIdentity identity, RegionNames names, RngStreams streams) → RegionIdentity` in `Identity/`, returning `identity with { … }`. Insert into `RegionGenerator.Generate` right after `GymTypingPass`. New fields on `RegionIdentity`:
- `IReadOnlyList<VillainTeam> VillainTeams` — new record `VillainTeam(string Name, IReadOnlyList<PokeType> Motif)`
- `RivalArchetype Rival` — new enum `RivalArchetype { Cocky, Friendly, Brooding, Underdog }` (GDD §7.2)
- `bool ChampionIsRival`

Draw from `streams.Stream("identity")` **in exactly this order** (determinism depends on it):
1. **Team count:** `rng.Chance(0.30) ? 2 : 1`.
2. **Team names:** for each team, name = `$"Team {root}"`, root drawn via `rng.Pick` from the region motif's `NameParts.Pools[motif].Roots`; redraw if the root (case-insensitive) is in the hardcoded canon list below or already used by the other team. **The canon-team-name list does not exist in `data/` — hardcode it** as `static readonly string[] CanonTeamNames = ["Rocket", "Aqua", "Magma", "Galactic", "Plasma", "Flare", "Skull", "Yell", "Star", "Snagem", "Cipher"]`.
3. **Type motifs:** per team, draw 1 type (or 2 if `rng.Chance(0.40)`) via `rng.Pick` from the 17 `PokeType`s **excluding** the region's gym types and the other team's motif (GDD §7.1: villain motifs chosen away from gym collisions); if that leaves nothing, fall back to excluding only the other team's motif.
4. **Champion roll:** `ChampionIsRival = rng.Chance(0.25)`.
5. **Archetype:** if `ChampionIsRival`, pick with weights Underdog 0.55 / Cocky 0.15 / Friendly 0.15 / Brooding 0.15 (cumulative `rng.NextDouble()`); else uniform `rng.Pick` over the four.

**Blocked by:** None (3a/3b are done).

- [ ] 1–2 villain teams, each named `Team <Root>` with root ∉ canon list (case-insensitive), teams mutually distinct; each with a 1–2-type motif disjoint from gym types when possible; deterministic
- [ ] Exactly one rival archetype per seed, stable across the run
- [ ] Fuzz invariant over the corpus: `ChampionIsRival` rate ∈ [0.20, 0.30]; P(Underdog | ChampionIsRival) > P(Underdog | !ChampionIsRival)
- [ ] Printed by `RegionGraphText` (teams + motifs, archetype, "Champion: rival" / "Champion: generated NPC")

</details>

## 4b. Starter triangle selection — ✅ DONE

Delivered alongside 3a/3b. `StarterSelector` (`domain/ProcPoke.Generation/Roster/`) rebuilds 3-stage
lines from `evolutions.json`, filters to level-only (excluding friendship evolutions, which stay
`Trigger=LevelUp` with `MinHappiness` set), final BST ≤ 540, and within `RosterCap`
(`SpeciesGeneration.Of`, Gen 1–5 breakpoints), then brute-forces the corner-triple with the
smallest final-BST spread (≤ 40) per available template, drawn from `streams.Stream("starters")`.
`StarterSelectorTests` (11 cases) verifies against ground truth from the pinned data (Elemental
clears at Roster Cap 3+, Mind & Body only at Cap 5, Classic at every cap) and independently
re-derives each chosen line from raw evolution rules rather than trusting the selector's internals.

## 4a-1. Evolution-family pool & availability order (split from 4a) — ✅ DONE

Delivered in commit "Implement ticket 4a-1: evolution families & availability order". `EvolutionFamilies.Build`
(`Roster/`) builds connected components of the in-cap evolution graph — every trigger (friendship, stone,
trade) counts as an edge — ordering each family's members by BFS from the no-incoming root (children
ascending by `ToSpeciesId`), with `FinalBst`/`Types`/`IsLegendary`/`IsFossil` computed per the blueprint
(fossil pool hardcoded, BST via a now-`internal` `StarterSelector.Bst`). `AvailabilityOrder.Of` (`Topology/`)
takes the off-spine anchors as a parameter (keeping `Topology` free of a `Gating` reference) and interleaves
each off-spine area right after its anchor. `EvolutionFamiliesTests` (11 cases) verifies against pinned
ground truth (Eevee 8/4 members, friendship edge Golbat→Crobat, 3 fossil families at cap 1, Dragonite
family BST 600, cap-3 Electabuzz truncation, exact partition of every cap) and fuzzes `AvailabilityOrder`
over the standard corpus (permutation, critical-path order, each off-spine area between its anchor and the
next path area). Pure functions, no RNG. Full suite green (139 tests).

<details><summary>Original ticket</summary>

**Model:** Qwen-OK (pure functions over baked data + unit tests against pinned ground truth; no generator wiring).
**Blueprint:** `docs/blueprints/4a1-evolution-families.md` (shared contract for 4a-2, 4c, 6a, 7a/b/c).

**What to build:** Two reusable helpers that 4a-2, 4c, 6 and 7 all consume. No pass, no `GeneratedRegion` change yet.

**(1) `EvolutionFamilies` in `Roster/`:** `Build(GameData data, int rosterCap) → IReadOnlyList<EvolutionFamily>`. A *family* is a connected component of the evolution graph — this generalizes `StarterSelector`'s 3-stage lines to branching families (Eevee, Wurmple) and 1/2-stage lines, which is what "evolution lines stay together in consecutive dex slots" (GDD §5.1) actually requires.
- Nodes = species with `SpeciesGeneration.Of(id) <= rosterCap`; edges = `data.Evolutions` rules with **both** endpoints in cap. A family truncated by the cap (e.g. Electabuzz without Electivire at cap 3) is a complete, valid family at that cap.
- `EvolutionFamily` record: `IReadOnlyList<int> Members` (ordered: BFS from the root — the member with no incoming in-cap edge; if several roots, lowest id — children visited in ascending `ToSpeciesId`), `int FinalBst` (max BST among members with no outgoing in-cap edge), `IReadOnlyList<PokeType> Types` (union over members), `bool IsLegendary` (any member `IsLegendary || IsMythical`), `bool IsFossil` (any member in the hardcoded list below).
- **Fossil species are not flagged in data — hardcode the pool** (GDD §5.4 lines, Gen 1–5): Omanyte 138, Omastar 139, Kabuto 140, Kabutops 141, Aerodactyl 142, Lileep 345, Cradily 346, Anorith 347, Armaldo 348, Cranidos 408, Rampardos 409, Shieldon 410, Bastiodon 411, Tirtouga 564, Carracosta 565, Archen 566, Archeops 567.

**(2) `AvailabilityOrder` in `Topology/`:** `Of(RegionGraph graph) → IReadOnlyList<int>` (area ids). Critical path ascending `PathIndex`; immediately after each critical-path area, the off-spine areas anchored to it (`GatingGenerator.OffSpineAnchors` value == that `PathIndex`), ordered by area id. Every area appears exactly once. This is GDD §5.2's "first-availability order" made canonical — 4a-2 numbers by it, 6 levels by it, tests re-derive against it.

**Blocked by:** None.

- [ ] Unit tests against pinned data: Eevee family at cap 5 has 8 members (133, 134, 135, 136, 196, 197, 470, 471) and at cap 1 has 4; a friendship evolution (`MinHappiness` set) is still an edge (families are about dex adjacency, not level-only completability); at cap 1 exactly 3 fossil families exist
- [ ] Every species in cap belongs to exactly one family; members are consecutive-unique; `FinalBst` matches a hand-computed example (e.g. Dragonite family → 600)
- [ ] `AvailabilityOrder` fuzz: permutation of all area ids; critical-path ids appear in `PathIndex` order; each off-spine id appears after its anchor and before the next critical-path id
- [ ] Deterministic (pure functions of data/graph — no RNG at all)

</details>

## 4a-2. Regional dex selection & numbering (split from 4a) — ✅ DONE

Delivered in commit "Implement ticket 4a-2: regional dex selection & numbering". `DexSelector.Generate`
(`Roster/`) builds the dex from `EvolutionFamilies` (minus legendary and starter families), walks
`AvailabilityOrder`'s wild areas (towns/League/hideouts excluded) filling fair-share quotas toward a rising
BST target with a −200 type-coverage bonus, and numbers #1–9 starter lines then whole families in
availability order. `DexPlan` (Entries/FossilAreaId/FossilFamilySpecies/SpeciesByArea) is added to
`GeneratedRegion`. The two reserved fossil families are **pre-placed** at the fossil area so the budget can
never be exhausted before they are seated. The budget is clamped to the candidate pool, so an infeasible
knob combo (e.g. DexSize 150 at RosterCap 1) yields a smaller whole-family dex instead of crashing — a no-op
at every feasible setting. `DexSelectorTests` (7 cases) fuzz the corpus for exactly-DexSize numbering, no
broken families (re-derived), first-availability order, the two fossil families, full 17-type coverage at
defaults, budget-exactness at DexSize 60, the cap-1 clamp, and determinism. `RegionGraphText` prints the
dex (first/last 10 + per-area counts). Full suite green (146 tests).

<details><summary>Original ticket</summary>

**Model:** Sonnet recommended (the algorithm below is fully pinned, but it's a long single pass with a budget-exactness proof obligation; a Qwen-class model may be attempted if 4a-1 landed cleanly).

**What to build:** `DexSelector.Generate(RegionGraph graph, BiomeMap biomes, StarterPlan starters, GameData data, GenerationSettings settings, RngStreams streams) → DexPlan` in `Roster/`; extend `GeneratedRegion`. **Correction to the old ticket text:** per GDD §5.1/§5.2 the dex holds **exactly `DexSize` species total, and #1–#9 are the three starter lines** (starters count *inside* the 150) — the 4 legendaries are appended later by 4c as #`DexSize`+1..+4.

**Pinned algorithm** (draw only from `streams.Stream("dex")`):
1. `families = EvolutionFamilies.Build(data, settings.RosterCap)` minus legendary families minus the 3 starter families (match by membership of the starters' species ids).
2. `wildAreas` = `AvailabilityOrder.Of(graph)` filtered to archetypes **not in** { StartTown, Town, League, VillainHideout } (towns/League have no wilds in our model — deliberate deviation from §5.2's "City 1's population"; note it in the pass docstring). Let `J = wildAreas.Count`.
3. `fossilAreaId` = first area in availability order with archetype `DeepCave`, else first `StandardCave`, else first area with biome `Cave` or `Mountain`, else the first dungeon-archetype area (assert one exists). Store on the plan — 4c places the fossils there.
4. Reserve exactly **2 distinct fossil families** via `rng.Pick` from the fossil families in cap (≥3 exist at every cap); they will be assigned to `fossilAreaId`'s slot in step 6 and count toward the budget.
5. Budget `B = settings.DexSize − 9`. Area quotas: `quota_j = floor(B·(j+1)/J) − floor(B·j/J)` (fair share, sums to B exactly).
6. Walk `wildAreas` in order, j = 0..J−1, with target BST `T_j = 250 + (J==1 ? 1 : j/(J−1.0)) · 350`. While the area's assigned species count < `quota_j` and global assigned < B: candidates = unassigned families with `Members.Count ≤ B − assigned`; if the area is `fossilAreaId` and reserved fossil families remain, take those first; else score each candidate `|FinalBst − T_j| − 200·(family has ≥1 type whose current dex count is 0)` and take the minimum, breaking ties with `rng.Pick` among the tied. Update per-type running counts (both types of every member). Overshooting `quota_j` by a family's tail is fine; **never** overshoot B (the size-≤-remaining filter guarantees exactness — Gen 1–5 has ~90 single-member families in cap, so the pool can always finish; assert non-empty candidates).
7. Number: #1–9 = starter corners in `StarterPlan.Corners` order (base, mid, final each); #10.. = families in assignment order (area order, then per-area assignment order), members in family order.

`DexPlan` record: `IReadOnlyList<DexEntry>` (`Number, SpeciesId`), `int FossilAreaId`, `IReadOnlyList<int> FossilFamilySpecies`, `IReadOnlyDictionary<int, IReadOnlyList<int>> SpeciesByArea` (areaId → species first available there; starters map to the start town; needed by 6 and 7a).

**Blocked by:** 4a-1, 4b (done).

- [ ] Fuzz invariant: dex holds exactly `DexSize` entries numbered 1..`DexSize` with #1–9 = the starter lines; **no broken families** — every included species' full in-cap family is included and consecutive (re-derive families independently in the test, don't trust the selector)
- [ ] Fuzz invariant: numbering equals first-availability order — recompute `AvailabilityOrder` in the test and assert each area's species block starts at a number ≥ every earlier area's
- [ ] Exactly 2 fossil families included, assigned to `FossilAreaId`
- [ ] Every one of the 17 types has ≥1 representative at default settings (`DexSize` 150, cap 5, badges 8) — do **not** assert this at cap 1 (Gen 1 ids have no Dark-type)
- [ ] Deterministic; printed by `RegionGraphText` (number, name, types, final BST — first/last 10 plus per-area counts is enough)

</details>

## 4c. Fossils & legendaries — ✅ DONE

Delivered in commit "Implement ticket 4c: fossils & legendaries". `SpecialSpeciesPass.Generate` (`Roster/`)
emits `SpecialSpecies` (added to `GeneratedRegion`): two `FossilPlacement`s (each fossil family's root at
`dex.FossilAreaId`) and four `LegendaryPlacement`s. Legendaries are drawn from the non-mythical in-cap pool
(`rng.Shuffle` of the id-sorted pool, take 4), sorted weakest-BST-first, and placed one-per-dungeon across
the off-spine Tower/DeepCave areas (excluding the fossil area) in availability order — round-robin when a
region has fewer than four, which is the common case at 4/8 badges once the fossil DeepCave is spent. Levels
50/55/65/70 and dex numbers DexSize+1..+4 by placement index. The pass never receives or touches gating.
`SpecialSpeciesTests` (5 cases) fuzz the corpus for two fossils at the fossil area (roots re-derived), four
distinct in-cap non-mythical legendaries, weakest-first ordering, one-per-dungeon when ≥4 eligible (seen at
12 badges), the cap-1 legendary set {144,145,146,150}, unchanged solvability, and determinism.
`RegionGraphText` prints fossils and legendaries. Full suite green (151 tests).

<details><summary>Original ticket</summary>

**Model:** Qwen-OK (after 4a-2 the dungeon and fossil choices are already settled; this is list-filtering plus a pinned placement rule).

**What to build:** `SpecialSpeciesPass.Generate(RegionGraph graph, DexPlan dex, GameData data, GenerationSettings settings, RngStreams streams) → SpecialSpecies` in `Roster/`; extend `GeneratedRegion`. Fossil *species* are already in the dex at `dex.FossilAreaId` (4a-2); this pass emits the two fossil pickups and the four legendary static encounters. Draw from `streams.Stream("special-species")`.

**Pinned rules:**
- **Fossils:** exactly the 2 families in `dex.FossilFamilySpecies`; emit `FossilPlacement(SpeciesId /* family root */, AreaId = dex.FossilAreaId)` for each.
- **Legendaries:** pool = species with `IsLegendary && !IsMythical` and `SpeciesGeneration.Of(id) <= RosterCap` (mythicals are event-only — excluded). `rng.Shuffle` the pool sorted by id, take 4 distinct.
- **Placement:** eligible dungeons = off-spine areas with archetype `Tower` or `DeepCave`, excluding `dex.FossilAreaId`, in availability order. (There is **no `Ruins` archetype** — the old ticket text was wrong.) If ≥4 eligible: one legendary per dungeon, first 4, weakest-BST legendary earliest. If fewer: wrap around round-robin so every legendary still gets a dungeon (multiple per dungeon is acceptable; assert every legendary is placed).
- **Levels** by placement order index: 50, 55, 65, 70.
- **Dex numbers:** append as #`DexSize`+1..+4 in placement order (the 154-entry dex of GDD §5.1).
- Legendaries are optional (§5.5) — **do not touch gating**.

**Blocked by:** 4a-2.

- [ ] Exactly 2 fossils placed together in `dex.FossilAreaId`; exactly 4 legendaries, each with a placement dungeon, one-per-dungeon whenever ≥4 eligible dungeons exist
- [ ] All within `RosterCap`; legendaries carry `IsLegendary` and not `IsMythical`; numbered `DexSize`+1..+4
- [ ] Gating/solvability unchanged — assert the `GatingPlan` is the same reference / serializes identically with the pass present
- [ ] Deterministic; printed by `RegionGraphText`

</details>

## 5a. Forest carver: de-stripe

**Model:** Qwen-OK.

**What to build:** `ForestCarver` currently lays full-height vertical tree columns with one gap each — literal stripes. Replace with organic tree **clumps**. Additionally — the old ticket implied but never said it — `ForestCarver` today ignores `OpeningPlan` and hardcodes `(0, midY)`/`(w−1, midY)`: **change its signature to `Carve(Area, Biome, Pcg32, IReadOnlyList<AreaOpening> planned)`** mirroring `TownCarver`, resolve left/right rows via `planned.OffsetOr(EdgeSide.Left/Right, midY)`, honor any planned Top/Bottom branch openings, and update the `AreaCarver` switch to pass `openings.OpeningsOf(area.Id)`. (Warp-connected forests have no planned openings — the midY fallback covers them.)

**Pinned algorithm:** ground fill + tree border as today; protect the 3 spine rows around the left/right opening rows plus a connecting column per Top/Bottom opening. Then `clumpCount = 6 + rng.NextInt(5)` clumps: each picks a random interior center and stamps a filled ellipse (half-width `1 + rng.NextInt(2)`, half-height `1 + rng.NextInt(2)`) of `Tree`, skipping protected tiles and tiles adjacent to an opening. Keep the 4 tall-grass patches and 1 `ItemBall` as today.

**Blocked by:** None (2a is done). Land before or with 2c — whichever order, keep `AreaCarver` compiling.

- [ ] Assert: no interior column (x = 1..w−2) is entirely `Tree`
- [ ] Assert: `Tree` tiles excluding the border form ≥ 3 discrete clumps (4-neighbour connected components)
- [ ] Openings match the plan (`OpeningAlignmentTests` cover forests once the signature changes); spine open; `CarvingTests` spine + mutual-reachability invariants stay green
- [ ] Deterministic

## 5b. Cave carver: rooms, winding passages, boulder fields

**Model:** Qwen-OK with care (the algorithm and every assert below are pinned; the only subtlety is re-checking reachability after each boulder).

**What to build:** `CaveCarver` currently carves thin straight corridors to one chamber. Rework it — and, like 5a, **make it take `planned` openings** (it hardcodes them today) and update the `AreaCarver` switch.

**Pinned algorithm:** wall fill. Rooms: `2 + rng.NextInt(2)` rectangles, w ∈ [5..8], h ∈ [4..6], rejection-sampled ≤100 tries each, ≥2 tiles from the border, ≥1 tile of wall between rooms; carve `Ground`. Order rooms by center x; connect consecutive centers with a 3-segment corridor (horizontal to a random `midX` between them, vertical, horizontal); if the two centers share a row, offset the middle segment ±2 to force a bend. Transit mode: L-corridor from the left opening to the first room, and from the last room to the right opening; honor Top/Bottom planned openings with a connecting corridor to the nearest room. Non-transit: single entrance (planned, else bottom-center), corridor to the nearest room. Item: `ItemBall` in the room whose center is BFS-farthest from the entrance opening, at a cell not on any corridor segment line. Boulders: 8 attempts — pick a random `Ground` tile not adjacent to an opening, set `Boulder`, BFS-verify all opening-pairs (and item) still mutually reachable, revert if not.

**Blocked by:** None (2a done). Coordinate with 5a on the `AreaCarver` switch.

- [ ] Assert: ≥2 rooms — count connected components over cells that belong to at least one fully-open 4×3 rectangle
- [ ] Assert: the BFS shortest path between the two transit openings contains both a horizontal and a vertical step (bend); ≥3 `Boulder` tiles exist; mutual-reachability invariant still green
- [ ] Item cell is inside a room (member of a fully-open 4×3 rectangle) and not on a corridor centerline
- [ ] Deterministic

## 5c-1. League carver (split from 5c)

**Model:** Qwen-OK.

**What to build:** The League (last critical-path area) currently carves as a town. New `LeagueCarver` in `Carving/`; dispatch `AreaArchetype.League` to it in the `AreaCarver` switch (League leaves `TownCarver`'s case).

**Pinned layout:** Large grid (`CarveKit.Dimensions`), wall fill. Entrance = planned Left opening (fallback midY). Four **chambers** (E4 rooms, ~6 wide, full usable height minus 2) laid left→right with 1-tile wall between, each connected to the next by a 1-wide door on the spine row; then a **throne room** (Champion) at the right, 8 wide. One `TrainerPost` centered in each chamber and in the throne room (5 total — 7b keys boss teams to these). Honor any other planned openings.

**Blocked by:** None. Coordinate on the `AreaCarver` switch with 5a/5b/5c-2/5c-3.

- [ ] `AreaCarver` dispatches `League` to `LeagueCarver`
- [ ] Signature asserts: exactly 5 `TrainerPost` tiles; the BFS path from entrance to the throne-room post passes ≥4 doorway tiles (walkable tiles whose north and south neighbours are both `Wall`)
- [ ] All carving invariants (spine walkability, mutual reachability, chokepoint if gated) stay green
- [ ] Deterministic; ASCII render visibly differs from a town

## 5c-2. Villain-hideout carver (split from 5c)

**Model:** Qwen-OK.

**What to build:** New `HideoutCarver` in `Carving/`; dispatch `AreaArchetype.VillainHideout` to it (leaves `CaveCarver`'s non-transit case).

**Pinned layout:** Medium grid, wall fill. 2×2 grid of rooms (~6×4 each) with 1-wide corridors between horizontal and vertical neighbours. Single entrance (planned, else bottom-center) with a corridor into the bottom-left room. Boss room = top-right: one `TrainerPost` + the `ItemBall`. Grunt posts: one `TrainerPost` in each of ≥2 other rooms.

**Blocked by:** None. Coordinate on the `AreaCarver` switch.

- [ ] `AreaCarver` dispatches `VillainHideout` to `HideoutCarver`
- [ ] Signature asserts: ≥4 rooms (4×3-rectangle component count, as in 5b); ≥3 `TrainerPost` tiles; `ItemBall` in the BFS-farthest room from the entrance
- [ ] Single-entrance reachability invariant green (item + all posts reachable from the entrance)
- [ ] Deterministic; ASCII render visibly differs from a cave

## 5c-3. Tower carver (split from 5c)

**Model:** Qwen-OK (pure geometry — the stair-*warp* version was descoped, see note).

**What to build:** New `TowerCarver` in `Carving/`; dispatch `AreaArchetype.Tower` to it. **Design note:** the original "stair warps" idea needs intra-area warp *pairs*, which `CarvedArea` cannot express and every BFS reachability helper would break on. Descoped: floors are separated by full-width wall rows with a 1-wide **stair gap**, alternating ends, which yields the zigzag-climb read. (True stair warps = a Phase 3+ follow-up: add `WarpPairs` to `CarvedArea` and teach the test BFS to traverse them.)

**Pinned layout:** Medium grid, wall fill, then carve all-Ground interior. `floors = 3 + rng.NextInt(2)` horizontal bands separated by 1-thick full-width `Wall` rows (band height ≥ 3). Each separating wall row gets exactly one 1-wide gap: alternating right end (x = w−3) and left end (x = 2), bottom-up. Single entrance (planned, else bottom-center) into the bottom band. Top band: `ItemBall` + one `TrainerPost`.

**Blocked by:** None. Coordinate on the `AreaCarver` switch.

- [ ] `AreaCarver` dispatches `Tower` to `TowerCarver`
- [ ] Signature asserts: ≥2 interior full-width wall rows each with exactly one walkable gap; consecutive gaps on opposite halves of the width; `ItemBall` in the top band
- [ ] Single-entrance reachability invariant green (item reachable via the zigzag)
- [ ] Deterministic; ASCII render visibly differs from cave, hideout, and League

## 5d-1. Town layout variation (split from 5d)

**Model:** Qwen-OK.

**What to build:** `TownCarver` stamps fixed bands `TopBand = [4,4,3]`, `BottomBand = [3,3,3]` every time. Vary per seed from the carver's `Pcg32`: per band, building count 2–4, widths 3–5, heights 3–4, x-positions left-to-right with ≥1-tile gaps (rejection-sample within the row's usable width). Keep the semantics: first top building is the gym (make it the widest in its band), keep one south-side `Warp` door per building, keep planned openings and the clear spine row exactly as today.

**Blocked by:** None (2a done).

- [ ] Assert across seeds 1..50 (badges 8): ≥2 distinct building-rectangle sets occur for the first town (layouts genuinely vary)
- [ ] Every building has a door `Warp` on its south wall reachable from the spine; carving invariants stay green
- [ ] Deterministic per seed

## 5d-2. Decoration rules: directional ledges, item nooks, trainer sightlines (split from 5d)

**Model:** Sonnet (touches the tile-model contract plus two carvers; the semantics call is the risky part, pinned below).

**What to build:** The §-carver decoration rules on routes (and forests where noted). **Model decision the old ticket hid:** `LogicalTile.Ledge` has no direction. Pin the convention instead of extending the enum: **a `Ledge` tile is one-way passable moving south** (north→south hop; document this in the `LogicalTile` docstring — Phase 3 movement will enforce it; `IsWalkable()` stays as-is for reachability purposes).

**Pinned rules for `RouteCarver` (+ `ForestCarver` for nooks/posts):**
- **Ledges:** 1–2 horizontal runs of length 4–8, placed 2–4 rows **south of the spine row** (hopping down moves you off-spine, and walking back along the south side leads toward the entrance — the mainline shortcut asymmetry). Every ledge tile's north and south neighbours must be walkable.
- **Item nooks:** every `ItemBall` sits off the spine row and adjacent to ≥2 non-walkable tiles (a pocket, not open floor).
- **Trainer posts:** 1–2 per route, within 2 rows of the spine, with an unobstructed straight-line row or column (walkable the whole way) to at least one spine tile.
- Also fix the known cosmetic issue flagged in `TownCarver`'s docstring (gate-approach straightening punching a floor gap through a building wall): after `GateCarver.Apply`, re-close any building-wall tile the straightening opened unless it's the doorway.

**Blocked by:** 5d-1 (same file), 5a (forest signature).

- [ ] Asserts, per fuzz corpus: every `Ledge` tile has walkable north+south neighbours and sits south of the spine row; no `ItemBall` on the spine row and each has ≥2 non-walkable neighbours; every route `TrainerPost` has a clear straight sightline to a spine tile
- [ ] `LogicalTile.Ledge` docstring states the one-way-south contract
- [ ] Carving invariants stay green; deterministic

## 6a. Encounter framework: level curve, table shapes, method assignment (split from 6)

**Model:** Qwen-OK.
**Blueprint:** `docs/blueprints/6a-encounter-framework.md` (records + `LevelCurve` consumed by 6b, 7a, 7b, 7c).

**What to build:** The skeleton of GDD §6.3 in `domain/ProcPoke.Generation/Encounters/`: records, the level curve, and which areas get which tables. Slot *filling* is 6b.

**Records:** `EncounterMethod { Land, Surf, Fishing }`; `EncounterSlot(int Percent, int SpeciesId, int MinLevel, int MaxLevel)`; `EncounterTable(EncounterMethod Method, IReadOnlyList<EncounterSlot> Slots, double HiddenAbilityChance = 0)`; `AreaEncounters(int AreaId, IReadOnlyList<EncounterTable> Tables, EncounterTable? Overlay)`; `EncounterPlan` (by-area dictionary). Slot layouts (pinned, GDD §6.3): Land = 12 slots at 20/20/10/10/10/10/5/5/4/4/1/1; Surf = 5 at 60/30/5/4/1; Fishing = 5 at 60/30/5/4/1 (one rod — B2W2 has only the Super Rod; per-tier tables are a noted deferral, not a TODO for this ticket).

**`LevelCurve` static class (pinned — 7a/7b reuse it):**
- Gym cities = the keys of `RegionIdentity.GymTypes` in critical-path order; `GymAce(i, G) = G == 1 ? 50 : (int)Math.Round(14 + 36.0 * i / (G − 1))` (GDD §7.1: ~14 → ~50).
- `WildLevel(area)`: find the first gym city at a critical-path position **after** the area's position (own `PathIndex`, or its `OffSpineAnchors` anchor for off-spine areas); wild = that gym's ace − 5; if no gym follows (post-last-gym), wild = 54 − 5 = 49. Destination dungeons (`DeepCave`, `Tower`, `VillainHideout`) get +2. The ±2 per-slot jitter is applied in 6b, not here.

**Method assignment** (`EncounterPass.Generate(graph, biomes, gating, dex, streams) → EncounterPlan`, stream `"encounters"`): Land table for archetypes Route, Forest, StandardCave, MountainPath, DeepCave, VictoryRoad, Tower; Surf + Fishing tables additionally for areas with biome `Water` **or** listed in `gating.BiomeRequirements` with a water terrain tag; no tables for StartTown/Town/League/VillainHideout. In this ticket, emit the tables with correct methods and per-area base levels but **empty `Slots`** — 6b fills them.

**Blocked by:** 4a-2 (the plan takes `DexPlan`; pass runs after it in `RegionGenerator`).

- [ ] Every Route/Forest/cave-family/Tower area has a Land table; every Water-biome or water-gated area also has Surf + Fishing; towns/League/hideouts have none
- [ ] `LevelCurve` unit tests: `GymAce(0, 8) = 14`, `GymAce(7, 8) = 50`, monotone non-decreasing; `WildLevel` non-decreasing along availability order (±0 tolerance — jitter isn't in yet)
- [ ] Deterministic; `RegionGraphText` prints each area's methods + base wild level

## 6b. Encounter slot filling & special overlays (split from 6)

**Model:** Sonnet recommended (pinned, but the pool-widening and rarity-tiering interact; Qwen-class feasible after 6a merges).

**What to build:** Fill the 6a tables from the dex; add Special Encounter Overlays. All draws from `streams.Stream("encounters")`.

**Pinned fill, per area:**
- **Pool:** dex species (exclude legendaries — appended #151+ are never wild — and exclude the 2 fossil families) whose type intersects the area's affinity: archetype override first — `Tower` → [Ghost, Psychic, Normal] (GDD §6.2 Tower/Graveyard row; the 7-value `Biome` enum has no Tower biome) — else `BiomeTypeAffinity.Table[biome]`, with `Urban` falling through to neighbouring areas' biomes. If pool < 4 species: widen to Normal-typed dex species; still < 4: all dex species (assert final ≥ 4).
- **Rarity = slot tier** (§6.3: "a species' rarity *is* which slots it holds"). Sort the pool ascending by its family's `FinalBst`; split into quartile tiers: common (bottom 40%), uncommon (next 30%), rare (next 20%), very-rare (top 10%, min 1 each tier). Land slots: the two 20% slots and four 10% slots draw from common/uncommon; the 5%/4% slots from rare; the two 1% slots from very-rare. Draw with `rng.Pick`, repeats allowed within a tier; require ≥4 distinct species per Land table (redraw the last offending slot until satisfied — tiers guarantee it terminates). Surf/Fishing: same tiering over the Water-typed pool, ≥3 distinct.
- **Levels:** per slot `MinLevel = max(2, wild − 2)`, `MaxLevel = wild + 2` where wild is 6a's base level.
- **Overlays** (§6.3/§3 — the only hidden-ability source): every Land-table area with archetype Route or Forest whose availability fraction ≥ 0.5 gets a Dark-Grass-style overlay: 12-slot layout, drawn from the rare+very-rare tiers only, levels +5, `HiddenAbilityChance = 0.5`. Base tables keep 0.

**Blocked by:** 6a.

- [ ] Fuzz invariant: every slot's percentages match the pinned layouts and sum to 100; every wild species ∈ regional dex, never a legendary or fossil-family member; each slot species' type ∈ the area's affinity set (or the widened pool was in effect — assert via the pool-size precondition, not by skipping)
- [ ] Overlays exist exactly on late (≥0.5) Routes/Forests and are the only tables with `HiddenAbilityChance > 0`
- [ ] Wild levels track the curve (non-decreasing along availability order within the ±2 jitter + dungeon +2 tolerance); deterministic; printed by `RegionGraphText`

## 7a. Route & gym-building trainers, item balls

**Model:** Sonnet recommended (pinned tables, but it consumes four upstream plans and the carved tile grid; Qwen-class only after 2c/6a are merged and stable).

**What to build:** `TrainerPass` in `domain/ProcPoke.Generation/Trainers/`, filling every carved `TrainerPost` tile (row-major scan order of `region.Carved[areaId].Grid`) and every `ItemBall`. Stream `"trainers"`.

**Pinned class table** (pick uniformly among the listed candidates): archetype VictoryRoad → Veteran; Tower → Psychic; VillainHideout → `Team <name>` Grunt, hideout's team = region's villain teams alternated in availability order; StandardCave/MountainPath/DeepCave → Hiker; Forest (archetype or biome) → Bug Catcher; else by biome: Grassland → Youngster/Lass; Mountain/Cave → Hiker; Water → Swimmer/Fisherman; Desert → Hiker/Ace Trainer; Urban → Gentleman/Lady.
**Class type-bias** (roster filter): Youngster/Lass/Gentleman/Lady/Veteran/Ace Trainer → any; Bug Catcher → Bug; Hiker → Rock/Ground/Fighting; Swimmer/Fisherman → Water; Psychic → Psychic/Ghost; Grunt → the team's motif types.
**Rosters:** size = 1 + (f ≥ 0.35 ? 1 : 0) + (f ≥ 0.7 ? 1 : 0) where f = availability fraction; ace level = `LevelCurve.WildLevel(area)` + 2 + `rng.NextInt(3)`; other members ace−2, ace−3. Species: `rng.Pick` from dex species available at-or-before this area (`DexPlan.SpeciesByArea` walked in availability order) whose type intersects the class bias; if empty, all available dex species. (Later-dex species on trainers are *allowed* by §5.2 but not required — available-only keeps it simple.)
**Gym trainers:** per gym city, 2 trainers (class Ace Trainer) with mono-type rosters of the gym's type, levels between the city's route wild level and `GymAce − 2` (leader teams are 7b).
**Item balls** by availability fraction: f < 1/3 → `items.json` Category `healing` with Cost ≤ 700; f < 2/3 → `healing` ≤ 2000 or `standard-balls`; else `revival` or `held-items`; any tier: 10% `rng.Chance` swaps in a TM (Category `all-machines`).

**Blocked by:** 2c (carved tiles in the region), 4a-2 (dex), 6a (levels), 3c (grunt team names).

- [ ] Every carved `TrainerPost` has a trainer (count matches tile count per area); every `ItemBall` has contents; classes match the pinned table
- [ ] Fuzz invariant: all trainer species ∈ regional dex; trainer ace levels non-decreasing along the critical path within a ±4 tolerance
- [ ] Deterministic; printed by `RegionGraphText` (per-area class/ace summaries)

## 7b. Gym leader, Elite Four & Champion teams

**Model:** Sonnet recommended (formulas pinned below, but the species-picking fallback chain wants judgment; Qwen-class possible if 7a's roster helper is reused).

**What to build:** `BossPlan` (extend `GeneratedRegion`): leader teams honoring 3b's `GymTypes`, E4 teams, Champion team. **Species + levels only — no movesets in Phase 2.** Stream `"bosses"`.

**Pinned formulas:** Leader i of G (0-based, f = i/(G−1), G==1 → f=1): size = min(6, 2 + round(4f)); ace = `LevelCurve.GymAce(i, G)`; member levels = ace, ace−2, ace−3, ace−4, ace−4, ace−5 (truncate to size). Candidates = dex species carrying the gym type (primary or secondary), no two from one family, BST ≤ 250 + 350f; ace slot = highest-BST candidate, rest descending; if candidates run out, drop the BST cap, then admit any dex species (mono-type padding per §7.1 — assert how often via test message, don't fail). E4 member j (0..3): full 6-mon team of its assigned type, ace = 55 + j, levels [a−2, a−2, a−1, a−1, a−1, a]. Champion (typeless, §7.1): 6 highest-BST dex species, distinct families, ≤2 sharing any type, levels [57, 57, 58, 58, 58, 60]; must include a pseudo-legendary final stage if the dex drew one (hardcode: Dragonite 149, Tyranitar 248, Salamence 373, Metagross 376, Garchomp 445, Hydreigon 635), else one starter corner's final stage (`rng.Pick` of the three).

**Blocked by:** 4a-2 (dex), 3b (done), 6a (`LevelCurve`).

- [ ] Each leader's team is mono-type in its assigned type until the candidate pool exhausts (padding admitted only then); size and ace follow the pinned formulas exactly
- [ ] E4 single-type per member, types distinct; Champion team typeless (no type constraint), includes the pseudo-legendary/starter pick, distinct families, ≤2 per type
- [ ] Fuzz invariant: all boss species ∈ regional dex (legendaries #151+ excluded); levels match the formulas exactly
- [ ] Deterministic; printed by `RegionGraphText`

## 7c. Rival team across beats & rival-Champion team

**Model:** Sonnet (three player-choice variants × four beats × evolution-stage resolution against real `MinLevel` data, plus consuming 3c's roll — genuinely cross-cutting).

**What to build:** `RivalPlan` (extend `GeneratedRegion`): for **each of the 3 possible player starter corners**, the rival's counter-pick and a team per beat. The Champion-identity roll already happened in 3c (`RegionIdentity.ChampionIsRival`) — consume it, don't re-roll. Stream `"rival"`.

**Pinned rules:** `StarterSelector.TriangleCorners` order means corner *i* beats corner *i+1 (mod 3)*; the rival counter to player corner *i* is corner *(i+2) mod 3*. Beats (sizes/aces): post-starter = rival starter base stage at level 5; early-route = 2 mons, ace `GymAce(0)+2`; midpoint = 4 mons, ace `GymAce(G/2)`; pre-League = 5 mons, ace 52. The rival starter appears in every beat at the highest evolution stage its family's `MinLevel` chain permits at that beat's ace level. Non-starter slots: dex species (no legendaries, no duplicate families) picked by BST closest to 250 + 350·(beat fraction). If `ChampionIsRival`: 7b's generated-NPC Champion team is **replaced** by a rival Champion team — 7b's Champion formula but forced to include the rival starter's final stage (per player corner).

**Blocked by:** 4b (done), 3c (`ChampionIsRival`, archetype), 6a (`LevelCurve`), 7b (Champion formula reuse).

- [ ] For every player corner: rival starter is the corner that beats it (assert via the triangle order, and independently via `TypeChart` effectiveness); teams exist at all four beats with non-decreasing ace levels and sizes
- [ ] Starter evolution stage at each beat re-derived in the test from raw `MinLevel` chains
- [ ] Rival-Champion seeds: the Champion team contains the rival starter final stage for each player corner; non-rival seeds keep 7b's team
- [ ] All rival species ∈ regional dex; deterministic; printed by `RegionGraphText`

## 8a. NPC plan: hints, furniture, flavor (split from 8)

**Model:** Qwen-OK (the reachability re-derivation below is spelled out; everything else is templating).

**What to build:** Graph-level `NpcPass` → `NpcPlan` (extend `GeneratedRegion`): per area, a list of `NpcPost(NpcKind Kind, string Text)`. Kinds: Hint, GymGuide, CenterGossip, Sign, Flavor. Stream `"npcs"`. No tiles yet — placement is 8b.

**Pinned content:**
- **Hint NPCs** (the load-bearing part, GDD §4.3 rule 8): for every gate, one Hint post in `gate.HintAreaId`, text from 3 templates `rng.Pick`ed, e.g. `$"I hear {gate.KeyName} waits somewhere in {names.Of(gate.KeyAreaId)}."` — generated names, never internal ids.
- **Furniture:** each gym city gets a GymGuide post (`$"The Leader here runs a {type} gym!"`) and a CenterGossip post (names the nearest villain hideout by generated name, if any); each Route gets one Sign (`$"Route {n} — onward to {names.Of(next critical-path town)}"`).
- **Flavor:** 1–2 per town from a ≥6-template pool parameterized with region/area names.

**Hint invariant — independent re-derivation (do not trust `HintAreaId`):** for gate g, the hintable set = every area whose critical-path position (own `PathIndex`, or `OffSpineAnchors` anchor if off-spine) is **≤ `g.BlockPathIndex`**, *intersected with* the areas actually reachable at that frontier by a `GatingValidator`-style forward replay (walk forward from the start picking up keys, stopping the first time gate g blocks the frontier; collect every on-path area at index ≤ frontier and every off-spine area anchored ≤ frontier). Assert each gate's Hint post lands in its hintable set.

**Blocked by:** 3a (done), 3c (hideout names exist for gossip — soft; can stub if 3c is in flight).

- [ ] Fuzz invariant: for every gate, ≥1 Hint post in an area in the independently re-derived hintable set
- [ ] Hint/gossip/sign text contains generated names and never an internal area id
- [ ] Deterministic; printed by `RegionGraphText` (post counts per area + hint texts)

## 8b. NPC posts on tiles (split from 8)

**Model:** Qwen-OK.

**What to build:** Realize `NpcPlan` on the carved maps. New `LogicalTile.NpcPost` — **walkable** (add to `IsWalkable()`), glyph `n` in `AsciiRenderer`, a distinct color in `MapImage`. In `RegionGenerator`, after carving and `NpcPass`: for each area, stamp one `NpcPost` tile per plan entry on a `rng`-picked (`streams.Stream("npc-tiles")`) walkable `Ground` tile that is not on the spine row and not adjacent to an opening or gate tile; skip-and-log nothing — if an area lacks room (tiny grids), place on any walkable non-opening tile (assert count always matches).

**Blocked by:** 2c (carved maps in the region), 8a (the plan).

- [ ] Per area: `NpcPost` tile count == `NpcPlan` entry count; every post tile was walkable before stamping and `NpcPost` is walkable after (reachability invariants untouched — assert `CarvingTests` still green)
- [ ] Posts render in ASCII and PNG debug maps
- [ ] Deterministic

## 9. Go/no-go sign-off packet

**Model:** Sonnet (explicitly — the old ticket already said the mechanical half is "the part Sonnet does"; it's cross-cutting glue over every pass).

**What to build:** Execute the Phase 2 exit criterion (DEVELOPMENT-PLAN §Phase 2). Two parts:

**(1) Mechanical.** A domain `Invariants.CheckAll(GeneratedRegion, GameData) → IReadOnlyList<string>` consolidating the fuzz assertions (chokepoint, key-before-gate, hint-before-gate, edge alignment, names off-blocklist, dex numbering & no-broken-families, type coverage at defaults, encounter/trainer ⊆ dex, level monotonicity) so tests and the harness share one implementation. Then a MapGen mode `--packet [seeds]` that (a) sweeps seeds 1..N (default 10,000) × badges {4, 8, 12} through `CheckAll`, reporting any violating seed and failing non-zero; (b) renders the pinned showcase seeds {2, 7, 42, 99, 123, 500, 777, 1234, 4242, 9001} × badges {4, 8, 12} to per-area PNGs + stitched overviews; (c) writes `PACKET.md` with the sweep summary and a per-archetype "distinct? yes/no" self-check computed from the 5a/5b/5c signature asserts (expose those checks as a shared helper, e.g. `Debug/CarverSignatures`, so tests and packet agree).

**(2) Human.** The user judges whether the maps read hand-crafted (trainers guard the path, ledges create shortcut asymmetry, item nooks reward poking around, no mush). Record the verdict in `PACKET.md`; if it fails, the deliverable is a documented gap list feeding an ADR-0001 reconsideration.

**Blocked by:** every other ticket in this file.

- [ ] `Invariants.CheckAll` passes on ≥10,000 seeds × {4, 8, 12} badges via one command
- [ ] One command generates the packet; overviews are spatially stitched (2b); packet includes the per-archetype distinctness self-check
- [ ] Human verdict recorded: pass, or a gap list + explicit decision on ADR-0001
