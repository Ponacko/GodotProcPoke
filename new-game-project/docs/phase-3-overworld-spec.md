# Phase 3 — Overworld

## Problem Statement

Phase 2 can generate and validate a complete region, but the player cannot yet walk it. Logical tiles are
currently debug data rather than a playable world: they have no biome art, movement rules, area transitions,
interactions, or physical gate behavior in Godot. The next phase must turn one deterministic generated region
into a navigable overworld while preserving the engine-independent generation contract and the map invariants
already established in Phase 2.

## Solution

Build the first playable overworld around three seams:

1. A Tile Realizer converts a logical tile grid plus biome into Godot TileMaps and collision/navigation data.
2. An Overworld Session owns the player, current area, movement mode, camera, transitions, and interaction
   dispatch without changing the generated domain objects.
3. An Interaction Registry maps generated markers and gates to talk, pickup, field-move, and transition
   actions, keeping dialogue and gameplay effects behind stable interfaces.

The player starts in the generated StartTown, can walk through seamless outdoor connections and warp into
interiors, can interact with NPC posts and item balls, and can see gates physically block progress. Debug
unlocking remains available until the badge and battle systems make normal key acquisition possible.

## User Stories

1. As a player, I want a generated region rendered with coherent biome art, so that the logical map reads as a
   game world rather than a debug diagram.
2. As a player, I want walls, water, trees, boulders, gates, and building boundaries to have correct collision,
   so that I cannot walk through scenery or obstacles.
3. As a player, I want Ground, TallGrass, Sand, Ledge, Warp, TrainerPost, ItemBall, and NpcPost to retain their
   gameplay meaning after realization, so that generated content remains usable.
4. As a player, I want to move on a grid with keyboard controls, so that movement is predictable and faithful
   to the intended mainline-style experience.
5. As a player, I want controller bindings for the same movement and interaction actions, so that the game is
   playable without a keyboard.
6. As a player, I want walking and running tiers, so that I can explore carefully or cross familiar ground
   quickly.
7. As a player, I want Bicycle movement where the generated rules permit it, so that long routes do not feel
   unnecessarily slow.
8. As a player, I want Surf to change movement onto surfable water only when I have the required key or debug
   unlock, so that water gates have physical meaning.
9. As a player, I want Ledge tiles to permit only the documented southward hop, so that shortcuts and one-way
   route geometry behave as designed.
10. As a player, I want the camera to follow the player without exposing invalid map space, so that each area
    feels like a contained playable map.
11. As a player, I want to cross a seamless outdoor connection near its aligned opening, so that adjacent
    generated areas feel like one continuous landmass.
12. As a player, I want interiors, caves, towers, hideouts, and other warp connections to transition cleanly,
    so that entering a building or dungeon does not require understanding map internals.
13. As a player, I want transitions to preserve my arrival direction and place me at the paired destination
    opening, so that I never appear inside a wall or facing an arbitrary direction.
14. As a player, I want a generated NPC post to be talkable, so that hints, signs, gym guidance, gossip, and
    flavor text are visible in the world.
15. As a player, I want hint dialogue to mention the generated key location, so that gates are discoverable
    without consulting debug output.
16. As a player, I want signs and named places to use generated names, so that the overworld matches the region
    identity shown elsewhere.
17. As a player, I want item balls to be collectible once, so that generated item contents have a clear gameplay
    result and cannot be farmed by repeatedly entering an area.
18. As a player, I want trainer posts to visibly guard the route, so that their sightlines and engagement points
    are understandable before trainer battles are wired.
19. As a player, I want gates to show their obstacle type and a useful prompt, so that I understand why a path is
    blocked and what kind of key will eventually open it.
20. As a player, I want debug unlock controls for every gate, so that the entire generated region can be walked
    before the battle and badge systems exist.
21. As a player, I want a dark-cave vision radius before Flash, so that caves are navigable but meaningfully
    different before the relevant field move is acquired.
22. As a player, I want a region map screen showing visited areas and available Fly destinations, so that
    backtracking remains practical in a branched region.
23. As a player, I want a time-of-day clock and night tint, so that the world has a readable day/night state
    without changing deterministic generation output.
24. As a player, I want a new-game choice of character and name, so that the playable session has a stable player
    identity before exploration begins.
25. As a player, I want generation settings and the seed to remain visible in debug mode, so that a reported map
    problem can be reproduced exactly.
26. As a developer, I want the Tile Realizer to be the only component that chooses art, so that carvers and
    invariant tests remain independent of Godot assets.
27. As a developer, I want area transitions to consume the generated OpeningPlan and WorldCanvas metadata, so
    that runtime navigation cannot silently disagree with Phase 2 geometry.
28. As a developer, I want interaction handlers to consume immutable generated plans, so that rendering and
    presentation changes cannot alter encounter, trainer, NPC, or item assignments.
29. As a developer, I want movement and interaction tests to run without booting the Godot editor wherever
    possible, so that regressions are fast and deterministic.
30. As a developer, I want a three-seed start-to-League smoke harness, so that the Phase 3 exit criterion is
    reproducible from the command line.

## Implementation Decisions

- Preserve the Phase 2 engine-independent boundary. `ProcPoke.Generation` continues to emit logical tiles,
  openings, gate metadata, NPC plans, trainer plans, encounter plans, and names; Godot owns only realization and
  session presentation.
