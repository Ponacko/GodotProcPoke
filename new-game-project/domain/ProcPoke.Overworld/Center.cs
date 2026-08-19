using ProcPoke.Generation.Carving;

namespace ProcPoke.Overworld;

/// <summary>
/// Binds a town's Pokémon Center clerk to the fixed interior template. Talking to the clerk fully heals
/// the party (<see cref="OverworldSession.HealParty"/>) — the same restoration a whiteout already applies
/// on the way back, just available on request instead of only after fainting.
/// </summary>
public static class CenterInteractions
{
    public static IReadOnlyList<InteractionRegistration> Build(int areaId, InteriorTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (template.Kind != InteriorKind.Center)
            throw new ArgumentException("template is not a Pokémon Center", nameof(template));

        var positions = TileGridQuery.PositionsOf(template.Grid, LogicalTile.NpcPost);
        if (positions.Count != 1)
            throw new InvalidOperationException($"Center template has {positions.Count} clerk posts, expected 1");

        return
        [
            new InteractionRegistration
            {
                Id = $"center:{areaId}",
                AreaId = areaId,
                Position = positions[0],
                Kind = InteractionKind.Heal,
                Text = "We'll take care of your Pokémon!",
            },
        ];
    }
}
