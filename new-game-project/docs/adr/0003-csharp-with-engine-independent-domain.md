# 0003 — C# Throughout, Engine-Independent Domain Layers

**Status:** Accepted — 2026-07-11

## Context

Godot 4.6 (.NET build) supports both GDScript and C#. GDScript iterates faster in-editor; C# gives static typing, refactoring safety, and better performance for heavy loops. ProcPoke is unusually simulation-shaped: a Gen 5-faithful battle engine and a whole-region procedural generator, both large, rule-dense domains where correctness against known reference behavior matters more than hot-reload speed.

## Decision

C# is the single implementation language. The two core domains — the battle simulation and the region generator — are written as plain C# class libraries with no Godot dependencies; Godot scenes/nodes are a thin presentation layer over them.

## Consequences

- Battle-formula fidelity (damage, catch rate, stat math) and generator invariants (gate solvability, chokepoint guarantees) are verifiable in fast unit tests without booting the engine.
- Generation-time work (map carving, constructive lock-and-key, roster selection) runs at compiled speed behind the Generate button.
- Slower edit-run iteration than GDScript and a build step — accepted; the presentation layer that would benefit from hot iteration is deliberately thin.
- One greppable language across the codebase; no cross-language marshalling at the domain boundary.
