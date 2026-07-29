# ProcPoke — Game Design Document

**Genre:** 2D top-down monster-catching RPG
**Style Reference:** Pokémon Generations 1–5
**Status:** Draft v0.2 — all v0.1 open questions resolved (design review, July 11, 2026)
**Last Updated:** July 11, 2026
**Companion documents:** `CONTEXT.md` (glossary of domain terms), `docs/adr/` (architectural decision records)

---

## Table of Contents

1. [Overview](#1-overview)
2. [Design Pillars](#2-design-pillars)
3. [Core Gameplay Systems (Inherited from Mainline)](#3-core-gameplay-systems-inherited-from-mainline)
4. [Procedural Region Generation](#4-procedural-region-generation)
5. [Pokémon Roster & Regional Pokédex](#5-pokémon-roster--regional-pokédex)
6. [Biomes & Wild Encounters](#6-biomes--wild-encounters)
7. [Trainers, Rival & Villain Team](#7-trainers-rival--villain-team)
8. [World & Location Naming](#8-world--location-naming)
9. [User Interface / Screens](#9-user-interface--screens)
10. [Data Sourcing](#10-data-sourcing)
11. [Save System](#11-save-system)
12. [Post-MVP Features](#12-post-mvp-features)
13. [Resolved Design Questions](#13-resolved-design-questions)
14. [Technology & Development Plan](#14-technology--development-plan)
15. [Second Region (Post-Game Expansion)](#15-second-region-post-game-expansion)

---

## 1. Overview

ProcPoke is a 2D top-down monster-catching RPG built in the mechanical and visual style of Generation 1–5 Pokémon games. Every core system — battling, catching, leveling, gyms, and the underlying Pokémon data itself (stats, movepools, move effects) — is a faithful clone of the mainline formula. Nothing about those systems is being reinvented.

The single new pillar is **the region**. Every new game procedurally generates a complete, seed-based world: routes, towns, dungeons, Pokémon spawns, trainers, gym leaders, a rival, and a villain team. The generated region should still *feel* hand-crafted — including the same style of item/HM gating and hidden progression puzzles that make exploring the original games satisfying.

**Elevator pitch:** *Pokémon, but the region is different every time you start a new game.*

**Target experience:** the player should recognize every individual system instantly (battle math, catch mechanics, the type chart), while never being able to predict the map. Progression should feel like a classic Pokémon game — gyms punctuating a path that's gated by items/moves earned inside non-obvious side dungeons — forcing exploration and occasional backtracking, never a straight line, but never unfair either.

## 2. Design Pillars

**Cloned 1:1 from mainline (no redesign):**
- Battle system, damage formula, full type chart
- Catching mechanics (Poké Balls, catch-rate formula)
- XP / leveling curves
- Species stats, movepools, and move effects
- Abilities, Natures, and evolution methods
- Gym / Badge structure
- Elite Four → Champion → Victory Road
- Pokémon Centers, Marts, and PC storage

**Procedurally generated per seed (the new layer):**
- Region topology — routes, towns, dungeons, and their order
- Biome placement
- Wild encounter tables per area
- Trainer rosters and placement
- The gating puzzle graph — which obstacle blocks which area, and where its key lives
- Starter trio — type-triangle template and BST balancing
- Regional Pokédex — 150 species + 4 legendaries
- Rival identity and villain team identity
- Location names — cities, routes, dungeons
- Fossil placement

## 3. Core Gameplay Systems (Inherited from Mainline)

**Canonical Ruleset: Gen 5 (Black 2/White 2).** "Gen 1–5" was never one ruleset — mechanics changed every generation — so all inherited systems are implemented to Gen 5 spec exactly: damage formula, catch formula, XP curves, IV/EV/Nature/Ability model, physical/special split, critical hits, status mechanics, and the 17-type chart. "Gen 1–5" elsewhere in this document refers to *content* (the species/roster pool), never to rules.

**Documented deviations from Gen 5** (each deliberate, decided in review):

- **Trade evolutions → Link Cable item.** No trading exists; a consumable Link Cable evolves trade-evolvers (held-item recipes like Onix + Metal Coat still require the held item).
- **Seasons: cut.** Time-of-day (real system clock) is kept — it's load-bearing for evolutions (Espeon/Umbreon, Gliscor, Weavile, Chansey) and may modify some encounter tables — but the Gen 5 season system is out entirely.
- **Breeding stack: Post-MVP.** Eggs, inheritance, egg moves, and the Daycare are deferred (Section 12). Baby species spawn as rare wild encounters in low-level biomes, so dex completability is unaffected.
- **Singles only.** Doubles is Post-MVP; triples and rotation battles will never be implemented. Multi-battle-only moves/abilities remain in the data with their (near-useless) singles semantics, as in mainline.
- **Multiple save slots** (Section 11) — an addition, not a cut.
- **Badge obedience: cut.** It exists solely to police traded Pokémon; with no trading it polices nothing.
- **Alternate forms: base forms only.** Form variants (Rotom appliances, Deoxys formes, in-battle transformations, seasonal Deerling — fixed to spring) are dropped at data-bake; a form system is Post-MVP.
- **Hidden abilities via Special Encounter Overlays.** No Dream World exists; a Pokémon met through a rare-spawn overlay (Rustling Grass, Dust Clouds, Rippling Water, Dark Grass) has a ~50% chance of its hidden ability — ordinary encounters never roll one. (The B2W2 Hidden Grotto idea, translated.)

Any Gen 5 rule not explicitly contradicted above or elsewhere in this document carries over unmodified:

- Battling — singles, wild & trainer encounters, Speed/priority turn order, status effects, critical hits
- Catching — Poké Balls, catch-rate formula, shake checks
- Leveling & XP — growth-rate groups per species
- Full stat model — HP/Atk/Def/SpA/SpD/Spe, base stats, IVs, EVs, Natures, Abilities
- Full move data — power, accuracy, PP, secondary effects, per-species learnsets (Gen 5 / B2W2 snapshots)
- Type chart (Gen 5 rules; 17 types, no Fairy)
- Evolution (level, item, friendship, time-of-day, etc., per species data; trade → Link Cable)
- Gyms & Badges (default 8, configurable — see [Section 9](#9-user-interface--screens))
- Elite Four → Champion → Victory Road endgame structure
- Healing (Pokémon Centers), shopping (Marts), box storage (PC)

## 4. Procedural Region Generation

This is the core feature of the game. Everything else in this document exists to support it.

### 4.1 Generation Parameters

Set by the player on the [Generation Screen](#91-region-generation-screen) before a new game begins:

- Number of available Pokémon (default **150**)
- Maximum Pokémon generation included (default **Gen 5**)
- Number of cities / badges (default **8**)
- Seed (random by default; can be entered manually to reproduce or share a region)

### 4.2 Region Topology

- The region is a chain of **Routes** and **Cities/Towns** connected in critical-path order (Route 1 → City 1 → Route 2 → … → League) — the same backbone shape as a mainline region.
- **Dungeons / Special Locations** (Section 4.4) are interspersed along and off this backbone. Design mandate: the player should never pass through more than **two consecutive plain routes/cities** without hitting at least one dungeon or special location that breaks up the flow.
- Branch points, optional detours, dead ends with rewards, and loop-backs (an area that only opens up once a later key is obtained) are required in every seed — the map should never be a single unbranching hallway.

**Topology growth (constructive, ADR-0004 pipeline).** The backbone chain is built first; the region then grows by quota-driven **attachment operations** — insert transit dungeon (driven directly by the ≤2-plain-areas pacing rule), attach destination dungeon (sized to the expected Gate Budget plus reward/fossil/legendary sites), attach dead-end branch, add loop-back edge (quota ≥1). Quotas scale with badge count; when they're spent the graph satisfies §4.2's requirements by construction. Every area receives a per-archetype **size class** with mainline-anchored footprints (routes ~20×30 to 30×60 logical tiles, towns 25²–40², cave floors ~30²) — a bigger region means more areas, never bigger ones; each route carries one spine idea, mainline-style.

**Map realization — constraint-first carving (ADR-0001).** Generation is two-layer: the abstract **Region Graph** (topology, gates, keys, biomes) is generated first; each node is then realized as tiles by an archetype-specific *carver*. Every map is fully procedural — no hand-built chunk/template library — but carved, never sampled: the walkable spine is drawn first, gates are placed on verified chokepoints as hard geometric guarantees, and decoration (trees, ledges, grass, trainers, item nooks) is applied outward under placement rules that enforce hand-crafted feel by construction (trainer sightlines face the path, ledges drop toward the entrance, tall grass straddles the mandatory path).

### 4.3 The Gating System (Lock-and-Key Design)

The defining puzzle layer of the game. An obstacle ("gate") blocks forward progress; a move or item ("key") removes it. Rules for this system:

1. **Every seed must be provably solvable.** Before a region is finalized, the generator verifies a valid order exists to acquire every key before it's needed. If no valid order exists, the seed is regenerated or patched.
2. **Keys live off the main spine** — typically at the end of a dedicated special location: a hideout you must battle through, a puzzle cave, a reward for finishing a gauntlet — rather than being handed to the player automatically.
3. **No circular locks.** The path to a key must always be reachable without needing that same key.
4. **Difficulty escalates** along the timing column below, with timings read as **fractions of critical-path progression**, not absolute ordinals ("3rd–5th gate" of a default 8-badge region ≈ 30–50% progression) — so the system holds at any configured badge count.
5. **Gate count is decoupled from badge count.** The generator computes a **Gate Budget** from region size (roughly one gate per 1–1.5 route segments; ~9–10 at the default 8 badges) and fills it from the table: 100%-chance classics are guaranteed slots first, the rest drawn using the listed percentages as relative weights. Exotic low-weight obstacles therefore tend to appear only in larger regions.
6. **Move-keys are classic permanent HMs.** Taught from HM items, occupying a party moveslot, removable only via the Move Deleter. ProcPoke defines its **own HM roster** — a cross-generation superset (Cut, Surf, Strength, Rock Smash, Waterfall, Flash, Defog, Rock Climb, Dive, Whirlpool), since no single generation's HM list covers this table. Item-keys stay ordinary key items.
7. **Badge-gated field use.** Each spawned HM is assigned a per-seed badge prerequisite consistent with its gate timing (always earnable at or before the HM's earliest gate). Finding an HM early but unlocking its field use later is a deliberate loop-back mechanic. Key items have no badge prerequisite.
8. **Every gate is hinted.** Non-obvious never means unhinted: for every gate, at least one **Hint NPC** — reachable before the player first meets that gate, placed in the adjacent town or route — names the key's location in generated dialogue ("I hear a miner deep in Emberdeep Cave teaches how to smash rocks"). This is a generation invariant checked by the fuzz suite, alongside mainline furniture hints (Gym guides, Center gossip about the villain hideout, route signs).
9. **Fly** spawns in every seed as a pure travel utility — never a key, ignored by solvability rules. Mainline conventions: found off the spine, badge-gated around mid-game (~40% progression), fast-travels to already-visited cities via the region map.

The table below is the master obstacle/key reference, carried over from the original design brief:

| Obstacle | Key (Move / Item) | Spawn Chance | Typical Gate Timing |
|---|---|---|---|
| Cut trees | Cut | 100% | 1st gate |
| Surfable water | Surf | 100% | 3rd–5th gate |
| Boulders | Strength | 100% | 3rd or 4th gate |
| Cracked rocks | Rock Smash | 80% | 2nd–3rd gate |
| Waterfalls | Waterfall | 80% | Late game |
| Dark caves (no light) | Flash | 80% | 2nd–3rd gate |
| Fog | Defog | 20% | — |
| Climbable rock walls | Rock Climb | 20% | — |
| Deep water | Dive | 40% | Late game |
| Sleeping Pokémon blocking a path | Poké Flute (equivalent) | 40% | Mid game |
| Invisible Pokémon | Silph Scope / Devon Scope (equivalent) | 20% | — |
| Bike-only roads | Bicycle | 80% | Early–mid game |
| Acro/Mach-bike-specific terrain | Bike upgrade | 20% | — |
| Sandstorm desert | Go-Goggles (equivalent) | 20% | — |
| Whirlpools | Whirlpool (move) | 40% | — |
| Guardian blocking a path (Sudowoodo-style) | Squirtbottle (equivalent) | 40% | — |
| Sealed tower/shrine | Relic pair (equivalent to Clear Bell / Rainbow Wing) | 40% | — |
| Sea crossing | Ship Ticket | 60% | — |

*Percentages are relative selection weights for filling the Gate Budget (100% = guaranteed slot), carried over from the original brief as the tuning source.*

**Illustrative example (not literal output):**

```
Town A (start)
 └─ Route 1 [gate: Cut] — key: given by an NPC in Town A
 └─ Route 1 (open) → Town B
 └─ Town B → Forest (transit dungeon) → Route 2
 └─ Route 2 [gate: Rock Smash Rubble] — key: reward for clearing
	  "Hideout Alpha" (destination dungeon, optional detour from Town B)
 └─ Route 2 (open) → Town C
```

### 4.4 Special Locations / Dungeons

Every special location fills one of two roles:

- **Transit dungeons** — part of the main path; the player walks through them to reach the next area (a forest, a connecting cave).
- **Destination dungeons** — not on the way to anywhere; the player goes there specifically to retrieve something (a key item, a fossil, a legendary, a defeated villain team) and leaves the way they came in. Many of these should be trainer gauntlets culminating in a boss fight — the "Rocket Hideout" model — rather than pure exploration puzzles.

**Interiors.** Pokémon Centers and Marts use one fixed, hand-authored interior each, reused region-wide — mainline's own convention, and the sole documented exception to the no-hand-built-maps rule (ADR-0001). Houses come from a small parameterized template pool; gym interiors are carved trainer gauntlets (2–4 trainers between door and leader) with type-themed dressing — bespoke gym *puzzles* are Post-MVP. Hideouts, towers, and all true dungeons are carved by their archetypes as usual.

**Connections.** Adjacent outdoor areas join seamlessly (mainline-style scrolling **Map Connections**, requiring edge-aligned carving); interiors, caves, and dungeons connect by warp fades.

The table below is a **taxonomy of archetypes**, organized by role and challenge type. The bracketed examples are drawn from mainline games purely as tone references — per the naming rules in [Section 8](#8-world--location-naming), no generated dungeon may reuse an existing name.

| Archetype | Typically Transit? | Common Challenge | Reference Examples (inspiration only) |
|---|---|---|---|
| Forest | Yes | Wild encounters, simple maze, a hidden item | Viridian Forest, Ilex Forest, Petalburg Woods |
| Standard Cave | Often | Flash/dark-cave gating, wild encounters, branching paths | Mt. Moon, Union Cave, Granite Cave |
| Deep/Post-Game Cave | No | High-level wilds, no trainers, rare-Pokémon reward | Cerulean Cave, Mt. Silver |
| Facility / Power Plant | No | Switch/circuit puzzle, strong Electric/Steel wilds | Power Plant, New Mauville |
| Tower / Spire | No | Story beat, Ghost encounters, culminates in a key item or relic | Pokémon Tower, Celestial Tower, Sky Pillar |
| Villain Hideout | No | Straight trainer gauntlet into a boss fight | Rocket Hideout, Team Plasma Hideout |
| Corporate Building | No | Multi-floor gauntlet, elevator/switch puzzle, boss + item | Silph Co., Galactic HQ |
| Ship | No (usually a destination) | Enclosed gauntlet, sometimes a fossil or legendary | Sea Mauville, Plasma Frigate |
| Ruins / Chamber Complex | No | Environmental puzzle (statues, switches, glyphs), ancient Pokémon | Ruins of Alph, Relic Castle, Tanoby Ruins |
| Mansion / Old Building | No | Item hunt, rare Pokémon, sometimes a fossil | Pokémon Mansion, Old Chateau |
| Safari-style Preserve | No | Special catch rules (no battling, limited turns/balls) | Safari Zone |
| Mountain Path | Usually | Strength/Rock Climb gating, Rock/Ground wilds | Jagged Pass, Twist Mountain |
| Desert Ruin | No | Sand obstacle (Go-Goggles), ancient Pokémon | Relic Castle, Desert Resort |
| Ice Cave | Usually | Slippery-floor puzzle, Ice-type wilds | Icefall Cave, Ice Path |
| Victory Road / League | Yes / No | Endgame gauntlet, highest-level trainers, badge check | Victory Road, Pokémon League |

### 4.5 Solvability & Validation

Solvability is guaranteed **by construction, not by validation** (ADR-0002). The generator walks the critical path in order; when placing gate N, it places gate N's key (and sets its HM's badge prerequisite) only within areas provably reachable *before* gate N given what the player can already have. Circular locks and unreachable keys are therefore unrepresentable.

A validator still runs as an internal sanity assertion, confirming:

- Every gate has exactly one key, and that key is reachable without passing through that same gate.
- No key depends on a gate it itself unlocks (no circular dependencies).
- At least one branch, optional detour, or loop-back exists somewhere in the region.

If the assertion ever fires (i.e., a generator bug), the game silently re-rolls an internal sub-seed, regenerates, and logs diagnostics. The player never sees a failed generation — **Generate** always yields a valid region.

## 5. Pokémon Roster & Regional Pokédex

### 5.1 Pokédex Composition

- **150 species** by default (configurable), plus **4 legendaries**, for a 154-entry regional dex.
- Species are drawn from a Gen 1–5 pool, capped at the configured max generation.
- **Evolution lines stay together** — including a species means including its full line, occupying consecutive dex slots.
- Species are split across biomes (Section 6) so each biome contributes a thematically consistent slice of the dex.
- **Type coverage check:** the selection algorithm tracks running totals across all 17 types (primary and secondary) and biases toward under-represented types, so no type is left with zero representation among wild encounters or trainer options.

### 5.2 Pokédex Numbering

- Dex number = order of first availability along the intended critical path.
- **#1–#9**: the three starter evolution lines (3 lines × 3 stages).
- **#10 onward**: Route 1's population, then City 1's, then Route 2's, and so on, in strict critical-path order.
- **Strength curve:** each line's placement is weighted by its fully-evolved BST — lower final-stage BST lines trend toward early routes, higher BST toward later ones, producing the same soft early-Caterpie-to-late-Dragonite curve as the mainline games.
- **Trainer legality:** any trainer may carry a Pokémon that appears later in the dex than the player currently has access to (that's normal and expected), but never a species that isn't catchable anywhere in the region. Every trainer roster is a subset of the regional dex.

### 5.3 Starter Pokémon

- Three starters, each a separate 3-stage evolution line, final stage capped at **540 BST**, with all three final BSTs kept roughly balanced against each other — no starter should be a trap pick.
- The three types form a closed effectiveness triangle. One template is chosen at random per seed from:

| Triangle | Cycle |
|---|---|
| Classic | Water → Fire → Grass → Water |
| Mind & Body | Fighting → Dark → Psychic → Fighting |
| Elemental | Rock → Flying → Fighting → Rock |

- For the Rock → Flying → Fighting triangle, the "Flying" corner must carry the Flying type (needed for the effectiveness math against Rock/Fighting to hold), but its secondary type is free to be Normal, Bug, Electric, Water, Grass, Poison, or Fire — reflecting how varied "bird" starters have looked across the mainline games.
- Once a triangle is chosen, one 3-stage line is **selected** per corner (ProcPoke never invents species) such that all three lines are distinct and their final BSTs are close together.
- **Starter eligibility:** a corner's pool is every 3-stage evolution line whose final stage carries the corner's type, final BST ≤ 540, within the Roster Cap — not just canonical starters (Krookodile, Alakazam, or Crobat can be a starter; that's the point). One hard filter: the line must be **completable by leveling alone** — lines requiring stones, Link Cables, or friendship to finish are excluded from starter pools (they remain in the regular dex).
- **Triangle availability follows the Roster Cap:** templates with an empty corner pool under the current cap are unpickable (e.g. Mind & Body needs Dark-types, which don't exist at a Gen 1 cap); Classic is always available as the guaranteed floor.

### 5.4 Fossil Pokémon

- **2 fossil Pokémon** per region, drawn from the fossil-eligible pool (the Kabuto/Omanyte/Aerodactyl/Lileep/Anorith/Cranidos/Shieldon/Tirtouga/Archen-style species across Gen 1–5).
- Both fossils are placed inside a single cave-type destination dungeon, typically one themed around the Rock/Ground biome.

### 5.5 Legendary Pokémon

- **4 legendaries** per region, drawn from the Gen 1–5 legendary pool, placed at locations that suit their mythology (a tower, ruins, a remote cave — see the archetype table in [Section 4.4](#44-special-locations--dungeons)).
- **All four are static encounters — no roaming.** Each is a visible overworld encounter at the deepest point of its destination dungeon; battle starts on interaction; one per region. Per the B2W2 convention, a legendary that was KO'd or fled from respawns after the player's next Elite Four victory, so none is ever permanently lost.
- Level and placement scale with progression: 1–2 reachable pre-League behind late-tier gates (Waterfall/Dive), the rest tuned as post-game content in deep/post-game archetype dungeons.
- Legendaries are always optional — never a Key or story requirement; the solvability graph ignores them.

### 5.6 Type Balance

The species-selection algorithm tracks running type counts across the full 150+4 pool and applies a soft bias toward under-represented types, so the region never ends up starved of, say, Ice-types, or oversaturated with Water-types.

## 6. Biomes & Wild Encounters

### 6.1 Biome List & Map Frequency

Species counts and frequencies below are estimates carried over from the original games as a tuning starting point — the generator isn't required to match them exactly.

| Biome | Reference Species Count | Suggested Map Frequency |
|---|---|---|
| Tall Grass / Grassland | 20 | 13.4% |
| Forest | 12 | 8.1% |
| Fishing (Old/Good/Super Rod) | 12 | 8.1% |
| Cave | 10 | 6.7% |
| Surfable Water | 10 | 6.7% |
| Mountain / Rocky Terrain | 8 | 5.4% |
| Swamp / Marsh | 8 | 5.4% |
| Dark Grass (special encounters) | 8 | 5.4% |
| Underwater (Dive) | 6 | 4.0% |
| Volcanic / Hot Areas | 6 | 4.0% |
| Safari-style Preserve | 6 | 4.0% |
| Desert / Sand | 5 | 3.4% |
| Ice / Snow | 5 | 3.4% |
| Power Plant / Industrial | 5 | 3.4% |
| Tower / Graveyard | 5 | 3.4% |
| Headbutt Trees | 4 | 2.7% |
| Rock Smash Rubble | 4 | 2.7% |
| Rustling Grass | 4 | 2.7% |
| Dark Cave / Deep Cave | 3 | 2.0% |
| Dust Clouds | 3 | 2.0% |
| Rippling Water | 3 | 2.0% |
| Ancient Ruins | 2 | 1.3% |

### 6.2 Biome → Encounter Design Rules

Since species are randomized per seed, biomes are defined by **type bias and role**, not fixed species — the generator slots whichever seed-specific species fit each biome's bias. This is the table requested for "which Pokémon make sense in which biome," expressed at the level the procedural system actually operates on:

| Biome | Typical Type Bias | Notes |
|---|---|---|
| Tall Grass / Grassland | Normal, Grass, Bug, Flying | Baseline early-game biome; widest variety, lowest average BST band |
| Forest | Bug, Grass, Poison, Normal | Denser encounters than open grass; early evolution-item lines |
| Fishing | Water | Split across Old/Good/Super Rod tiers by BST/rarity |
| Cave | Rock, Ground, Poison, Dark | Pairs with Dark Cave/Flash gating |
| Surfable Water | Water (Ice in cold biomes) | Mid-game route connector |
| Mountain / Rocky Terrain | Rock, Ground, Fighting, Flying | Pairs with Strength/Rock Climb gating |
| Swamp / Marsh | Water, Poison, Bug, Grass | Slower, bulkier statlines |
| Dark Grass (special) | Any, boosted rarity/level | Small pool of rarer per-biome specials, Gen 5-style |
| Underwater (Dive) | Water, Rock | Late-game only, higher BST band |
| Volcanic / Hot Areas | Fire, Rock, Ground | Paired with Waterfall/late-game gating |
| Safari-style Preserve | Wide mix, biome-agnostic rarities | Special catch rules apply |
| Desert / Sand | Ground, Rock, Dark, Fire | Pairs with Go-Goggles gating |
| Ice / Snow | Ice, Water, Normal | Late-game biome |
| Power Plant / Industrial | Electric, Steel | Facility-type destination dungeon |
| Tower / Graveyard | Ghost, Psychic, Normal | Story-driven destination dungeon |
| Headbutt Trees | Bug, Normal, Grass | Optional layer over existing Forest/Grassland tiles |
| Rock Smash Rubble | Rock, Ground, Fighting | Overlaps with Cave/Mountain |
| Rustling Grass | Any, boosted rarity | Rare-spawn signal overlaid on Grassland tiles |
| Dark Cave / Deep Cave | Dark, Ghost, Rock | Requires Flash; higher-rarity subset of the Cave pool |
| Dust Clouds | Ground, Rock, Steel | Overlaid on Desert tiles |
| Rippling Water | Water, higher rarity | Rare-spawn signal overlaid on Surf tiles |
| Ancient Ruins | Rock, Psychic, Dragon, Ghost | Home for part of the legendary/fossil placements |

### 6.3 Encounter Table Structure

Encounter tables clone the **Gen 5 slot model** rather than free-form weights: each encounter method has a fixed slot layout with canonical percentages (tall grass/cave: 12 slots at 20/20/10/10/10/10/5/5/4/4/1/1; surf: 5 slots at 60/30/5/4/1; fishing per rod tier), and the generator fills slots from the area's biome pool by rarity tier — a species' rarity *is* which slots it occupies, so 1%-slot chase targets exist in every area by construction. Encounter rates (steps-per-encounter) likewise use Gen 5's per-terrain values. Special Encounter Overlays sit on top as their own small tables and are the only source of hidden abilities.

**Wild levels** per area ≈ (next gym's ace level − 5) ± 2, riding the difficulty curve in §7.1; destination dungeons run ~+2, post-game areas above Champion level.

## 7. Trainers, Rival & Villain Team

### 7.1 Trainer Classes & Pokémon Assignment

As with encounters, trainer classes are defined by type bias and strength tier rather than fixed species — this is the requested "trainer types and assign them Pokémon" table, at the abstraction level a seed-agnostic system needs:

| Trainer Class | Biome Tie-in | Type Bias | Strength Notes |
|---|---|---|---|
| Youngster / Lass | Grassland, early routes | Normal, mixed low-tier | Early game, 1–2 mons |
| Bug Catcher | Forest | Bug | Early game |
| Hiker | Mountain / Cave | Rock, Ground, Fighting | Early–mid, bulky |
| Swimmer | Surf routes / coast | Water | Mid game |
| Fisherman | Fishing spots | Water | Mid game, status/evasion-leaning sets |
| Psychic | Tower / Ruins | Psychic, Ghost | Mid–late game |
| Gentleman / Lady | Cities | Mixed, higher-rarity picks | Mid–late game, flavor battles |
| Biker | Industrial / Power Plant | Poison, Electric | Mid game |
| Skier-type | Ice / Snow | Ice, Fighting | Late game |
| Ace Trainer | Anywhere, recurring | Broad, no fixed bias | Scales continuously with route progression |
| Veteran-type | Late routes, Victory Road | Broad, high-BST picks | Pre-League gauntlet difficulty |
| Team [Villain] Grunt | Hideouts, certain routes | One or two thematic types | Recurs throughout — see 7.3 |
| Gym Leader | End of each city (1 per badge) | One type per leader | Team size scales with badge order (2→6 mons) |
| Elite Four / Champion | League | One type each (E4), broadest/strongest (Champion) | Highest BST band, full 6-mon teams |

- Every trainer roster is a subset of the regional dex (Section 5.2) — trainers may use species "ahead" of the player's current progress, never species absent from the region.
- **Difficulty curve** (all levels expressed against progression fraction, so any badge count works): gym ace levels run ~14 at the first badge to ~50 at the last, interpolated smoothly; Elite Four ~54–58, Champion ~59–60. Trainer aces ≈ their area's wild level + 2–4; gym trainers sit between route trainers and their leader; the rival tracks the player-facing curve at each beat; villain bosses match gym-equivalent strength for their timing. Starters begin at level 5. (Badge *obedience* is cut — it exists only to police traded Pokémon, and there is no trading.)
- **Gym types are distinct** within a region — no repeats — selected with **biome affinity** (the §6.2 bias tables read in reverse: a mountain-ringed city leans Rock/Ground/Fighting, a port leans Water), so gyms feel placed rather than rolled. Thin types in the drawn dex pad their gym with dual-types and adjacent picks, mainline-style. Team size scales 2→6 along the curve.
- **Elite Four** types are distinct from each other and from the gyms while the type count allows (always true at default settings); full 6-mon teams. The **Champion** is typeless: the broadest, highest-BST team in the region, traditionally featuring a starter line's final stage or a pseudo-legendary if the dex drew one. Villain-team thematic types are chosen away from gym-type collisions when possible.
- **Champion identity (per-seed roll):** by default the Champion is a generated NPC (own name, title dropped by NPC gossip during the run). In ~25% of seeds the **rival is the Champion instead** — foreshadowed at the pre-League beat, the League's final door opening on a familiar face. Rival-Champion seeds weight the Underdog archetype (7.2) higher; the pairing is otherwise free. Reuses existing rival-team and difficulty machinery.

### 7.1a Framing Narrative (Professor & Premise)

- A **generated professor** per seed: name from the mainline tree convention (curated pool — Aspen, Laurel, Hawthorn, Linden… — minus canon names), gender rolled, lab in the starter town. Delivers the opening "welcome to the world of Pokémon" monologue (showing a wild species from the region's own dex), gives the starter from three Poké Balls with the rival present and picking second — the exact Gen 1–5 beat.
- The **premise is the mainline default**: fill the Pokédex, take the gym challenge. No generated plot arcs at MVP — the villain-team arc (7.3) is the story's middle, the League its end. Only identities vary; the frame is fixed.

### 7.2 Rival

- Generated once per seed, appearing at the same recurring beats as a mainline rival: after the starter choice, on an early route, at the midpoint city, and before the Elite Four.
- The rival's starter is auto-selected as whichever of the two remaining trio members is strong against the player's pick, following the standard "rival picks your counter" convention.
- Each seed assigns the rival one personality archetype, kept consistent across every encounter (dialogue tone, team-building flavor). Suggested starting archetype set (expandable):
  - **The Cocky Rival** — arrogant, competitive, provokes the player
  - **The Friendly Rival** — supportive, frames battles as a partnership
  - **The Brooding Rival** — mysterious, sparse dialogue, quietly intense
  - **The Underdog Rival** — starts weak, visibly grows in confidence over the game

### 7.3 Villain Team(s)

- **1–2 recurring villain organizations** per region (mainline reference: Rocket, Magma, etc.).
- Each is generated fresh per seed: a name, a thematic motif (type- or ideology-driven), and a grunt roster matching that theme.
- The team recurs across multiple hideouts (destination dungeons, Section 4.4) that gate key items behind a grunt gauntlet and a boss fight — the direct implementation of the "gate key lives in a hideout" rule from Section 4.3.
- If two teams are generated, they are **fully independent story threads** — each gets its own hideout chain, gauntlets, boss, and key-item custody, generated exactly as if alone. The only awareness is free flavor: the theme generator may pick opposed motifs, and grunt dialogue may name-drop the other team from generic templates. Structurally coupled crossover arcs are Post-MVP (Section 12).

## 8. World & Location Naming

- A name generator produces a unique name for every city, route, and dungeon per seed.
- **Hard rule: no generated name may match an existing location name from any official Pokémon game.**
- **Method:** each seed draws a **naming motif** (colors, flora, minerals, weather, light, …) — mirroring mainline's per-region convention (Kanto = colors, Johto = plants) — and blends names from curated word-part pools within that motif: motif root + settlement suffix (-burgh, -port, -vale, -ton) for cities, biome-aware forms for dungeons ("Emberdeep Cave," "Palegrove Forest"). One motif per region gives mainline's subliminal coherence; curated parts keep results pronounceable by construction.
- The blocklist is **baked for free from the pinned PokeAPI data** (its location tables contain every official game's location names) as `name_blocklist.json`; generated names failing it, or colliding within the region, redraw. Villain team names come from the same machinery, checked against the (short, hardcoded) list of canon team names.
- Routes keep the mainline numbering convention (Route 1, Route 2, …) in critical-path order; cities and dungeons receive generated proper names.

## 8a. Economy & City Services

**Economy (cloned tables, no new design).** Gym leaders award badge + prize money + a **TM fitting their type** (the mainline triple). Trainer payouts = per-class base rate × ace level. Whiteout costs money by the Gen 5 formula (scaled to badges/level, never items). **Mart stock unlocks in tiers keyed to badge count** — better balls and medicine appear as the player progresses, driven by the §7.1 curve.

**Services pass.** One-off service NPCs (Move Deleter, Name Rater, fossil revival, Link Cable vendor, move tutors) are placed by a generation pass: each city rolls 0–2 services under per-service placement constraints — fossil revival binds to the city nearest the fossil cave; the Move Deleter appears by mid-game (the HM escape hatch, needed before HM-slot regret gets expensive); Link Cables stock in late-game Marts plus one guaranteed earlier vendor; Name Rater anywhere. Every service is guaranteed present somewhere per seed, and hint NPCs (§4.3) may point to services in neighboring towns.

## 9. User Interface / Screens

### 9.1 Region Generation Screen

Before starting a new game, the player sets:

- Number of available Pokémon (default 150)
- Max Pokémon generation included (default Gen 5)
- Number of cities/badges (default 8)

The player can hit **Generate** repeatedly to preview different seeds. Toggling **Public Pokédex** reveals the full regional dex alongside the map preview; leaving it off previews only the map layout for a blind playthrough. Once satisfied, **Enter Game** locks in the seed and starts the save file.

### 9.2 Controls & Movement

Grid-locked movement with Gen-standard speed tiers: walk, run (hold B, available from the start per Gen 5), Bicycle (key item), Surf. Ledge hops, no diagonals. Input actions: move (arrows/WASD/d-pad), A = confirm/interact (Z/Space), B = cancel/run (X), Start = pause menu (Enter) — controller bound to the same actions; rebinding arrives with the options menu. Player picks one of the two standard characters at new game and enters a name (generated default). Interaction is face-and-press-A everywhere; gates prompt mainline-style when a party member knows the field move and the badge allows.

### 9.3 Overworld Map Screen

Shows the generated region's layout — routes, cities, dungeons, and their connections — available both in the generation preview and as an in-game pause-menu map.

### 9.4 Pokédex Screen

Standard seen/caught dex UI, populated in the regional numbering order defined in [Section 5.2](#52-pokédex-numbering).

## 10. Data Sourcing

All Pokémon-intrinsic data — species base stats, movepools, move effects/power/accuracy, the type chart, abilities, and evolution methods — is imported, not re-authored. Only the region itself — map layout, spawns, trainers, and names — is procedurally authored. Resolved sources:

- **Game data:** the **PokeAPI / veekun CSV dumps**, filtered to version-group `black-2-white-2` — the only open dataset with faithful per-generation snapshots (modern datasets ship current-gen stats, Fairy typings, and post-Gen-5 move balance). Fetched at a pinned commit SHA and baked into committed per-entity JSON with a drift-guarded manifest (see `docs/DEVELOPMENT-PLAN.md`, Phase 1). Note: no dataset contains move *effect logic* — datasets parameterize effects (power/accuracy/PP/effect ID); the effect implementations are our code (see Section 14).
- **Battle sprites:** the **PokeAPI sprites repo** — Gen 5 Black/White front/back/shiny/animated sets, keyed to the same species IDs as the data.
- **Menu/box icons:** **pokesprite**, also ID-indexed.
- **Cries:** the **PokeAPI cries repo** (`.ogg`, legacy Gen-5-era versions), same ID scheme — data, sprite, and cry form one coherent keyed triple.
- **Overworld tilesets, player/NPC/trainer sprites:** fan-made Gen 4/5-style free-with-credit packs from the Pokémon Essentials community (Eevee Expo).
- **Music & SFX:** fan-made/royalty-free Gen-style packs; no ripped OST. A modest track count (~15) suffices via mainline-style reuse. **Audio is a late build phase** — the game runs silent-with-cries until then.

IP posture: species sprites/cries are Nintendo IP used under fan-game norms — this project is strictly non-commercial.

## 11. Save System

- **Multiple independent save slots** (a deviation from mainline's single save): each slot is a fully independent world — its region, party, and progress — with new-game flow starting from slot selection and no fixed cap beyond disk space. *Within* a slot, mainline conventions hold: one save state, overwrite-on-save, no manual branching (keeping legendaries and one-time events meaningful).
- Once a seed is locked in via **Enter Game**, the save stores the seed **and the fully generated region**, so a save survives any later generator changes untouched.
- **Seed sharing:** the shareable **Seed String** encodes raw seed + generation settings + generator version. Determinism is promised only within a generator version — entering an older Seed String warns that the region may differ rather than failing. No cross-version compatibility switches are maintained.
- **Early provisions for the second region (Section 15):** even at MVP, the save schema reserves space for **multiple regions** plus a region-2 badge/level-cap state, and each region graph keeps one unused **external-connection slot** (the future Region Link point) — so adding region 2 never forces region 1 to regenerate.

## 11a. Post-Game (MVP)

Some post-game exists even at MVP because decided systems reach past the Champion (legendary placement, the B2W2 respawn-on-E4-rematch rule). It is deliberately minimal — **unlocks over existing systems, not new content:**

- Credits roll, then the save resumes at home with a **Champion flag** set.
- The deep/post-game cave (Cerulean Cave archetype, already a destination dungeon in the topology) becomes enterable — its guard steps aside; inside are the region's highest-level wilds and typically a legendary.
- **Elite Four rematch** enabled at +10–15 levels (pure reuse of existing teams + curve), which is what makes the legendary respawn rule functional.
- **Dex-completion diploma** from a professor visit — the dex premise's payoff.
- **The second-region offer** (Section 15) becomes available.
- Explicitly **not** at MVP (all Post-MVP): trainer rematches, battle facilities, roaming encounters, new areas beyond those already generated.

## 12. Post-MVP Features

Explicitly out of scope for the initial build:

- Secret Bases
- Berry growing
- Apricorns
- Honey trees
- Headbutt trees as an interactive side-activity (distinct from their role as an encounter-table entry in Section 6)
- **The breeding stack** — eggs, IV/nature inheritance, egg moves, the Daycare (including its leveling service); baby species spawn as rare wild encounters instead
- **Doubles battles** (triples and rotation: never)
- **Seasons** (time-of-day is in scope; the Gen 5 season system is not)
- **Villain-team crossover arcs** (two generated teams stay structurally independent at MVP)
- **Long-tail move-effect fidelity** — bespoke oddball moves beyond the MVP fidelity gate (Section 14)
- **Alternate-form system** — Rotom appliances, Deoxys formes, in-battle transformation forms (Castform, Zen Mode, Meloetta)

## 13. Resolved Design Questions

All v0.1 open questions were resolved in the July 11, 2026 design review and folded into the sections above:

- **Solvability fallback** → dissolved by constructive generation; validation is an internal assertion with silent re-roll (Section 4.5, ADR-0002).
- **Legendary encounter behavior** → all static, B2W2 respawn convention, always optional (Section 5.5).
- **Badge count vs. gate count coupling** → decoupled; Gate Budget scales with region size, timings normalize to progression fractions (Section 4.3).
- **HM-equivalent teaching** → classic permanent HM moves; ProcPoke defines its own cross-gen HM roster (Section 4.3).
- **Badge-gated key use** → yes, per-seed badge prerequisites on HMs, as a deliberate loop-back mechanic (Section 4.3).
- **Seed stability across patches** → determinism per generator version only; saves store the full region and are immortal (Section 11).
- **Two-team interaction** → fully independent threads, flavor-only awareness (Section 7.3).

Additional decisions from the same review: Gen 5 canonical ruleset and its documented deviations (Section 3), constraint-first map carving (Section 4.2, ADR-0001), Fly as a guaranteed non-gating travel HM (Section 4.3), Link Cable for trade evolutions (Section 3), asset/audio sourcing (Section 10), multiple save slots (Section 11), and the technology & build plan below (Section 14, ADR-0003).

## 14. Technology & Development Plan

*Expanded phase-by-phase in [`docs/DEVELOPMENT-PLAN.md`](docs/DEVELOPMENT-PLAN.md) (work items, exit criteria, risks).*

- **Engine/language:** Godot 4.6 (.NET), **C# throughout** (ADR-0003). The battle simulation and region generator are plain C# class libraries with no Godot dependencies — unit-testable without booting the engine (damage/catch formulas verified against known Gen 5 values; generator invariants like gate solvability tested headlessly). Godot scenes are a thin presentation layer.
- **Move-effect strategy:** an effect-archetype engine (each move's data points at an archetype + parameters) covers ~90% of the ~560-move pool. Unimplemented effects **fall back** to plain damage / no-op, logged and flagged in dev builds. The MVP fidelity gate: all archetype-covered moves fully correct, plus hand-implemented tail moves prioritized by actual appearance frequency in generated learnsets (data-driven priority list). True 100% fidelity is Post-MVP.
- **Build order — de-risk by uncertainty, not familiarity:**
  1. **Data pipeline** — bake PokeAPI B2W2 data into game resources.
  2. **Generator, headless** — Region Graph + map carving with debug renders, no gameplay. *Go/no-go milestone: iterate until generated maps genuinely read as hand-crafted.*
  3. **Overworld** — walk the generated region: movement, collision, doors, gates as physical objects.
  4. **Battle engine** — headless and unit-tested, then wired to encounters.
  5. **Loop closers** — trainers, gyms, badges, HM field use, marts/centers/PC, save/load.
  6. **Shell** — generation screen, dex UI, region map, polish; audio last.
  7. **Second region** (Section 15) — post-v1 flagship feature; provisions reserved in Phases 5–6 (save schema + connection slot).
- **MVP definition:** one default-settings region, beatable start → Champion, all core systems present, with reduced *variety* (subset of dungeon archetypes and trainer classes) rather than reduced systems.

## 15. Second Region (Post-Game Expansion)

The flagship post-v1 feature: a Gen 2 Kanto homage. After becoming Champion, the player may generate a **second region** (new sub-seed) merged with the first. Because the generator is region-agnostic, this is a second invocation of the existing pipeline — the expensive machinery is reused wholesale. Built as **Phase 7**, after v1 ships; Phases 5–6 only reserve the two cheap provisions (Section 11).

- **Level caps (the challenge layer — badge obedience reborn with purpose):** in region 2, Pokémon above the current per-badge cap **cannot be fielded** (marked resting, not selectable, framed as a League regulation) and **stop gaining XP at the cap** (excess discarded) until the next badge. Catching is never capped. Each region-2 badge sets the cap ~+3 above that gym's ace. This forces a fresh team from region 2's dex while region-1 champions sit benched until caps climb — fixing Gen 2 Kanto's steamroll problem.
- **Region Link (geography-decided):** if region 1 has a coastal city, it's flagged as a **port** and the link is a **ship** crossing (reusing the Ship Ticket / Ship-archetype machinery); otherwise the reserved external slot attaches a **border pass** (a generated mountain-pass transit route with warp edges between edge cities). Either way blocked by a League guard until the Champion flag sets. The two regions stay **separate map spaces** (own region-map tabs); **Fly networks are local** to each region — crossing is always the link.
- **Dex & starter:** region 2 generates its own 150+4 dex, drawing **preferentially from species unused in region 1** (deep pool at default settings; degrades gracefully to weighted overlap at tight roster caps). The merged Pokédex UI has per-region tabs; catches count once globally. A professor's colleague in the landing town offers a **second starter** from a **triangle template unused by region 1**, drawn from region 2's dex — seeding the fresh-team loop the caps demand.
- **Structure:** region 2 has its own badge count (own mini generation screen, default 8) and full gym machinery (distinct biome-affine types, hint NPCs, caps riding aces). Curve re-anchored ~Lv 12 → 60. **No second Elite Four** — the topology's League slot becomes the **Summit**: a short Victory-Road gauntlet ending in one Red-analog battle (~Lv 70s, full six). The summit boss is **whoever the player did not face for the region-1 title** — the rival at their peak in normal seeds, the deposed former Champion in rival-Champion seeds. Region 2 rolls its own fresh villain thread(s).

---

*This document organizes the original project brief into GDD form. A few pieces extend the source notes with standard design-document scaffolding: the rival archetype list (7.2), the encounter and trainer tables (6.2, 7.1) — built around type/role rather than fixed species, since the roster is randomized per seed — and the dungeon archetype taxonomy (4.4). v0.2 folds in the July 11, 2026 design review: every v0.1 open question is resolved (Section 13), with domain terms in `CONTEXT.md` and load-bearing trade-offs in `docs/adr/`.*