- Use a Tile Realizer seam of `(LogicalTile grid, Biome, rendering profile) → TileMap layers + collision
  shapes`. The realizer owns palettes, autotiling, biome variants, water animation hooks, and debug colors.
- Keep gameplay markers in a dedicated interaction layer rather than baking dialogue or item effects into tile
  art. Marker coordinates remain sourced from the carved map and population plans.
- Use one Overworld Session state containing the current area, player position/facing, movement mode, camera
  state, visited-area set, badge/key debug state, and transition state. Area scenes are views of that state, not
  independent progression authorities.
- Define movement as grid-locked intent resolution: check facing/interaction first for the action button, then
  resolve movement against collision, ledge direction, field-move permissions, and transition openings.
- Implement Ledge as a directional movement rule, not ordinary collision. The Phase 2 contract remains that
  Ledge is walkable for connectivity analysis but permits only north-to-south movement at runtime.
- Model seamless transitions as paired outdoor area loads using aligned edge openings. Model warp transitions as
  fade-out/fade-in pairs with destination coordinates supplied by the transition table.
- Build a fixed Center and Mart interior template first, then reuse the same transition and interaction seams for
  generated houses, gyms, caves, towers, and hideouts.
- Treat NPC posts, trainer posts, item balls, signs, and gates as interaction registrations with stable IDs and
  positions. Text and contents come from generated plans; one-time state is owned by the session/save layer.
- Keep debug unlock as an explicit development capability. It clears gate interaction checks without mutating
  the generated map or pretending that badges and keys exist.
- Keep day/night presentation state separate from generation RNG. Time-of-day changes tint, encounter hooks,
  and presentation only; it does not regenerate maps or reorder plans.
- Add input actions for directional movement, confirm/interact, cancel, run, Bicycle, Surf, menu, and map, with
  keyboard and controller bindings in project settings.
- Add a runtime map/transition adapter rather than exposing Godot nodes to domain records. The adapter is the
  highest seam shared by rendering, movement, transitions, and interaction tests.
- Organize delivery as the following tickets:
  - P3-1: Tile Realizer, biome palettes, layers, and collision.
  - P3-2: grid movement, facing, running/Bicycle, ledges, and camera bounds.
  - P3-3: seamless and warp transitions using aligned openings and arrival placement.
  - P3-4: fixed Center/Mart interiors plus reusable house and gym entry templates.
  - P3-5: NPC posts, signs, item balls, gates, debug unlock, and dark-cave vision.
  - P3-6: region map, visited/Fly state, day/night tint, and new-game identity setup.
  - P3-7: three-seed start-to-League playability packet and Phase 3 exit review.

## Testing Decisions

- Test external behavior at the highest available seam: logical grid plus generated metadata for movement and
  interaction rules; Godot scene tests only for node wiring, rendering layers, and input bindings.
- Add pure domain tests for movement intent resolution, ledge direction, gate permissions, paired transition
  coordinates, item one-time collection, and visited-area/Fly eligibility.
- Add integration tests that generate seeds 2, 42, and 777, realize the StartTown and critical path, and verify
  that every area opening has a valid arrival tile, every marker has an interaction registration, and every gate
  remains blocked until debug-unlocked.
- Reuse the Phase 2 fuzz corpus for edge alignment, reachability, NPC marker counts, and generated-name checks;
  the Tile Realizer must not change those domain outputs.
- Add screenshot or pixel-signature checks for representative biome/tile combinations, while avoiding brittle
  full-screen snapshots for camera and text layout.
- Add an input matrix test for keyboard/controller actions and a transition replay test that walks a fixed route
  through at least one seamless connection, one warp, one ledge, one item, one NPC, and one gate.
- Add a headless smoke command that attempts StartTown → League on at least three seeds with every gate debug
  unlocked. It should report the first blocked transition with area ID, position, facing, and gate metadata.
- Keep the existing generation, data, and battle test suites in the default solution test run.

## Out of Scope

- Full battle simulation, wild-battle transitions, trainer battles, gym battles, catching, XP, evolution, and
  badges; those belong to Phase 4 and Phase 5.
- Final art production, bespoke animations, music, sound effects, and polished UI; use debug or placeholder art
  until the shell and polish phases.
- Generated overworld terrain beyond the MVP logical tile vocabulary and existing carver archetypes.
- Online features, trading, breeding, alternate forms, roaming legendaries, and post-MVP facilities.
- Save serialization and the final multi-region save schema; Phase 3 may use an in-memory session and a debug
  seed display.
- Rewriting the Phase 2 generator to accommodate runtime presentation requirements.

## Further Notes

- The Phase 3 exit criterion is a human-playable walk from StartTown to League on at least three different seeds,
  with gates physically blocking until debug-unlocked and no broken transition or map.
- The Phase 2 human map review remains a prerequisite input to P3-7. If the rendered logical maps fail the
  hand-crafted readability bar, record the gap list and ADR-0001 decision before polishing the Tile Realizer.
- The first implementation should prefer a visibly complete debug overworld over a large art surface. Stable
  movement, transitions, collision, and interaction seams are the risk-reduction goal of this phase.
- Once the issue tracker is connected, publish P3-1 through P3-7 with the `ready-for-agent` label and retain this
  document as their shared specification.
