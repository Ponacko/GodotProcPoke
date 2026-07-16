# Tickets: Phase 2 completion — generator go/no-go

Finish DEVELOPMENT-PLAN Phase 2 (the go/no-go milestone): the rest of 2b map carving, all of 2c population, and the sign-off packet. Source: `docs/DEVELOPMENT-PLAN.md` §Phase 2, the GDD sections cited per ticket, ADR-0001/0002/0004/0005. Topology, gating, biomes, first-cut carvers, and gates-on-tiles are already done and fuzz-tested.

**These tickets are sized for a mid-tier implementer.** Each one names the file(s) it lives in, sketches the algorithm, and states its acceptance as **checkable assertions or fuzz invariants** — not subjective judgment. The one genuinely subjective call ("do the maps read hand-crafted?") is deferred to the final sign-off ticket, where a human makes it. When a ticket says "deterministic," it means: same seed → identical output, and it draws only from a named RNG stream (`streams.Stream("<name>")`, ADR-0005) — never `System.Random`, never wall-clock.

House rules that apply to every ticket:
- New generation passes hang off `RegionGenerator.Generate` and extend the `GeneratedRegion` record (`domain/ProcPoke.Generation/RegionGenerator.cs`). Follow the existing pass pattern (`GatingGenerator`, `BiomePass`).
- Baked game data (species, moves, evolutions, types) loads through `ProcPoke.Data` — `GameDataLoader.Load(dataDir)`. A species' BST is the sum of its `BaseStats`. Legendaries carry `IsLegendary`.
- Add a fuzz test alongside the existing ones in `tests/ProcPoke.Generation.Tests/`; the standard corpus is `seed = 1..2000` × badge counts `{4, 8, 12}` (carving-heavy suites may use a smaller corpus, matching `CarvingTests`).
- New debug output goes through `RegionGraphText` (text) or `MapImage`/`AsciiRenderer` (maps) so it shows up in the MapGen harness.

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

**What to build:** For every `Seamless` connection between two areas, the opening each area carves on the shared edge must line up with its neighbour's — same edge side, same offset along that edge, same width — so Phase 3 can scroll between them without a seam. Today each carver picks its own opening at its own `midY`, and neighbours have different heights, so two connected areas disagree on where the doorway is.

**Approach:** Openings are decided *before* the per-area carve, by a small pass that walks the graph and assigns each `Seamless` connection a shared opening descriptor (which edge of each area, the offset, the width — start with width 1). Carvers receive the opening positions for their area instead of hard-coding `(0, midY)`/`(w-1, midY)`. Warp connections are unaffected (they keep their warp tiles and need no alignment). Where two areas have different sizes on a shared vertical edge, clamp the shared offset into both areas' valid interior range.

**Blocked by:** None — can start immediately.

- [x] A carver takes its per-edge opening positions as input rather than computing `midY` itself; existing callers pass the aligned positions
- [x] Fuzz invariant: for every `Seamless` connection, the two areas' openings on the shared edge have identical offset and width (converted to a common edge coordinate)
- [x] Warp connections still produce warp-tile openings; nothing regresses
- [x] Existing spine-walkability and mutual-reachability invariants (`CarvingTests`) stay green

## 2b. Spatially stitched region overview — ✅ DONE

Delivered alongside 2a. `OverviewLayout.Plan` (domain, fuzz-tested in `OverviewLayoutTests`) assigns each
area a `(Col, Row)` cell: critical path left→right at `(PathIndex, 0)`, off-spine areas hung one row
above/below/further-out their `GatingGenerator.OffSpineAnchors` column. `MapImage.SaveOverview` blits every
area's carved footprint into a canvas sized per column/row band and draws a connector between each
connection's pair of openings (aligned opening tile for Seamless, area center for Warp). Verified visually
via `dotnet run --project tools/ProcPoke.MapGen -- 42 --badges 8 --png` — the overview reads as one
horizontally-flowing map with branches stacked above/below the spine, not a stack.

