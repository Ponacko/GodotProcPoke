# 0001 — Constraint-First Map Carving (no chunk templates)

**Status:** Accepted — 2026-07-11

## Context

The region generator has two layers: an abstract Region Graph (topology, gates, keys, biomes) and the tile-level maps the player actually walks. Two realization strategies were considered:

1. **Chunk/template assembly** — a hand-authored library of parameterized map pieces per archetype, stitched per seed (Spelunky/Isaac model). Guarantees hand-crafted feel by construction; costs a large, ongoing content-authoring workload.
2. **Full tile-level procedural generation** — every map generated algorithmically with no hand-built pieces.

The gating system imposes a hard structural requirement: a Gate only functions if map geometry guarantees no path around it. Unconstrained generation (noise/WFC + validate-and-retry) cannot cheaply guarantee this, and reliably fails the GDD's "feels hand-crafted" mandate.

## Decision

Full tile-level procgen, but **constraint-first**: each map is *carved*, not sampled. The generator draws the walkable Spine first, places Gates on verified Chokepoints as hard guarantees, then decorates outward under archetype-specific placement rules (trainer sightlines face the path, ledges drop toward the entrance, tall grass straddles the mandatory path, item nooks behind optional obstacles). Each archetype (route, cave, town, …) gets its own carver. No hand-built chunk library exists anywhere in the pipeline.

## Consequences

- Zero per-map content authoring; all effort goes into carver algorithms and their placement rules — the right trade for a solo programmer.
- "Hand-crafted feel" becomes a property of the rule set, so carver rules need real design iteration per archetype.
- Solvability is guaranteed by construction (gates only ever exist on carved chokepoints), not by post-hoc validation — generate-and-retry loops on free terrain were explicitly rejected as a failure mode.
- More novel algorithm work up front than chunk assembly; less art/content work forever after.
