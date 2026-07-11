# ProcPoke — Development Plan

Expands GDD §14's build order into concrete phases. Ordering principle: **de-risk by uncertainty, not familiarity** — the generator (the unproven bet) comes before the battle engine (a fully documented spec). Each phase has a hard exit criterion; a phase isn't done until its criterion holds.

Sizes are relative (S/M/L/XL), not calendar promises. Phases are sequential; sub-items within a phase can interleave.

---

## Phase 0 — Scaffolding (S)

Turn the empty Godot project into a workable solution.

- Rename project (`New Game Project` → ProcPoke); fix project settings for a 2D pixel game (2D renderer, pixel snap, integer-scaled viewport at GBA-like resolution; drop the 3D physics config).
- Solution layout per ADR-0003 — engine-independent domain libraries:
  - `ProcPoke.Data` — baked-data access (read side of Phase 1)
  - `ProcPoke.Generation` — Region Graph + carvers
  - `ProcPoke.Battle` — battle simulation
  - `ProcPoke.*.Tests` — xUnit projects for each
  - Godot project references the libraries; scenes/nodes are presentation only.
- `dotnet test` runs green from the CLI; add a trivial CI step if the repo gets a remote runner.

**Exit criterion:** a failing domain unit test fails the build, without opening the Godot editor.

## Phase 1 — Data Pipeline (M)

Bake PokeAPI's B2W2 snapshot into game-native resources (GDD §10). Design resolved in the Phase 1 grilling session (July 11, 2026):

- **One C# console tool** at `tools/ProcPoke.Bake` with subcommands `fetch` / `bake` / `assets` — no shell scripts. It references `ProcPoke.Data`'s record types, so writer and reader can't drift.
- **Source pinning:** `fetch` downloads only the needed CSV files from the `pokeapi/pokeapi` repo at a **hard-coded commit SHA** (constants beside the fetch code) into a gitignored cache.
- **Output:** per-entity JSON (`species.json`, `moves.json`, `learnsets.json`, `type_chart.json`, `evolutions.json`, `items.json`, `natures.json`, `growth_rates.json`, `name_blocklist.json` — every official location name, for the name generator — and `manifest.json`) under `data/` at the project root — **committed to git**, diff-reviewed like code. The **manifest** records source SHA + bake-tool version; a `ProcPoke.Data` test asserts it matches the pin in code (no silent drift).
- **Base forms only:** form variants are dropped at bake (Deerling fixed to spring); the form system is Post-MVP. In-battle form abilities (Forecast, Zen Mode) fall under the battle engine's fallback policy.
- **Hidden abilities are baked** (flagged per species); gameplay assigns them only via Special Encounter Overlays (~50% there, never elsewhere).

The bake itself, filtered to version-group `black-2-white-2`:
  - Species 1–649: base stats, types, abilities, catch rate, EXP yield, EV yield, growth rate, gender ratio.
  - Learnsets (level-up/TM/tutor/egg as data — egg moves inert until breeding ships), full move table (power, accuracy, PP, class, priority, effect ID + parameters), 17-type chart.
  - Evolution chains, with the trade-method → **Link Cable** transform applied at bake time.
  - Items subset: balls, medicine, TMs/HMs, evolution items, held items referenced by evolutions, key-item templates.
  - Natures, growth-rate curves, status/crit/stat-stage constants.
- **`assets` subcommand**: download + organize by species ID — Gen 5 animated battle sprites (PokeAPI sprites repo), menu icons (pokesprite), cries (PokeAPI cries repo), each pinned at a commit SHA. Assets stay out of git (the tool + pins are the reproducibility story).
- Fidelity tests: spot-check baked values against independently known Gen 5 facts (e.g. Bulbasaur 45/49/49/65/65/45; Tackle in B2W2 = 50 power/100 acc; ground immune to electric; Kadabra evolves via Link Cable after bake).

**Exit criterion:** all 649 species load through `ProcPoke.Data` with complete B2W2 learnsets, and the fidelity test suite passes.

## Phase 2 — Generator, Headless (XL) — **go/no-go milestone**

The whole bet, with no gameplay attached. Three sub-stages, all engine-free, all fuzz-tested across thousands of seeds.

Design resolved in the Phase 2 grilling session (July 11, 2026):

