# ProcPoke — Ubiquitous Language

Glossary of domain terms. Definitions only — no implementation details.

## Terms

### Bake
The build-time transformation of pinned upstream Pokémon data into ProcPoke's committed game data. Deterministic: the same source pin and bake-tool version always produce identical output.

### Data Manifest
Metadata written alongside baked data recording the upstream source pin and bake-tool version. A test asserts it matches the pin in code, so a stale or drifted bake fails the build.

### Canonical Ruleset
The single generation whose mechanics define every inherited system: **Gen 5 (Black/White 2)**. Damage formula, catch-rate formula, XP/growth curves, stat model (IVs/EVs/Natures/Abilities), physical/special split, critical hits, status effects, and the 17-type chart (no Fairy) are all implemented to Gen 5 spec. Distinct from the **Roster Cap** — the player-configurable maximum generation species may be drawn from (default Gen 5), which filters the species pool but never changes the mechanics.

### Roster Cap
Player-set generation limit (Gen 1–5) on which species can appear in a generated region. Affects the species pool only; the Canonical Ruleset always applies.

### Link Cable
A consumable item that substitutes for trading as an evolution trigger: used on a trade-evolving species, it evolves it immediately. Held-item trade recipes (e.g. Onix + Metal Coat) still require the species to hold that item when the cable is used. Exists because ProcPoke has no trading; every other evolution method works per Gen 5 data unchanged.

### Attachment Operation
One of the fixed set of moves the topology pass applies to grow a region beyond its backbone: insert transit dungeon, attach destination dungeon, attach dead-end branch, add loop-back edge. Each has a quota scaled to region size; when quotas are spent the graph is valid by construction.

### Logical Tile
The gameplay-semantic vocabulary carvers emit (Ground, Wall, Water, TallGrass, Ledge, CutTree, Boulder, …) plus markers (spawn zones, trainer posts, warps, gate anchors). Carries meaning only, never art. The contract shared by carvers, invariant tests, the debug renderer, and overworld collision.

### Tile Realizer
The presentation-side component that turns a Logical Tile grid plus biome into rendered art tiles (tileset choice, autotiling, variants). All aesthetics live here; none upstream.

### Size Class
A per-archetype footprint tier (route S/M/L, town by importance, dungeon floors) assigned by the topology pass, anchored to mainline map dimensions. Bigger regions get more areas, never bigger ones.

### Naming Motif
The per-seed theme family (colors, flora, minerals, weather, …) from which all of a region's location names are blended, mirroring mainline's per-region naming (Kanto = colors, Johto = plants). Names are built from curated word-part pools within the motif and checked against the canon-name blocklist baked from the pinned data.

### Portable Obstacle
An obstacle class that needs only a few tiles, not a biome — cut trees, boulders, cracked rocks, sleeping Pokémon, guardians, bike roads, dark passages. Realized as a **Terrain Inset** (a small terrain pocket — pond, outcrop, tree cluster) carved into whatever biome its area has; places no constraint on biome assignment.

### Terrain-Bound Obstacle
An obstacle class that genuinely requires its terrain — surfable water, waterfalls, dive spots, whirlpools, sandstorm desert, sea crossings. Emits a **Biome Requirement** on its area.

### Battle Event
One typed entry in the ordered stream a battle turn resolves to (`MoveUsed`, `DamageDealt`, `Fainted`, …). The contract between the battle simulation, its tests, and the battle UI — the UI replays events; it never inspects sim state.

### AI Tier
A trainer's battle intelligence level. Tier 0 (wild): uniform random legal move. Tier 1 (standard trainer): best-damage choice, hard-avoids immunities, ~20% second-best imperfection. Tier 2 (boss): tier 1 without imperfection, plus potion-heal below ~25% HP and at most one hopeless-matchup switch per battle. Difficulty comes from team composition and levels, never from deeper AI.

### Biome Requirement
A tag placed by the gate pass on an area ("water-dominant", "desert"), consumed by the later biome pass as a fixed point it grows the coherent biome map around. At most one terrain tag per area — obstacles that would conflict are filtered from the draw before selection, so contradictions are unrepresentable.

### Region Graph
The abstract, seed-generated structure of a region: areas (routes, cities, dungeons) as nodes, connections as edges, with gates, keys, and biomes assigned. Exists before — and independently of — any tile-level map.

### Gate
An obstacle placed on a connection or passage that blocks progress until its Key is used. Every Gate sits on a Chokepoint, so it cannot be walked around.

### Key
The move or item that removes exactly one class of Gate (e.g. Cut removes cut trees). Keys are acquired off the main Spine, never handed out on it.

### Special Encounter Overlay
A Gen 5-style rare-spawn signal layered over ordinary encounter terrain — Rustling Grass, Dust Clouds, Rippling Water, Dark Grass. Grants rarer/boosted encounters and is the **only** source of Hidden Abilities.

### Hidden Ability
A species' rare third ability (Gen 5 Dream World / Hidden Grotto data). Rolled only on Pokémon met via a Special Encounter Overlay (~50% chance there); ordinary encounters always use a regular ability slot.

