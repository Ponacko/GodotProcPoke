# 0004 — Generation Pass Order: Puzzle Before Terrain

**Status:** Accepted — 2026-07-11

## Context

Each generation pass can only depend on passes before it, so the pipeline order is a one-way commitment. The contestable ordering: mainline designers work "region first, obstacles where terrain suggests them"; a generator could mimic that (biomes → gates) or invert it (gates → biomes).

## Decision

The pipeline: **parameters/seed → topology → gates & keys → biomes → dungeon archetypes → roster → population (encounters/trainers/items) → identity (names, villain, rival) → carving.**

Biomes come **after** gates: the lock-and-key graph is the game's defining layer, terrain is negotiable set-dressing, so the puzzle is never compromised to fit scenery. Gate/biome coherence is guaranteed constructively, not by exception handling:

- **Portable obstacles** (cut trees, boulders, …) adapt to any biome via carved Terrain Insets — no constraint on the biome pass.
- **Terrain-bound obstacles** (surf, waterfall, desert, …) emit Biome Requirement tags that the biome pass honors as fixed points it grows the biome map around.
- The gate pass allows at most one terrain tag per area, filtering conflicting obstacles from the draw — "waterfall in a desert" is unrepresentable.

## Consequences

- The puzzle graph is generated with full freedom; terrain always justifies it after the fact.
- Biome maps are constrained-growth (fixed points + adjacency coherence) rather than free assignment.
- Carvers must support Terrain Insets in every biome, since any portable gate can appear anywhere.
- Later passes (roster, encounters) see settled biomes, keeping the dex's biome slices stable.
