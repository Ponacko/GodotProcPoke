# Blueprints

A **blueprint** is a near-complete C# scaffold for one ticket, written by a strong model so a weaker
implementation model (Sonnet / Qwen2.5-coder class) only has to fill in the marked method bodies. The
hard, expensive-to-get-wrong decisions — record shapes, public signatures, pass wiring, RNG stream names,
the exact contract downstream tickets depend on — are already made. The mechanical bodies are not.

Blueprints exist **only** for the tickets whose *output shape is a shared contract* that later tickets
consume, because a shape error there propagates across many tickets. Self-contained tickets (the carvers,
which copy an existing carver in `Carving/`) have no blueprint — the ticket text is enough.

## How a weak model uses a blueprint

1. Read the matching ticket in `../../tickets.md` first — it has the acceptance asserts and the algorithm
   in prose. The blueprint is the *skeleton*; the ticket is the *spec*.
2. Copy each fenced code block to the file path named in its header comment (`// FILE: …`).
3. Fill every `// >>> IMPLEMENT(n): …` marker. Each marker states exactly what its body must compute. Do
   **not** change public signatures, record fields, enum members, or stream names — downstream tickets and
   tests are written against them verbatim.
4. Wire the pass into `RegionGenerator.Generate` and `GeneratedRegion` exactly as the `// WIRING` block
   shows.
5. Write the test named in the ticket, then run `dotnet test`.

## Index

| Blueprint | Ticket | Why it has one |
|---|---|---|
| `3c-identity-pass.md` | 3c | First population pass — doubles as the canonical "how to add a pass" example |
| `4a1-evolution-families.md` | 4a-1 | `EvolutionFamily` + `AvailabilityOrder` are consumed by 4a-2, 4c, 6a, 7a/b/c |
| `6a-encounter-framework.md` | 6a | Encounter records + `LevelCurve` are consumed by 6b, 7a, 7b, 7c |

All three are **Qwen-OK** tickets: with the blueprint, a local coder model has the full shape and only
writes bodies whose logic is pinned in the ticket.
