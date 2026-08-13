using ProcPoke.Generation;
using ProcPoke.Generation.Carving;

namespace ProcPoke.Overworld;

/// <summary>Binds a generated gym city's two trainer teams and leader team to the fixed gym template.</summary>
public static class GymInteriorBattles
{
    public static IReadOnlyList<InteractionRegistration> Build(
        GeneratedRegion region, int areaId, InteriorTemplate template)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(template);
        if (template.Kind != InteriorKind.Gym)
            throw new ArgumentException("template is not a gym", nameof(template));
        if (!region.Bosses.GymLeaders.TryGetValue(areaId, out var leader)) return [];

        var positions = PositionsOf(template.Grid, LogicalTile.TrainerPost);
        var trainers = region.Trainers.TrainersOf(areaId)
            .Where(trainer => trainer.IsGymTrainer)
            .ToArray();
        if (positions.Count != trainers.Length + 1)
            throw new InvalidOperationException(
                $"gym area {areaId} has {positions.Count} posts for {trainers.Length} trainers plus its leader");

        var leaderPosition = positions.OrderBy(position => position.Y).ThenBy(position => position.X).First();
        var interactions = new List<InteractionRegistration>
        {
            new()
            {
                Id = $"gym-leader:{areaId}",
                AreaId = areaId,
                Position = leaderPosition,
                Kind = InteractionKind.Trainer,
                Text = $"{leader.Name} challenges you.",
                TrainerClass = "Gym Leader",
                GymLeader = leader,
                StartsOnSight = false,
            },
        };

        var trainerPositions = positions.Where(position => position != leaderPosition)
            .OrderBy(position => position.Y).ThenBy(position => position.X).ToArray();
        for (var i = 0; i < trainers.Length; i++)
        {
            var trainer = trainers[i];
            interactions.Add(new InteractionRegistration
            {
                Id = $"gym-trainer:{areaId}:{i}",
                AreaId = areaId,
                Position = trainerPositions[i],
                Kind = InteractionKind.Trainer,
                Text = $"{trainer.DisplayClass} challenges you.",
                TrainerClass = trainer.DisplayClass,
                Trainer = trainer,
            });
        }

        return interactions;
    }

    private static IReadOnlyList<GridPosition> PositionsOf(TileGrid grid, LogicalTile tile)
        => Enumerable.Range(0, grid.Height)
            .SelectMany(y => Enumerable.Range(0, grid.Width)
                .Where(x => grid[x, y] == tile)
                .Select(x => new GridPosition(x, y)))
            .ToArray();
}
