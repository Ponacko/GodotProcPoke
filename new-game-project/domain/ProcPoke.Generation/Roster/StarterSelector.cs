using ProcPoke.Data;
using ProcPoke.Generation.Rng;

namespace ProcPoke.Generation.Roster;

/// <summary>
/// Starter triangle selection (GDD §5.3). Chooses a triangle template from those with a non-empty pool
/// at every corner under the current <see cref="GenerationSettings.RosterCap"/> (Classic is always
/// available), then picks one 3-stage, level-only-completable, ≤540-BST evolution line per corner —
/// distinct lines whose final BSTs land as close together as possible.
/// </summary>
public static class StarterSelector
{
    private const int MaxFinalBst = 540;
    private const int BstSpreadThreshold = 40;

    private static readonly IReadOnlyDictionary<StarterTriangle, IReadOnlyList<PokeType>> TriangleCorners =
        new Dictionary<StarterTriangle, IReadOnlyList<PokeType>>
        {
            [StarterTriangle.Classic] = [PokeType.Water, PokeType.Fire, PokeType.Grass],
            [StarterTriangle.MindAndBody] = [PokeType.Fighting, PokeType.Dark, PokeType.Psychic],
            [StarterTriangle.Elemental] = [PokeType.Rock, PokeType.Flying, PokeType.Fighting],
        };

    public static StarterPlan Generate(GameData data, GenerationSettings settings, RngStreams streams)
    {
        var rng = streams.Stream("starters");
        var eligible = EligibleLines(data, settings.RosterCap);

        var available = Enum.GetValues<StarterTriangle>()
            .Where(t => TriangleCorners[t].All(corner => PoolFor(eligible, data, corner).Count > 0))
            .ToList();
        if (available.Count == 0)
            throw new InvalidOperationException("no starter triangle has a non-empty pool at every corner — Classic should always qualify (generator bug).");

        var triangle = rng.Pick(available);
        var corners = SelectCorners(TriangleCorners[triangle], eligible, data, rng);
        return new StarterPlan { Triangle = triangle, Corners = corners };
    }

    /// <summary>Threshold used by the "final BSTs are close together" acceptance check — exposed so tests
    /// assert against the same constant the selector optimizes for.</summary>
    public static int MaxAcceptableBstSpread => BstSpreadThreshold;

    private readonly record struct Line(int Base, int Mid, int Final, bool LevelOnly);

    private static List<Line> EligibleLines(GameData data, int rosterCap)
    {
        var incoming = data.Evolutions.ToLookup(e => e.ToSpeciesId);
        var outgoing = data.Evolutions.ToLookup(e => e.FromSpeciesId);

        var lines = new List<Line>();
        foreach (var species in data.Species.Values)
        {
            if (incoming[species.Id].Any()) continue; // not a base form
            var outs = outgoing[species.Id].ToList();
            if (outs.Count != 1) continue;
            var r1 = outs[0];

            var midId = r1.ToSpeciesId;
            if (incoming[midId].Count() != 1) continue;
            var midOuts = outgoing[midId].ToList();
            if (midOuts.Count != 1) continue;
            var r2 = midOuts[0];

            var finalId = r2.ToSpeciesId;
            if (incoming[finalId].Count() != 1) continue;
            if (outgoing[finalId].Any()) continue; // must be exactly 3 stages

            // Friendship evolutions stay Trigger==LevelUp with MinHappiness set (unlike trade, which is
            // rewritten to its own LinkCable trigger at bake time) — check both, or Leavanny-style lines
            // slip through as "level-only" when they actually need friendship.
            var levelOnly = IsPlainLevelUp(r1) && IsPlainLevelUp(r2);
            lines.Add(new Line(species.Id, midId, finalId, levelOnly));
        }

        return lines
            .Where(l => l.LevelOnly)
            .Where(l => Bst(data.Species[l.Final]) <= MaxFinalBst)
            .Where(l => SpeciesGeneration.Of(l.Base) <= rosterCap
                        && SpeciesGeneration.Of(l.Mid) <= rosterCap
                        && SpeciesGeneration.Of(l.Final) <= rosterCap)
            .OrderBy(l => l.Base)
            .ToList();
    }

    /// <summary>Level-up, and not gated on friendship — friendship evolutions stay
    /// <see cref="EvolutionTrigger.LevelUp"/> with <see cref="EvolutionRule.MinHappiness"/> set (unlike
    /// trade, which gets its own <see cref="EvolutionTrigger.LinkCable"/> trigger at bake time), so the
    /// trigger alone doesn't tell "leveling alone" apart from "leveling once it likes you" (GDD §5.3).</summary>
    private static bool IsPlainLevelUp(EvolutionRule rule)
        => rule.Trigger == EvolutionTrigger.LevelUp && rule.MinHappiness is null;

    private static List<Line> PoolFor(List<Line> eligible, GameData data, PokeType cornerType)
        => eligible.Where(l => data.Species[l.Final].Types.Contains(cornerType)).ToList();

    /// <summary>Brute-forces the corner-triple with the smallest final-BST spread — pools are small
    /// (dozens of 3-stage lines at most), so an exhaustive search is cheap and simple.</summary>
    private static IReadOnlyList<StarterCorner> SelectCorners(
        IReadOnlyList<PokeType> corners, List<Line> eligible, GameData data, Pcg32 rng)
    {
        var poolA = PoolFor(eligible, data, corners[0]);
        var poolB = PoolFor(eligible, data, corners[1]);
        var poolC = PoolFor(eligible, data, corners[2]);

        var bestSpread = int.MaxValue;
        var best = new List<(int A, int B, int C)>();

        for (var a = 0; a < poolA.Count; a++)
        {
            var bstA = Bst(data.Species[poolA[a].Final]);
            for (var b = 0; b < poolB.Count; b++)
            {
                if (poolB[b].Base == poolA[a].Base) continue;
                var bstB = Bst(data.Species[poolB[b].Final]);
                for (var c = 0; c < poolC.Count; c++)
                {
                    if (poolC[c].Base == poolA[a].Base || poolC[c].Base == poolB[b].Base) continue;
                    var bstC = Bst(data.Species[poolC[c].Final]);

                    var spread = Math.Max(bstA, Math.Max(bstB, bstC)) - Math.Min(bstA, Math.Min(bstB, bstC));
                    if (spread < bestSpread) { bestSpread = spread; best.Clear(); best.Add((a, b, c)); }
                    else if (spread == bestSpread) best.Add((a, b, c));
                }
            }
        }

        if (best.Count == 0)
            throw new InvalidOperationException("a triangle was declared available but no distinct corner-triple exists (generator bug).");

        var (ia, ib, ic) = rng.Pick(best);
        return
        [
            new StarterCorner { Type = corners[0], LineSpeciesIds = [poolA[ia].Base, poolA[ia].Mid, poolA[ia].Final] },
            new StarterCorner { Type = corners[1], LineSpeciesIds = [poolB[ib].Base, poolB[ib].Mid, poolB[ib].Final] },
            new StarterCorner { Type = corners[2], LineSpeciesIds = [poolC[ic].Base, poolC[ic].Mid, poolC[ic].Final] },
        ];
    }

    /// <summary>A species' base-stat total — the sum of its six base stats (there is no precomputed BST
    /// field). Internal so sibling roster passes (<see cref="EvolutionFamilies"/>, the dex) share one
    /// definition.</summary>
    internal static int Bst(PokemonSpecies s) => s.BaseStats.Hp + s.BaseStats.Attack + s.BaseStats.Defense
        + s.BaseStats.SpecialAttack + s.BaseStats.SpecialDefense + s.BaseStats.Speed;
}
