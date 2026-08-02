using ProcPoke.Battle;
using ProcPoke.Data;
using Xunit;

namespace ProcPoke.Battle.Tests;

public sealed class MoveLearningTests
{
    private static readonly GameData Data = GameDataLoader.Load(FindDataDir());

    [Fact]
    public void LevelCrossingAddsNewLevelUpMoveInLearnsetOrder()
    {
        var result = MoveLearningResolver.Resolve(Data, speciesId: 1, previousLevel: 5, newLevel: 7, [33, 45]);

        Assert.Equal(new[] { 33, 45, 73 }, result.MoveIds);
        Assert.Equal(new[] { 73 }, result.LearnedMoveIds);
        Assert.Empty(result.PendingMoveIds);
    }

    [Fact]
    public void InitialLevelOneResolutionLearnsAllAvailableMovesUntilSlotsFill()
    {
        var result = MoveLearningResolver.Resolve(Data, speciesId: 4, previousLevel: 0, newLevel: 1, []);

        Assert.Equal(new[] { 10, 45 }, result.MoveIds);
        Assert.Equal(result.MoveIds, result.LearnedMoveIds);
    }

    [Fact]
    public void FullMoveSlotsReturnOverflowAsPendingChoices()
    {
        var result = MoveLearningResolver.Resolve(Data, speciesId: 1, previousLevel: 12, newLevel: 13,
            knownMoveIds: [33, 45, 73, 22]);

        Assert.Equal(new[] { 33, 45, 73, 22 }, result.MoveIds);
        Assert.Equal(new[] { 77, 79 }, result.PendingMoveIds);
        Assert.Empty(result.LearnedMoveIds);
    }

    [Fact]
    public void AlreadyKnownMoveIsNotOfferedAgain()
    {
        var result = MoveLearningResolver.Resolve(Data, speciesId: 1, previousLevel: 0, newLevel: 7, [33, 45]);

        Assert.Equal(new[] { 33, 45, 73 }, result.MoveIds);
        Assert.Equal(new[] { 73 }, result.LearnedMoveIds);
    }

    private static string FindDataDir()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ProcPoke.slnx")))
                return Path.Combine(directory.FullName, "data");
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate the repository data directory.");
    }
}
