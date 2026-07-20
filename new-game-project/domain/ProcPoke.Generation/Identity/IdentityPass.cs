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
        var allTypes = Enum.GetValues<PokeType>();

        // DRAW ORDER IS LOAD-BEARING — do not reorder.

        // 1. Team count: 30% two teams, else one.
        var teamCount = rng.Chance(0.30) ? 2 : 1;

        // 2. Team names + 3. type motifs, per team.
        var teams = new List<VillainTeam>();
        var usedRoots = new List<string>();
        for (var t = 0; t < teamCount; t++)
        {
            // Name: a motif root that is neither a canon team name nor already used by an earlier team.
            // Motif roots (6) never collide with the canon list, so the bounded loop resolves quickly.
            string root;
            var tries = 0;
            bool Collides(string r) => CanonTeamNames.Any(c => c.Equals(r, StringComparison.OrdinalIgnoreCase))
                                       || usedRoots.Any(u => u.Equals(r, StringComparison.OrdinalIgnoreCase));
            do { root = rng.Pick(motifRoots); }
            while (Collides(root) && ++tries < motifRoots.Count * 4);
            // With 6 distinct roots (none canon) and ≤1 prior team, a free root is found almost immediately;
            // hitting the cap while still colliding would violate the distinctness invariant, so fail loudly.
            if (Collides(root))
                throw new InvalidOperationException("could not find a distinct non-canon villain-team name (generator bug).");
            usedRoots.Add(root);

            // Motif: 1 type, or 2 (40%). Exclude gym types and earlier teams' motifs (§7.1 — chosen away
            // from gym collisions); if that leaves too few, drop only the gym-type exclusion.
            var motifSize = rng.Chance(0.40) ? 2 : 1;
            var earlierMotifs = teams.SelectMany(x => x.Motif).ToHashSet();
            var pool = allTypes.Where(x => !gymTypes.Contains(x) && !earlierMotifs.Contains(x)).ToList();
            if (pool.Count < motifSize)
                pool = allTypes.Where(x => !earlierMotifs.Contains(x)).ToList();

            var motif = new List<PokeType>();
            for (var i = 0; i < motifSize; i++)
            {
                var pick = rng.Pick(pool);
                motif.Add(pick);
                pool = pool.Where(x => x != pick).ToList(); // the two types must differ
            }

            teams.Add(new VillainTeam($"Team {root}", motif));
        }

        // 4. Champion roll: 25% the rival is the Champion.
        var championIsRival = rng.Chance(0.25);

        // 5. Archetype. Rival-Champion seeds weight Underdog; otherwise uniform.
        RivalArchetype archetype;
        if (championIsRival)
        {
            var r = rng.NextDouble();
            archetype = r < 0.55 ? RivalArchetype.Underdog
                : r < 0.70 ? RivalArchetype.Cocky
                : r < 0.85 ? RivalArchetype.Friendly
                : RivalArchetype.Brooding;
        }
        else
        {
            archetype = rng.Pick(Enum.GetValues<RivalArchetype>());
        }

        return identity with
        {
            VillainTeams = teams,
            Rival = archetype,
            ChampionIsRival = championIsRival,
        };
    }
}