**What to build:** Replace the current top-to-bottom stack in `MapImage.SaveOverview` with a real 2-D layout: areas placed adjacent along their connections so the overview reads as one map. This is the artifact the go/no-go review judges.

**Approach (explicit, so layout isn't open-ended):** Assign each area an integer grid cell `(col, row)`. Lay the critical path left→right: `StartArea` at `(0, 0)`, each next critical-path area at `col+1`. Hang each off-spine area off its anchor (the critical-path area it connects to — see `GatingGenerator.OffSpineAnchors` for the exact rule): place it one row above the anchor if that cell is free, else one row below, else one further out. Then blit each carved area's tile grid into a canvas at `cell.col * (maxAreaW + gap)`, `cell.row * (maxAreaH + gap)`. Draw a thin connector line between the openings of connected areas. Do not attempt force-directed or pixel-perfect edge butting — the grid-cell layout is enough for the review.

**Blocked by:** 2a (edge-aligned openings).

- [x] `SaveOverview` lays areas in 2-D grid cells: critical path in a row, off-spine areas above/below their anchor
- [x] No two areas overlap in the canvas; every area appears exactly once
- [x] Connected areas have a visible connector drawn between them
- [x] `dotnet run --project tools/ProcPoke.MapGen -- <seed> --png` writes the stitched `_overview.png`

## 3a. Location name generator — ✅ DONE

Delivered in commit "Implement tickets 3a/3b/4b: naming, gym/E4/Champion typing, starter triangle".
`NameParts` (`domain/ProcPoke.Generation/Identity/`) holds 4 motifs (Colors, Flora, Minerals,
WeatherLight) with ~6 roots + ~6 blends each; `NamingPass` draws one motif per region from
`streams.Stream("names")`, numbers Route areas `Route 1..N` in critical-path order, and blends
city/dungeon names, redrawing on a `name_blocklist.json` hit or in-region collision. `NamingTests`
(6 cases) fuzzes the standard corpus; `RegionGraphText` prints the motif and every area's name.

**What to build:** A per-seed name for every city and dungeon (routes keep `Route N` in critical-path order). GDD §8: one **Naming Motif** per region (colors, flora, minerals, weather, light), names blended from curated word-part pools, city = motif-root + settlement suffix (`-burgh`, `-port`, `-vale`, `-ton`), dungeon = biome-aware form (`Emberdeep Cave`, `Palegrove Forest`). No name may match `name_blocklist.json` (baked already) or collide within the region — redraw on either.

**Approach:** Author a static `NameParts` table in `domain/ProcPoke.Generation/Identity/` holding, per motif, a pool of roots and the suffix/biome-form lists (seed it with ~6 roots per motif and the four motifs above — enough to be non-repeating at 8 badges; expandable later). Draw the motif from `streams.Stream("names")`, then draw parts per area, rejecting blocklist hits and in-region duplicates and redrawing. Load the blocklist via `ProcPoke.Data`.

**Blocked by:** None — can start immediately.

- [x] Every city and dungeon gets a name; routes are `Route 1..N` in critical-path order
- [x] Fuzz invariant over the corpus: no generated name is in `name_blocklist.json`, and no two areas in one region share a name
- [x] One motif per seed; deterministic (same seed → same names)
- [x] `RegionGraphText` prints the names next to each area

## 3b. Gym / Elite Four / Champion type assignment — ✅ DONE

Delivered alongside 3a. `BiomeTypeAffinity` encodes the §6.2 table over the 7-value `Biome` enum;
`GymTypingPass` ranks each gym city's own+neighbour biome affinity (ties shuffled off
`streams.Stream("gym-types")`), picks the highest-ranked untaken type per gym in critical-path
order, falling back to any untaken type once a city's affinity pool is exhausted, then draws 4
more distinct types for the Elite Four. Champion is unconditionally typeless. `GymTypingTests` (9
cases) fuzzes the standard corpus, including a reconstructed-fallback check for the exhaustion case.

**What to build:** Assign each gym a distinct type chosen by **biome affinity** (GDD §7.1 + §6.2 read in reverse: a mountain-ringed gym city leans Rock/Ground/Fighting, a port leans Water). Elite Four get types distinct from each other and from the gyms while the 17-type count allows. The Champion is **typeless** (flagged as such — team composition comes later in 7b).