- **Pass pipeline (ADR-0004):** seed → topology → gates/keys → biomes → dungeon archetypes → roster → population → identity → carving. Biomes come *after* gates; portable obstacles adapt via Terrain Insets, terrain-bound obstacles emit Biome Requirement fixed points — conflicts unrepresentable.
- **Randomness (ADR-0005):** hierarchical named RNG streams from the master seed over a version-stable PRNG; `System.Random` banned in generation code.
- **Topology:** constructive growth — backbone chain, then quota-driven attachment operations (transit-dungeon splice by pacing rule, destination dungeons by Gate Budget + reward sites, dead-end branches, ≥1 loop-back). Valid by construction when quotas are spent.
- **Scale:** per-archetype size classes with mainline-anchored footprints; bigger regions get more areas, never bigger ones.
- **Carver contract:** carvers emit Logical Tile grids + markers (owned by `ProcPoke.Generation`); a Phase 3 Tile Realizer owns all art. The go/no-go review judges colored logical maps.
- **Starters:** any 3-stage line matching a triangle corner (final BST ≤ 540, level-only-completable, within Roster Cap); triangle templates filtered to non-empty corners under the cap.
- **Names:** per-seed Naming Motif + curated word-part pools; blocklist baked from PokeAPI's location tables.
- **Difficulty:** progression-fraction level curve (gym aces ~14→~50, E4 ~54–58, Champion ~59–60, wilds = next ace − 5 ± 2); badge obedience cut.
- **Encounters:** Gen 5 slot model cloned (12-slot grass, 5-slot surf, rod tiers); rarity = slot assignment; Special Encounter Overlays as separate small tables carrying hidden-ability rolls.
- **Gyms:** distinct types with biome affinity; E4 distinct while type count allows; typeless Champion.

### 2a — Region Graph

- Topology: critical-path chain (routes/cities), branch points, dead-end rewards, loop-backs; transit/destination dungeon interspersal honoring the "never >2 plain areas" mandate.
- Gate Budget from region size; obstacle selection (guaranteed classics first, weighted draws after); constructive lock-and-key placement per ADR-0002; HM badge prerequisites; Fly placement.
- Biome assignment; dungeon archetype selection (MVP subset: Forest, Standard Cave, Villain Hideout, Tower, Mountain Path, Victory Road/League).
- Identity generation: location names (+ canon blocklist), villain team(s), rival archetype, gym leader types.

### 2b — Map Carving

- Carvers per MVP archetype (route, town, forest, cave, mountain, hideout interior, tower, League), spine-first with gates on verified chokepoints per ADR-0001.
- Decoration rule engine: tree lines, ledges (drop toward entrance), tall grass straddling the spine, water bodies, item nooks, building placement in towns.
- **Debug renderer** — PNG per map + whole-region stitch; this is the review artifact.

### 2c — Population

- Roster selection: 150+4 dex, lines-together, BST placement curve, type-coverage bias, starter triangle, fossils, legendaries.
- Encounter tables per area from biome type-bias tables (incl. fishing tiers, day/night variants where applied).
- Trainer generation: classes by biome, rosters as regional-dex subsets, level curve along progression; gym/E4/Champion teams; rival team evolution across beats; item/TM placement.

### Invariant test suite (runs on every seed in the fuzz corpus)

- Flood-fill from start without key K never reaches past K's gate (chokepoint guarantee).
- Every key reachable before its gate given obtainable badges; §4.5 assertions never fire.
- ≥1 branch/loop-back; ≤2 consecutive plain areas; all names miss the blocklist; every trainer species ∈ regional dex; dex numbering matches first-availability order.

**Exit criterion (go/no-go):** invariants hold over ≥10,000 fuzz seeds, **and** a human review of rendered regions judges the maps as reading hand-crafted (trainers guard the path, ledges create shortcut asymmetry, item nooks reward poking around, no mush). If carving can't clear the bar after honest iteration, this is where we reconsider ADR-0001 — before any gameplay exists.

## Phase 3 — Overworld (L)

First time the generated region is walked.

- Tile realization: carver output → Godot TileMaps via a tileset mapping layer (Essentials-community tilesets); this is the only place carver output meets art.
- GBA-style grid movement, collision from carver data, camera; map transitions (route/town/dungeon edges, doors, cave entrances, warps).
- Interactables: signs, static NPCs with generated dialogue stubs, item balls, gates as physical objects with HM/key prompts (debug-unlock menu until badges exist), ledge hops, Bicycle movement.
- Time-of-day clock + night tint; region map screen (functional debug version).