### Professor
The per-seed framing NPC: a generated identity (tree-derived name minus canon names, rolled gender, lab in the starter town) who delivers the opening monologue, gives the starter, and sets the fixed mainline premise (fill the dex, take the gym challenge). Only the identity varies; ProcPoke generates no plot arcs at MVP.

### Champion Roll
The per-seed casting of the League Champion: by default a generated NPC, but ~25% of seeds instead make the **Rival** the Champion (foreshadowed at the pre-League beat). Rival-Champion seeds weight the Underdog rival archetype higher. Reuses existing rival-team and difficulty machinery — a casting decision, not a mechanic.

### Services Pass
The generation pass that assigns one-off city services (Move Deleter, Name Rater, fossil revival, Link Cable vendor, move tutors) to generated cities, 0–2 per city, under per-service placement constraints (fossil revival near the fossil cave, Move Deleter by mid-game, etc.). Every service is guaranteed present somewhere per seed.

### Second Region
The Phase 7 post-game feature (Gen 2 Kanto homage): after the Champion, the player may generate a second region from a new sub-seed, merged with the first. Its own dex (novelty-preferring), starter (unused triangle), gyms, and villain thread; no second Elite Four — a **Summit** boss instead. Regions stay separate map spaces joined by a **Region Link**.

### Region Link
The physical join between the two regions, decided by region 1's geography: a **ship** crossing if region 1 has a coastal city (flagged as port), otherwise a **border pass** (a generated mountain-pass transit route with warp edges). Blocked by a League guard until the Champion flag sets. Fly networks stay local to each region; crossing is always the link.

### Level Cap
Region 2's per-badge challenge rule (badge obedience reborn with purpose): Pokémon above the current cap cannot be **fielded** (marked resting, not selectable) and stop gaining XP at the cap (excess discarded) until the next badge raises it. Catching is never capped. Each region-2 badge sets the cap ~+3 above that gym's ace level.

### Summit
Region 2's finale in place of a second Elite Four: a short Victory-Road-style gauntlet ending in one Red-analog battle (~Lv 70s, full six). The boss is **whoever the player did not face for the region-1 title** — the Rival at their peak in normal seeds, the deposed former Champion in rival-Champion seeds.

### Seed String
The shareable identity of a region: raw seed + generation settings + generator version, encoded as one string. Determinism is promised only within a generator version — an older Seed String warns it may produce a different region. Save files never depend on it (they store the fully generated region).

### Spine
The mandatory walkable path through an area — generated first, as the skeleton every map is carved around. Regionally, the critical path Route 1 → City 1 → … → League.

### Chokepoint
A point on a Spine that the geometry guarantees is the only passage; the only legal placement for a Gate.

### Field Move (HM)
A move-type Key. Taught permanently from an HM item (removable only via the Move Deleter), occupies a party moveslot, and performs an overworld action that removes its Gate class. ProcPoke defines its own HM roster — a superset drawn from across Gens 1–5 (Cut, Surf, Strength, Rock Smash, Waterfall, Flash, Defog, Rock Climb, Dive, Whirlpool) — since no single generation's HM list covers the obstacle table. Which subset exists is decided per seed by obstacle spawn rolls. Field use of each HM additionally requires a **Badge Prerequisite** — a per-seed-assigned badge, chosen so it is always earnable at or before the HM's earliest Gate; Key Items carry no Badge Prerequisite.

**Fly** is the one HM that is not a Key: it spawns in every seed as a pure travel utility (fast travel to visited cities via the region map), gates nothing, and is ignored by solvability rules. It follows all other HM conventions — off-spine placement and a mid-game Badge Prerequisite.

### Gate Budget
The target number of Gates in a region, computed from region size (roughly one per 1–1.5 route segments), not fixed and not 1:1 with badge count. Slots are filled from the obstacle table: 100%-chance classics first as guaranteed picks, the rest drawn using the table's percentages as relative weights.

### Gate Timing
When a Gate appears, expressed as a fraction of critical-path progression (e.g. Surf at 30–50%), never as an absolute ordinal — so timing holds at any badge count.

### Hint NPC
A generated NPC whose dialogue names the location of a specific Key. A generation invariant, not decoration: every Gate has at least one Hint NPC reachable before the player first hits that Gate, placed in the adjacent town or route. Distinct from flavor NPCs (generic dialogue) and furniture NPCs (Gym guide, Center gossip, signs).

### Map Connection
A seamless outdoor edge between two adjacent areas (route↔route, route↔town): the neighbor map scrolls into view with no transition, mainline-style. Requires the two carvings to agree on edge width and position (edge alignment). Interiors, caves, and dungeons connect by warp (fade) instead.

### Key Item
An item-type Key (Bicycle, Poké Flute-equivalent, Go-Goggles-equivalent, Ship Ticket, scope, squirtbottle-equivalent, relic pair). Grants its effect from the bag; never occupies a moveslot.

### Transit Dungeon
A special location the critical path runs through — the player enters one side and exits the other (forest, connecting cave).

### Destination Dungeon
A special location that is not on the way to anywhere; the player enters to retrieve something (a Key, fossil, legendary) and leaves the way they came.
