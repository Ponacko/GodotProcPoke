using ProcPoke.Data;
using ProcPoke.Generation.Bosses;
using ProcPoke.Generation.Encounters;
using ProcPoke.Generation.Identity;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Roster;

namespace ProcPoke.Generation.Rivals;

/// <summary>Builds the three deterministic rival variants described in GDD §7.2.</summary>
public static class RivalPass
{
    private static readonly (RivalBeat Beat, int TeamSize)[] BeatSizes =
    [
        (RivalBeat.PostStarter, 1),
        (RivalBeat.EarlyRoute, 2),
        (RivalBeat.Midpoint, 4),
        (RivalBeat.PreLeague, 5),
    ];

    public static RivalPlan Generate(
        RegionIdentity identity,
        DexPlan dex,
        StarterPlan starters,
        GenerationSettings settings,
        GameData data,
        RngStreams streams)
    {
        var rng = streams.Stream("rival");
        var dexSpecies = dex.Entries
            .Where(entry => data.Species.ContainsKey(entry.SpeciesId))
            .Select(entry => entry.SpeciesId)
            .Distinct()
            .ToList();
        var familyOf = FamilyMap(data, settings.RosterCap);
        var gymCount = identity.GymTypes.Count;
        var variants = new List<RivalVariant>(starters.Corners.Count);

        for (var playerCornerIndex = 0; playerCornerIndex < starters.Corners.Count; playerCornerIndex++)
        {
            var rivalCornerIndex = (playerCornerIndex + 2) % starters.Corners.Count;
            var rivalCorner = starters.Corners[rivalCornerIndex];
            var beats = new List<RivalTeam>(BeatSizes.Length);

            for (var beatIndex = 0; beatIndex < BeatSizes.Length; beatIndex++)
            {
                var (beat, teamSize) = BeatSizes[beatIndex];
                var ace = AceFor(beat, gymCount);
                var rivalStarter = beat == RivalBeat.PostStarter
                    ? rivalCorner.BaseSpeciesId
                    : HighestPermittedStage(rivalCorner, ace, data);
                var members = new List<BossMember> { new(rivalStarter, ace) };

                if (teamSize > 1)
                {
                    var usedFamilies = new HashSet<int> { FamilyOf(rivalStarter, familyOf) };
                    var fraction = beatIndex / (BeatSizes.Length - 1.0);
                    var targetBst = 250 + 350 * fraction;
                    for (var slot = 1; slot < teamSize; slot++)
                    {
                        var candidate = PickClosestSpecies(
                            dexSpecies, usedFamilies, targetBst, data, familyOf, rng);
                        usedFamilies.Add(FamilyOf(candidate, familyOf));
                        members.Add(new BossMember(candidate, ace));
                    }
                }

                beats.Add(new RivalTeam(beat, ace, rivalStarter, members));
            }

            BossTeam? champion = null;
            if (identity.ChampionIsRival)
            {
                champion = BossPass.BuildChampion(
                    $"Rival Champion (corner {playerCornerIndex + 1})",
                    dexSpecies, starters, settings, data, rng, rivalCorner.FinalSpeciesId);
            }

            variants.Add(new RivalVariant(playerCornerIndex, rivalCornerIndex, beats, champion));
        }

        return new RivalPlan { Variants = variants };
    }

    private static int AceFor(RivalBeat beat, int gymCount)
        => beat switch
        {
            RivalBeat.PostStarter => 5,
            RivalBeat.EarlyRoute => LevelCurve.GymAce(0, gymCount) + 2,
            RivalBeat.Midpoint => LevelCurve.GymAce(gymCount / 2, gymCount),
            RivalBeat.PreLeague => 52,
            _ => throw new ArgumentOutOfRangeException(nameof(beat)),
        };

    private static int HighestPermittedStage(StarterCorner corner, int ace, GameData data)
    {
        var stage = 0;
        for (var i = 1; i < corner.LineSpeciesIds.Count; i++)
        {
            var rule = data.Evolutions.SingleOrDefault(e =>
                e.FromSpeciesId == corner.LineSpeciesIds[i - 1] &&
                e.ToSpeciesId == corner.LineSpeciesIds[i]);
            if (rule?.MinLevel is not int minLevel || minLevel > ace)
                break;
            stage = i;
        }
        return corner.LineSpeciesIds[stage];
    }

    private static int PickClosestSpecies(
        IReadOnlyList<int> dexSpecies,
        IReadOnlySet<int> usedFamilies,
        double targetBst,
        GameData data,
        IReadOnlyDictionary<int, int> familyOf,
        Pcg32 rng)
    {
        var candidates = dexSpecies
            .Where(id => !usedFamilies.Contains(FamilyOf(id, familyOf)))
            .Select(id => (SpeciesId: id, Distance: Math.Abs(StarterSelector.Bst(data.Species[id]) - targetBst)))
            .ToList();
        if (candidates.Count == 0)
            throw new InvalidOperationException("regional dex cannot supply a distinct-family rival team");

        var bestDistance = candidates.Min(candidate => candidate.Distance);
        var tied = candidates
            .Where(candidate => candidate.Distance == bestDistance)
            .OrderBy(candidate => candidate.SpeciesId)
            .Select(candidate => candidate.SpeciesId)
            .ToList();
        return rng.Pick(tied);
    }

    private static IReadOnlyDictionary<int, int> FamilyMap(GameData data, int rosterCap)
    {
        var map = new Dictionary<int, int>();
        var familyIndex = 0;
        foreach (var family in EvolutionFamilies.Build(data, rosterCap))
        {
            foreach (var speciesId in family.Members) map[speciesId] = familyIndex;
            familyIndex++;
        }
        return map;
    }

    private static int FamilyOf(int speciesId, IReadOnlyDictionary<int, int> familyOf)
        => familyOf.TryGetValue(speciesId, out var family) ? family : -speciesId;
}