**Approach:** For each gym city, look at its own and neighbouring biomes, map biome→candidate types via the §6.2 bias table, and pick the highest-affinity type not already taken by an earlier gym (deterministic tiebreak from `streams.Stream("gym-types")`). Then draw E4 types from the remaining/pool with the distinctness rule. Store the assignments on the region (extend `GeneratedRegion`, e.g. a small `RegionIdentity` record).

**Blocked by:** None — can start immediately.

- [x] Fuzz invariant: gym types are pairwise distinct within a region
- [x] Each gym's type is in its biome's §6.2 affinity set (or an adjacent area's) — assert membership
- [x] E4 types distinct from each other and from gyms at default settings; Champion marked typeless
- [x] Deterministic; printed by `RegionGraphText`

## 3c. Villain team & rival identity

**What to build:** Per seed: 1–2 villain organizations, each with a generated name (via 3a's generator, checked against the short hardcoded canon-team-name list) and a thematic type motif; and one rival **archetype** drawn from the §7.2 set (Cocky, Friendly, Brooding, Underdog). This ticket is identity only — villain grunt rosters and rival *teams* are 7b/7c.

**Approach:** Small addition to the `RegionIdentity` record. Reuse the 3a name machinery for team names with the team-name blocklist. Pick archetype and team count (1 or 2) from `streams.Stream("identity")`.

**Blocked by:** 3a (name generator).

- [ ] 1–2 villain teams, each with a name not in the canon-team list and a type motif; deterministic
- [ ] Exactly one rival archetype per seed, stable across the run
- [ ] Printed by `RegionGraphText`

## 4b. Starter triangle selection — ✅ DONE

Delivered alongside 3a/3b. `StarterSelector` (`domain/ProcPoke.Generation/Roster/`) rebuilds 3-stage
lines from `evolutions.json`, filters to level-only (excluding friendship evolutions, which stay
`Trigger=LevelUp` with `MinHappiness` set), final BST ≤ 540, and within `RosterCap`
(`SpeciesGeneration.Of`, Gen 1–5 breakpoints), then brute-forces the corner-triple with the
smallest final-BST spread (≤ 40) per available template, drawn from `streams.Stream("starters")`.
`StarterSelectorTests` (11 cases) verifies against ground truth from the pinned data (Elemental
clears at Roster Cap 3+, Mind & Body only at Cap 5, Classic at every cap) and independently
re-derives each chosen line from raw evolution rules rather than trusting the selector's internals.

**What to build:** Pick the three starters (GDD §5.3). Choose a triangle template (Classic / Mind&Body / Elemental) — templates with an empty corner pool under the current `RosterCap` are unpickable; Classic is the guaranteed floor. For each corner, the eligible pool is every **3-stage** evolution line whose final stage carries the corner's type, final BST ≤ 540, **completable by leveling alone** (exclude lines needing stones / Link Cable / friendship — read the `EvolutionRule`s), and within `RosterCap`. Pick one distinct line per corner with final BSTs close together.

**Approach:** New `StarterSelector` in `domain/ProcPoke.Generation/Roster/`, over `ProcPoke.Data`. Build evolution lines from `evolutions.json`; a line is "level-only" iff every step's `EvolutionRule` is a plain level-up (no item/trade/happiness). Score corner-triples by BST spread and pick the tightest from `streams.Stream("starters")`. The Rock→Flying→Fighting triangle's Flying corner must carry Flying as one of its types.

**Blocked by:** None — can start immediately.

- [x] Fuzz invariant over the corpus: three distinct 3-stage lines, one per triangle corner, each final BST ≤ 540, each level-only-completable, each within `RosterCap`
- [x] Templates with an empty corner under the cap are never chosen; Classic always available
- [x] Final BSTs are "close" — assert max−min ≤ a stated threshold (e.g. 40)
- [x] Deterministic

## 4a. Regional dex selection & numbering

**What to build:** Select the `DexSize` (150) species and number them (GDD §5.1, §5.2). Evolution lines stay together in consecutive slots. Dex #1–#9 are the three starter lines (from 4b). #10 onward follows **first-availability order** along the critical path (Route 1's population, then City 1's, …). Placement is weighted by a line's **final-stage BST** (low-BST lines trend early, high-BST late — the Caterpie→Dragonite curve), with a **type-coverage bias** so no type is left unrepresented (§5.6).

**Approach:** New `DexSelector` in `Roster/`. Pool = all evolution lines within `RosterCap`, minus the starter lines (already placed at #1–9). Greedily assign lines to critical-path areas: for each area in order, pick lines whose final BST fits the area's progression band, breaking ties toward under-represented types (track running per-type counts). Number by assignment order. Keep going until `DexSize` species are placed. Draw from `streams.Stream("dex")`.

**Blocked by:** 4b (starters occupy #1–9 and are excluded from the fill).

- [ ] Fuzz invariant: dex holds exactly `DexSize` species (+ the 9 starter slots) with **no broken evolution lines** (every included species' full line is included and consecutive)
- [ ] Fuzz invariant: dex numbering equals first-availability order along the critical path
- [ ] Every one of the 17 types has ≥1 representative (assert non-zero counts) at default settings
- [ ] Deterministic; printed by `RegionGraphText` (number, name, types, final BST)

## 4c. Fossils & legendaries

**What to build:** Add 2 fossil species (from the fossil-eligible pool) into a single Rock/Ground cave-type destination dungeon, and 4 legendaries (`IsLegendary`, within `RosterCap`) placed in mythology-appropriate destination dungeons as static encounters (GDD §5.4, §5.5). Legendaries are optional — the solvability graph ignores them (don't touch gating).

**Approach:** Extend the dex/identity output. Fossil-eligible pool = a small hardcoded species-id list (Kabuto/Omanyte/Aerodactyl/Lileep/Anorith/Cranidos/Shieldon/Tirtouga/Archen lines, filtered by cap). Pick placement dungeons from the off-spine destination dungeons by archetype (Tower/DeepCave/Ruins). Draw from `streams.Stream("special-species")`.

**Blocked by:** 4a (draws from the settled dex/pool).

- [ ] Exactly 2 fossils placed together in one cave-type dungeon; exactly 4 legendaries, one per placement dungeon
- [ ] All are within `RosterCap`; legendaries carry `IsLegendary`
- [ ] Gating/solvability output is unchanged (assert the plan is byte-identical with and without this pass)
- [ ] Deterministic; printed by `RegionGraphText`

## 5a. Forest carver: de-stripe

**What to build:** `ForestCarver` currently lays full-height vertical tree columns — it reads as literal stripes. Replace them with organic tree **clumps** of varied size/position that still leave the spine open and keep left↔right traversal guaranteed.

**Blocked by:** 2a (opening positions come from the alignment pass).

- [ ] Assert: no interior column is entirely `Tree` (no full-height stripe) except the border
- [ ] Assert: tree tiles form ≥ N discrete clumps (connected-component count over `Tree` tiles ≥ a stated N), not evenly spaced walls
- [ ] Spine rows stay open; `CarvingTests` spine + mutual-reachability invariants stay green
- [ ] Deterministic

## 5b. Cave carver: rooms, winding passages, boulder fields

**What to build:** `CaveCarver` currently carves one thin straight corridor to a chamber. Give caves ≥2 open **rooms** connected by winding (non-straight) corridors, scattered impassable `Boulder` tiles as texture (not gates), and the reward item in a room. Keep both-openings-reachable (transit) / single-entrance-reachable (destination) guarantees.

**Blocked by:** 2a.

- [ ] Assert: ≥2 rooms (open rectangles above a min size); corridors are not a single straight line (path bends at least once)
- [ ] Assert: some `Boulder` decoration exists and never blocks the only route between openings (mutual-reachability invariant still green)
- [ ] Item sits inside a room, off the direct corridor
- [ ] Deterministic

## 5c. Distinct League, Villain Hideout & Tower carvers

**What to build:** These three archetypes currently reuse the town/cave carvers, so the League looks like a starter village and the hideout like a plain cave. Give each a dedicated carver in `Carving/` with a recognizable structural signature, and dispatch to them from `AreaCarver`. League = a gauntlet of chambers leading to a final throne room; Villain Hideout = a multi-room interior with grunt-post rooms and a boss room; Tower = stacked floor bands connected by stair warps.

**Blocked by:** 2a.

- [ ] `AreaCarver` dispatches `League`, `VillainHideout`, `Tower` to their own carvers (not `TownCarver`/`CaveCarver`)
- [ ] Each carver produces a documented structural signature, asserted mechanically (e.g. League has ≥3 chambers in sequence + one terminal room; Tower has ≥2 floor bands separated by walls with stair warps between them)
- [ ] All carving invariants (walkability, mutual reachability, chokepoint if gated) stay green
- [ ] Deterministic; the three render visibly differently from a town and from each other in ASCII

## 5d. Town & route variation + decoration rules

**What to build:** `TownCarver` uses a fixed `[4,4,3]` building layout every time; vary it per seed. Apply the §-carver decoration rules everywhere: ledges that drop *toward* the entrance (shortcut asymmetry), item nooks off the main path, trainer posts positioned to watch the spine.

**Blocked by:** 2a.

- [ ] Assert across seeds: building count/positions vary (not identical layouts for different seeds)
- [ ] Assert: route ledges are oriented toward the entrance side; item balls are not on the spine row; trainer posts are within sight of the spine
- [ ] Carving invariants stay green; deterministic

## 6. Encounter tables

**What to build:** Per-area wild encounter tables (GDD §6.3). Clone the Gen 5 **slot model**: tall-grass/cave = 12 slots at 20/20/10/10/10/10/5/5/4/4/1/1; surf = 5 slots at 60/30/5/4/1; fishing per rod tier. Fill slots from the area's biome pool (§6.2 type bias) by rarity — a species' rarity *is* which slots it holds. **Special Encounter Overlays** are separate small tables and the only source of hidden abilities. Wild level per area ≈ (next gym ace − 5) ± 2 (§6.3); destination dungeons ~+2.

**Approach:** New `EncounterPass` in `domain/ProcPoke.Generation/Encounters/`, run after the dex pass. Species eligible for an area = dex species whose types match the area's §6.2 bias; assign to slots by dex position/BST as a rarity proxy. Draw from `streams.Stream("encounters")`.

**Blocked by:** 4a (needs the settled dex).

- [ ] Every area with tall grass / surfable water / fishing gets a slot table with the canonical percentages above
- [ ] Fuzz invariant: every wild species is in the regional dex; the biome type-bias is respected (each slot's species matches the §6.2 set for that biome)
- [ ] Special Encounter Overlays exist where §6 mandates (e.g. Dark Grass) and are the only tables carrying a hidden-ability roll
- [ ] Wild levels track the progression fraction; deterministic; printed by `RegionGraphText`

## 7a. Route & gym-building trainers, item balls

**What to build:** Fill the carved `TrainerPost` tiles with trainers and the `ItemBall` tiles with contents. Trainer **class** by biome (GDD §7.1 table: Bug Catcher in Forest, Hiker in Mountain/Cave, Swimmer on Surf routes, …); **roster** a subset of the regional dex on the §7.1 level curve (trainer ace ≈ area wild level + 2–4); item/TM contents badge-appropriate.

**Approach:** New `TrainerPass` in `domain/ProcPoke.Generation/Trainers/`. For each carved area, read its `TrainerPost`/`ItemBall` counts, pick a class from the biome, and build rosters from the dex within the area's level band. Draw from `streams.Stream("trainers")`.

**Blocked by:** 4a (dex).

- [ ] Every `TrainerPost` gets a trainer with a biome-appropriate class and a dex-subset roster; every `ItemBall` gets contents
- [ ] Fuzz invariant: all trainer species ∈ regional dex; trainer ace levels rise (non-decreasing within tolerance) along the critical path
- [ ] Deterministic; printed by `RegionGraphText`

## 7b. Gym leader, Elite Four & Champion teams

**What to build:** Build the boss teams honoring 3b's type assignments. Gym leader teams are single-type (thin types padded with dual-types), size scaling 2→6 along the badge curve; leader ace ~14→~50 interpolated. E4 = full 6-mon single-type teams at ~54–58. Champion = typeless, broadest/highest-BST 6-mon team at ~59–60, favoring a starter final stage or a pseudo-legendary if the dex drew one.

**Blocked by:** 4a (dex), 3b (type assignments).

- [ ] Each gym leader's team is its assigned type (or dual-types including it); size and ace level follow the badge curve
- [ ] E4 teams single-type per member, distinct; Champion team typeless and highest-BST-band
- [ ] Fuzz invariant: all boss species ∈ regional dex; ace levels match the §7.1 bands within tolerance
- [ ] Deterministic; printed by `RegionGraphText`

## 7c. Rival team across beats & Champion-identity roll

**What to build:** The rival's starter is auto-picked as the counter to the player's (standard "rival picks your counter"); the rival team **evolves across the four beats** (post-starter, early route, midpoint, pre-League), tracking the player-facing curve. Roll Champion identity: ~25% of seeds the **rival is the Champion** (foreshadowed pre-League); rival-Champion seeds weight the Underdog archetype higher. Reuses 7b's team machinery.

**Blocked by:** 4b (starters, for the counter-pick), 3c (rival archetype), 7b (team-build machinery).

- [ ] Rival starter is the effectiveness-counter to each possible player pick; team present at all four beats with non-decreasing strength
- [ ] Champion-identity roll is deterministic and ~25% rival across the corpus (assert the rate within a band); rival-Champion seeds skew Underdog
- [ ] All rival species ∈ regional dex; deterministic; printed by `RegionGraphText`

## 8. NPC posts & hint reachability

**What to build:** Emit NPC posts into carved towns/routes: **Hint NPCs** naming each gate's key location, furniture NPCs (Gym guide, Center gossip, signs), and flavor NPCs from templates parameterized with the generated names. The load-bearing part is the hint invariant (Phase 3 addendum): every gate has ≥1 Hint NPC in an area reachable *before* that gate.

**Approach:** New `NpcPass` (or fold into population). Each gate already carries `HintAreaId` (`Gate.HintAreaId`) — place the Hint NPC there and verify reachability independently, the way `GatingValidator` verifies keys. Hint text pulls the generated key/area names from 3a.

**Blocked by:** 3a (names for hint text).

- [ ] Fuzz invariant: for every gate, ≥1 Hint NPC sits in an area reachable before the gate (independent flood-fill, not trusting `HintAreaId`)
- [ ] Hint text references generated key/area names, not internal ids
- [ ] Furniture/flavor NPC posts land on walkable tiles; deterministic; render in the debug maps

## 9. Go/no-go sign-off packet

**What to build:** Execute the Phase 2 exit criterion. Two parts: (1) the **mechanical** part Sonnet does — run the complete invariant suite over ≥10,000 seeds and produce a render packet (stitched overview + per-area maps for ~10 diverse seeds across varied badge counts) via one harness command, plus a structured self-check listing which archetypes are now visually distinct; (2) the **human** part — the user judges whether the maps "read hand-crafted" (trainers guard the path, ledges create shortcut asymmetry, item nooks reward poking around, no mush). Record the verdict; if it fails, the deliverable is a documented gap list feeding an ADR-0001 reconsideration.

**Blocked by:** every other ticket in this file.

- [ ] Complete invariant suite (chokepoint, key-before-gate, hint-before-gate, edge alignment, names, dex numbering, coverage, trainers ⊆ dex) passes on ≥10,000 seeds
- [ ] One command generates the packet; overviews are spatially stitched (2b); packet includes a per-archetype "distinct? yes/no" self-check
- [ ] Human verdict recorded: pass, or a gap list + explicit decision on ADR-0001
