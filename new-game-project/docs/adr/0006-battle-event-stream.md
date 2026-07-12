# 0006 — Battle Sim Emits an Event Stream

**Status:** Accepted — 2026-07-12

## Context

`ProcPoke.Battle` is engine-free (ADR-0003), so the battle scene needs a contract with the simulation. A mutate-and-diff contract (UI inspects state changes after each turn) cannot reconstruct what happened in what order — and mainline battles are precisely an ordered narration (crit → ability trigger → faint), which is also where Gen 5 fidelity bugs live.

## Decision

Per turn, the caller submits both sides' actions; the sim resolves the turn atomically to Gen 5 rules and returns an ordered list of typed **BattleEvents** (`MoveUsed`, `DamageDealt {amount, effectiveness, crit}`, `StatStageChanged`, `StatusInflicted`, `AbilityTriggered`, `Fainted`, `MessageShown`, …). The UI is a dumb replayer: one animation + textbox beat per event. The sim's RNG is an injected dependency (the version-stable PRNG): entropy-seeded in game (mainline soft-reset behavior preserved), fixed-seeded in tests, replay-seeded from captured (state, actions, seed) triples for bug reproduction.

## Consequences

- Reference tests assert on event order and content, not just final state — narration fidelity is testable.
- The move-effect fallback policy emits a dev-flagged `EffectNotImplemented` event: visible in logs, invisible in release UI.
- Any battle bug is exactly reproducible from its captured triple.
- The event vocabulary is a contract shared by sim, tests, and presentation — it changes expensively, like the Logical Tile vocabulary.
