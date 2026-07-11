# 0002 — Constructive Lock-and-Key Generation (validation as assertion)

**Status:** Accepted — 2026-07-11

## Context

GDD §4.5 specified a generate-then-validate pipeline for the gating graph: build a region, check for unreachable keys / circular locks, and discard-and-regenerate on failure. This left an open question (§13) about whether failed seeds regenerate silently or surface an error. Generate-and-retry was already rejected at the map layer (ADR-0001).

## Decision

The lock-and-key graph is generated **constructively**: the generator walks the critical path in order, and when placing gate N, places its key (and, for Field Moves, sets its Badge Prerequisite) only within areas provably reachable before gate N given what the player can already have. Circular locks and unreachable keys are unrepresentable by construction.

The §4.5 validator is retained but demoted to an internal sanity assertion. If it ever fires (a generator bug), the game silently re-rolls an internal sub-seed, regenerates, and logs diagnostics. The player never sees a failed generation; Generate always yields a valid region.

## Consequences

- No player-facing error state or regeneration loop; the §13 "solvability fallback" question is dissolved rather than answered.
- The validator's job shifts from gatekeeping output to catching regressions in the generator itself.
- A silent re-roll produces a region that differs from what the same seed yields after the underlying bug is fixed — accepted, as it only occurs in bug scenarios (see the seed-stability policy).
