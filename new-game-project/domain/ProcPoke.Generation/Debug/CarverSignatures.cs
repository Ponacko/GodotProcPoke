using System.Text;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Debug;

/// <summary>Stable structural fingerprints used by carver tests and the Phase 2 packet distinctness check.</summary>
public static class CarverSignatures
{
    public static string Of(Area area, CarvedArea carved)
    {
        var builder = new StringBuilder()
            .Append(area.Archetype).Append('|').Append(area.Size).Append('|')
            .Append(carved.Grid.Width).Append('x').Append(carved.Grid.Height).Append('|')
            .Append(carved.TrunkRow).Append('|')
            .Append(string.Join(',', carved.Openings.OrderBy(p => p.Y).ThenBy(p => p.X)))
            .Append('|').Append(string.Join(',', carved.GateTiles.OrderBy(p => p.Y).ThenBy(p => p.X)))
            .Append('|');

        for (var y = 0; y < carved.Grid.Height; y++)
        {
            for (var x = 0; x < carved.Grid.Width; x++)
                builder.Append((char)('A' + (int)carved.Grid[x, y]));
            builder.Append('/');
        }
        return builder.ToString();
    }

    public static IReadOnlyDictionary<AreaArchetype, bool> DistinctByArchetype(
        IEnumerable<(Area Area, CarvedArea Carved)> maps)
        => maps.GroupBy(pair => pair.Area.Archetype)
            .ToDictionary(group => group.Key,
                group => group.Select(pair => Of(pair.Area, pair.Carved)).Distinct().Count() > 1);
}
