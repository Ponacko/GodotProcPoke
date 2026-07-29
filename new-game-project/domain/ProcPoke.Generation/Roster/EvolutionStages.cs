using ProcPoke.Data;

namespace ProcPoke.Generation.Roster;

/// <summary>
/// How many evolutions deep each species sits within the roster cap: 0 for a base form, 1 for its first
/// evolution, and so on. Branching lines (Eevee) put every branch at the same depth.
/// <para>
/// <see cref="EvolutionFamily.Members"/> is in dex-seat order, which is not stage order — a branching
/// family lists all of its stage-1 forms after the root, and a family truncated by the cap can start at what
/// is a mid-stage species in the full dex. Depth has to be measured from the pre-evolution edges.
/// </para>
/// </summary>
public static class EvolutionStages
{
    /// <summary>speciesId → stage, for every species within <paramref name="rosterCap"/> generations.</summary>
    public static IReadOnlyDictionary<int, int> Of(GameData data, int rosterCap)
    {
        var inCap = data.Species.Values
            .Select(s => s.Id)
            .Where(id => SpeciesGeneration.Of(id) <= rosterCap)
            .ToHashSet();

        // Pre-evolution edges, both endpoints in cap. A species reachable from two pre-evolutions is not a
        // thing in the data, but taking the shallowest keeps this total either way.
        var preEvolutions = inCap.ToDictionary(id => id, _ => new List<int>());
        foreach (var evolution in data.Evolutions)
            if (inCap.Contains(evolution.FromSpeciesId) && inCap.Contains(evolution.ToSpeciesId)
                && evolution.FromSpeciesId != evolution.ToSpeciesId)
                preEvolutions[evolution.ToSpeciesId].Add(evolution.FromSpeciesId);

        var stages = new Dictionary<int, int>();

        int Depth(int id, HashSet<int> visiting)
        {
            if (stages.TryGetValue(id, out var known)) return known;
            // Guard against a cycle in the data rather than trusting it: treat it as a base form.
            if (!visiting.Add(id)) return 0;

            var parents = preEvolutions[id];
            var depth = parents.Count == 0 ? 0 : parents.Min(p => Depth(p, visiting)) + 1;

            visiting.Remove(id);
            stages[id] = depth;
            return depth;
        }

        foreach (var id in inCap.OrderBy(id => id)) Depth(id, []);
        return stages;
    }
}
