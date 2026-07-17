# Blueprint — Ticket 3c: villain teams, rival identity, Champion-identity roll

Spec + acceptance asserts: `../../tickets.md` §3c. This is the **canonical pass example** — every later
population pass follows this same shape (static class, one named RNG stream, one immutable record out, one
line added to `RegionGenerator.Generate`, one field added to `GeneratedRegion`, one nullable arg added to
`RegionGraphText`).

Determinism rule: draws happen in the exact order written below. Reordering changes every seed's output.

---

## 1. New records + enum — extend `RegionIdentity`

```csharp
// FILE: domain/ProcPoke.Generation/Identity/RegionIdentity.cs
// Extend the EXISTING record (add the three fields) and add the two new types below it.
// Keep the existing GymTypes / EliteFourTypes / ChampionIsTypeless fields unchanged.

using ProcPoke.Data;

namespace ProcPoke.Generation.Identity;

public sealed record RegionIdentity
{
    public required IReadOnlyDictionary<int, PokeType> GymTypes { get; init; }
    public required IReadOnlyList<PokeType> EliteFourTypes { get; init; }
    public bool ChampionIsTypeless { get; init; } = true;

    // --- added by ticket 3c ---
    public IReadOnlyList<VillainTeam> VillainTeams { get; init; } = [];
    public RivalArchetype Rival { get; init; } = RivalArchetype.Cocky;
    public bool ChampionIsRival { get; init; }
}

/// <summary>A generated villain organisation (GDD §7.3): a name and a 1–2 type thematic motif.</summary>
public sealed record VillainTeam(string Name, IReadOnlyList<PokeType> Motif);

/// <summary>The rival's personality archetype (GDD §7.2), fixed per seed.</summary>
public enum RivalArchetype { Cocky, Friendly, Brooding, Underdog }
```

## 2. The pass

```csharp
// FILE: domain/ProcPoke.Generation/Identity/IdentityPass.cs   (new file)
using ProcPoke.Data;
using ProcPoke.Generation.Rng;

namespace ProcPoke.Generation.Identity;

/// <summary>
/// Villain teams, rival archetype, and the Champion-identity roll (GDD §7.1/§7.2/§7.3). Runs right after
/// <see cref="GymTypingPass"/> and layers onto the identity it produced — the Champion roll and the
/// archetype pick share one stream so rival-Champion seeds can skew Underdog (they must be drawn together).
/// </summary>
public static class IdentityPass
{
    /// <summary>Canon organisation names the generator must never reproduce (there is no data file for
    /// these — hardcoded per GDD §8).</summary>
    private static readonly string[] CanonTeamNames =
        ["Rocket", "Aqua", "Magma", "Galactic", "Plasma", "Flare", "Skull", "Yell", "Star", "Snagem", "Cipher"];

    public static RegionIdentity Generate(RegionIdentity identity, RegionNames names, RngStreams streams)
    {
        var rng = streams.Stream("identity");
        var motifRoots = NameParts.Pools[names.Motif].Roots;
        var gymTypes = identity.GymTypes.Values.ToHashSet();

        // DRAW ORDER IS LOAD-BEARING — do not reorder.

        // 1. Team count: 30% two teams, else one.
        var teamCount = rng.Chance(0.30) ? 2 : 1;

        // 2. Team names + 3. type motifs, per team.
        var teams = new List<VillainTeam>();
        for (var t = 0; t < teamCount; t++)
        {
            // >>> IMPLEMENT(1): pick a root via rng.Pick(motifRoots); redraw while the root (case-insensitive)
            //     is in CanonTeamNames OR equals a root already used by an earlier team this seed. Name is
            //     $"Team {root}". The motif Roots pool has 6 entries and the canon list is small, so a bounded
            //     redraw loop (e.g. up to motifRoots.Count*4 tries) always finds one; keep the chosen root
            //     recorded so team 2 can't collide with team 1.
            string name = throw new NotImplementedException();

            // >>> IMPLEMENT(2): build the motif type list. Draw 1 type, or 2 if rng.Chance(0.40).
            //     Candidate pool = all Enum.GetValues<PokeType>() EXCEPT gymTypes and any type already in an
            //     earlier team's motif. If that leaves fewer than needed, fall back to excluding only the
            //     earlier teams' motifs (never the gym types). Use rng.Pick; the two types (if 2) must differ.
            IReadOnlyList<PokeType> motif = throw new NotImplementedException();

            teams.Add(new VillainTeam(name, motif));
        }

        // 4. Champion roll: 25% the rival is the Champion.
        var championIsRival = rng.Chance(0.25);

        // 5. Archetype. Rival-Champion seeds weight Underdog; otherwise uniform.
        // >>> IMPLEMENT(3): if championIsRival, pick by cumulative weight from rng.NextDouble() with
        //     Underdog 0.55, Cocky 0.15, Friendly 0.15, Brooding 0.15 (in that comparison order). Else
        //     rng.Pick over Enum.GetValues<RivalArchetype>().
        RivalArchetype archetype = throw new NotImplementedException();

        return identity with
        {
            VillainTeams = teams,
            Rival = archetype,
            ChampionIsRival = championIsRival,
        };
    }
}
```

## 3. Wiring

```csharp
// WIRING — FILE: domain/ProcPoke.Generation/RegionGenerator.cs
// In Generate(...), immediately AFTER the GymTypingPass line:
//     var identity = GymTypingPass.Generate(graph, biomes, streams);
// add:
//     identity = IdentityPass.Generate(identity, names, streams);
// (GeneratedRegion already carries `RegionIdentity Identity` — no new field needed; the record just
//  gained fields, which flow through unchanged.)
```

```csharp
// WIRING — FILE: domain/ProcPoke.Generation/Debug/RegionGraphText.cs
// The `identity` arg already exists on Render(...). In the block that already prints Elite Four /
// Champion, ALSO print (guard on identity is non-null):
//   - each team: $"Villain: {team.Name} [{string.Join('/', team.Motif)}]"
//   - $"Rival: {identity.Rival}"
//   - identity.ChampionIsRival ? "Champion: the rival" : "Champion: generated NPC"
```

## Test

`tests/ProcPoke.Generation.Tests/IdentityTests.cs`, standard corpus (seed 1..2000 × badges {4,8,12}),
following `NamingTests`' shape. Assert the four bullets in ticket 3c's acceptance list. The rate check
(`ChampionIsRival` ∈ [0.20, 0.30]) and the conditional-Underdog check accumulate counts across the whole
corpus, then assert once at the end (with a `count > 0` guard so an empty corpus fails).
