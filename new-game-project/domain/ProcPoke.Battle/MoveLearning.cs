using ProcPoke.Data;

namespace ProcPoke.Battle;

/// <summary>
/// Result of crossing one or more level thresholds. Pending moves are not silently discarded when
/// all four slots are occupied; the caller presents those choices to the player.
/// </summary>
public sealed record MoveLearningResult(
    IReadOnlyList<int> MoveIds,
    IReadOnlyList<int> LearnedMoveIds,
    IReadOnlyList<int> PendingMoveIds);

/// <summary>Resolves the level-up portion of the baked Gen 5 learnsets.</summary>
public static class MoveLearningResolver
{
    private const int MoveSlotCount = 4;

    public static MoveLearningResult Resolve(
        GameData data,
        int speciesId,
        int previousLevel,
        int newLevel,
        IReadOnlyList<int> knownMoveIds)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(knownMoveIds);
        if (!data.Species.ContainsKey(speciesId))
            throw new ArgumentException($"Unknown species {speciesId}.", nameof(speciesId));
        if (previousLevel is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(previousLevel));
        if (newLevel is < 1 or > 100 || newLevel < previousLevel)
            throw new ArgumentOutOfRangeException(nameof(newLevel));
        if (knownMoveIds.Count > MoveSlotCount)
            throw new ArgumentException("A Pokémon cannot know more than four moves.", nameof(knownMoveIds));
        if (knownMoveIds.Distinct().Count() != knownMoveIds.Count)
            throw new ArgumentException("Known moves must be unique.", nameof(knownMoveIds));
        if (knownMoveIds.Any(moveId => !data.Moves.ContainsKey(moveId)))
            throw new ArgumentException("Known moves must exist in baked game data.", nameof(knownMoveIds));

        var moveIds = knownMoveIds.ToList();
        var learned = new List<int>();
        var pending = new List<int>();
        var known = knownMoveIds.ToHashSet();
        var entries = data.Learnsets.GetValueOrDefault(speciesId, [])
            .Where(entry => entry.Method == LearnMethod.LevelUp
                && entry.Level > previousLevel
                && entry.Level <= newLevel)
            .OrderBy(entry => entry.Level)
            .ThenBy(entry => entry.MoveId);

        foreach (var entry in entries)
        {
            if (!data.Moves.ContainsKey(entry.MoveId) || !known.Add(entry.MoveId)) continue;
            if (moveIds.Count < MoveSlotCount)
            {
                moveIds.Add(entry.MoveId);
                learned.Add(entry.MoveId);
            }
            else
            {
                pending.Add(entry.MoveId);
            }
        }

        return new MoveLearningResult(moveIds, learned, pending);
    }
}
