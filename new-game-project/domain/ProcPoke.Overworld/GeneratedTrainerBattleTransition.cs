using ProcPoke.Battle;
using ProcPoke.Generation.Carving;

namespace ProcPoke.Overworld;

/// <summary>
/// Immutable hand-off for a generated trainer standing on a carved <see cref="LogicalTile.TrainerPost"/>.
/// The stable interaction id doubles as the session defeat flag, so returning to the map cannot retrigger
/// a trainer whose battle has already paid out.
/// </summary>
/// <param name="IsBoss">Whether this trainer fights at <see cref="BattleAiTier.Boss"/> (CONTEXT.md's AI
/// Tier): gym leaders, the Elite Four, the Champion, and a villain hideout's boss.</param>
public sealed record GeneratedTrainerBattleTransitionRequest(
    string TrainerId,
    string TrainerName,
    PlayerState ReturnState,
    IReadOnlyList<TrainerRosterMember> Roster,
    int PrizeMoney,
    GymVictoryReward? GymReward = null,
    LeagueVictoryReward? LeagueReward = null,
    bool IsBoss = false)
{
    public BattleAiTier AiTier => IsBoss ? BattleAiTier.Boss : BattleAiTier.Trainer;
}

public sealed record GymVictoryReward(int AreaId, int BadgeNumber);

public sealed record LeagueVictoryReward(int AreaId, int Stage);

/// <summary>Gen-5-style prize money: trainer-class base rate multiplied by the trainer's ace level.</summary>
public static class TrainerPayout
{
    public static int BaseRateFor(string className) => className switch
    {
        "Bug Catcher" => 12,
        "Youngster" or "Lass" or "Swimmer" => 16,
        "Hiker" or "Fisherman" or "Psychic" => 32,
        "Grunt" => 40,
        "Ace Trainer" => 60,
        "Gym Leader" => 100,
        "Elite Four" => 120,
        "Champion" => 150,
        "Veteran" => 100,
        "Gentleman" or "Lady" => 200,
        _ => 32,
    };
}

/// <summary>Pure straight-line engagement rules shared by the runtime controller and headless tests.</summary>
public static class TrainerSightlines
{
    public static InteractionRegistration? FindEngaging(
        IEnumerable<InteractionRegistration> interactions,
        TileGrid grid,
        GridPosition player,
        IReadOnlySet<string> defeatedTrainerIds)
    {
        ArgumentNullException.ThrowIfNull(interactions);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(defeatedTrainerIds);

        return interactions
            .Where(interaction => interaction.Kind == InteractionKind.Trainer
                && !defeatedTrainerIds.Contains(interaction.Id)
                && (interaction.Position == player
                    || (interaction.StartsOnSight && HasClearSightline(grid, interaction.Position, player))))
            .OrderBy(interaction => Distance(interaction.Position, player))
            .ThenBy(interaction => interaction.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static bool HasClearSightline(TileGrid grid, GridPosition trainer, GridPosition player)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (!grid.InBounds(trainer.X, trainer.Y) || !grid.InBounds(player.X, player.Y)) return false;
        if (trainer == player) return true;
        if (trainer.X != player.X && trainer.Y != player.Y) return false;

        var dx = Math.Sign(player.X - trainer.X);
        var dy = Math.Sign(player.Y - trainer.Y);
        for (var cursor = new GridPosition(trainer.X + dx, trainer.Y + dy);
             cursor != player;
             cursor = new GridPosition(cursor.X + dx, cursor.Y + dy))
        {
            if (!grid[cursor.X, cursor.Y].IsWalkable()) return false;
        }
        return true;
    }

    private static int Distance(GridPosition left, GridPosition right)
        => Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y);
}
