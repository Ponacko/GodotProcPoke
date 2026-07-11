# 0005 — Hierarchical Named RNG Streams, Version-Stable PRNG

**Status:** Accepted — 2026-07-11

## Context

All generation randomness flows from the master seed (Seed String promise, GDD §11). A single sequential RNG would be deterministic but brittle: any pass drawing one extra number reshuffles everything downstream, so no change can ever be isolated, and debugging "same region, different roster" states is impossible.

## Decision

Randomness is organized as **hierarchical named streams**: child seeds are derived by hashing (`hash(masterSeed, streamName)`), e.g. `topology`, `gates`, `roster`, `carve/area-17`. Each pass — and each area within a pass — consumes only its own stream, seeded into a small version-stable PRNG (PCG/xoshiro class) implemented in our code.

`System.Random` is banned from generation code: its algorithm is not stability-guaranteed across .NET versions and has changed before, which would silently break the per-version determinism promise through a mere runtime upgrade.

## Consequences

- A draw-count change in one pass perturbs nothing else; single areas can be regenerated in isolation for debugging.
- Determinism depends only on our own hash + PRNG implementation, not on BCL behavior.
- Every consumer must ask for a named stream rather than sharing an RNG instance — a mild discipline tax, enforceable by keeping the PRNG type internal to a stream factory.