**Exit criterion:** walk a generated region start → League on foot, with every gate physically blocking until debug-unlocked, across 3+ different seeds without a broken map.

## Phase 4 — Battle Engine (XL)

Headless first, presentation second (ADR-0003).

### 4a — Simulation (engine-free)

- Battle state machine: turn order (Speed/priority), damage formula, crits, STAB, type effectiveness, accuracy/evasion, stat stages, full status conditions, weather (battle-side).
- Effect-archetype engine + parameterized effects covering the archetype-able ~90%; **fallback policy** (plain damage / no-op, logged, dev-flagged) for the rest per GDD §14.
- Catch formula + shake checks; XP/EV gain; level-up, move learning, evolution triggers (level/stone/friendship/time/Link Cable).
- AI tiers: wild (random legal move), trainer (Gen-5-style "smart" — prefers effective moves, switches rarely), boss (gym/E4 flags).
- Reference test suite: known Gen 5 damage-calc cases, catch-rate cases, turn-order edge cases (priority ties, paralysis quarter-speed, etc.).

### 4b — Presentation & wiring

- Battle scene: Gen 5-style layout, animated sprites, HP bars/status icons, move/bag/party/run menus, catch animation, cry playback (the one audio item that isn't deferred).
- Wild encounters wired to tall grass/cave/surf/fishing tables; battle transition; whiteout → last Pokémon Center.

**Exit criterion:** reference suite green; in-game, catch a wild Pokémon, train it to a level-up evolution, and win/lose wild battles in a generated region.

## Phase 5 — Loop Closers (XL) → **MVP**

Everything that turns "walking + battling" into a Pokémon game.

- Trainer battles: sightline engagement (data already carver-placed), reward money, one-time defeat flags.
- Gyms: leader battles, badge grants, badge-gated HM field use now enforced (debug-unlock retired).
- HM/key field actions: Cut/Surf/Strength/etc. overworld effects, Move Deleter, key-item usage (Bicycle already in, Poké Flute-equivalents, scopes, tickets).
- Economy & services: marts (progression-scaled stock), Pokémon Centers, PC boxes, party management UI, bag.
- New-game flow: intro, starter selection (lab scene), rival hooks; scripted rival beats (post-starter, early route, midpoint, pre-League).
- Villain arc: hideout gauntlets granting gated key items, boss fights, two-team independence rules.
- Fossils (revival service), legendary static encounters + B2W2 respawn, Link Cable item in the economy.
- Elite Four → Champion → credits; post-game unlock state.
- Save system: multiple slots, full-region serialization, save-anywhere-per-mainline rules, Seed String encode/decode with version stamp.

**Exit criterion — MVP:** a complete blind playthrough of a fresh default-settings seed, start → Champion, by a human, without debug tools, without a progression-blocking bug. All core systems present; variety reduced is fine.

## Phase 6 — Shell & Polish (L)

- Generation Screen: parameter controls, repeated Generate preview, Public Pokédex toggle, manual seed entry, Seed String sharing (GDD §9.1).
- Pokédex UI (seen/caught, regional numbering); region map polish (visited-state, Fly targeting).
- **Audio phase** (deferred from §10): fan-made music (~15 tracks with mainline-style reuse), SFX pack integration, volume options.
- Options menu, text speed, key rebinding; title screen with slot selection.
- Balance passes: level curve, trainer density, encounter rates, mart pricing — tuned via full-run playtests across seeds.
- Soak: long fuzz runs on the invariant suite; performance pass on Generate-button latency and map streaming.
- Widen variety: remaining dungeon archetypes (Ruins, Ship, Mansion, Safari, Ice Cave, Facility, Desert Ruin…) and trainer classes — the MVP's deliberate variety cuts get paid back here, then Post-MVP features (GDD §12) become the backlog.

**Exit criterion:** a stranger can install, generate, and play a region to the Champion with no explanation and no silent-audio moments — shippable as a v1 fan release.

---

## Cross-cutting rules

- **Testing:** domain logic gets unit/fuzz coverage as it's written, never retrofitted; the invariant suite and battle reference suite run on every change to their libraries.
- **Determinism:** all generation randomness flows from the seed through explicit RNG streams (no wall-clock, no global RNG); generator version integer bumps on any generation-affecting change (GDD §11).
- **The GDD, CONTEXT.md, and ADRs are living documents** — any decision that changes them changes them in the same commit.
