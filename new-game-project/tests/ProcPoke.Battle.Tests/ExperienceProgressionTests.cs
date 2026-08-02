using ProcPoke.Battle;
using ProcPoke.Data;
using Xunit;

namespace ProcPoke.Battle.Tests;

public sealed class ExperienceProgressionTests
{
    private static readonly GameData Data = GameDataLoader.Load(FindDataDir());

    [Fact]
    public void ScaledExperienceMatchesGen5BaseAndTrainerMultipliers()
    {
        Assert.Equal(65, ExperienceCalculator.ForDefeat(64, 5, 5));
        Assert.Equal(97, ExperienceCalculator.ForDefeat(64, 5, 5, trainerBattle: true));
        Assert.Equal(33, ExperienceCalculator.ForDefeat(64, 5, 5, participants: 2));
    }

    [Fact]
    public void LevelLookupUsesTheSpeciesGrowthCurve()
    {
        var growth = Data.GrowthRates["medium"];

        Assert.Equal(1, ExperienceCalculator.LevelAt(growth, 0));
        Assert.Equal(5, ExperienceCalculator.LevelAt(growth, growth.CumulativeExperience[5]));
        Assert.Equal(5, ExperienceCalculator.LevelAt(growth, growth.CumulativeExperience[6] - 1));
        Assert.Equal(100, ExperienceCalculator.LevelAt(growth, int.MaxValue));
    }

    [Fact]
    public void DefeatRewardAdvancesExperienceLevelAndEvYield()
    {
        var growth = Data.GrowthRates["medium-slow"];
        var current = new ExperienceProgress
        {
            TotalExperience = growth.CumulativeExperience[5],
            EffortValues = new StatBlock(0, 0, 0, 0, 0, 0),
        };

        var result = ExperienceProgression.ApplyDefeat(
            Data, 1, current, defeatedSpeciesId: 4, defeatedLevel: 20);

        Assert.True(result.ExperienceGained > 0);
        Assert.Equal(5, result.PreviousLevel);
        Assert.Equal(result.NewLevel, ExperienceCalculator.LevelAt(growth, result.Progress.TotalExperience));
        Assert.Equal(1, result.Progress.EffortValues.Speed);
        Assert.Equal(current.TotalExperience + result.ExperienceGained, result.Progress.TotalExperience);
    }

    [Fact]
    public void EffortValuesRespectPerStatAndTotalCaps()
    {
        var current = new ExperienceProgress
        {
            TotalExperience = 0,
            EffortValues = new StatBlock(0, 0, 0, 0, 0, 0),
        };

        for (var i = 0; i < 228; i++)
        {
            var species = i < 100 ? 150 : 25;
            current = ExperienceProgression.ApplyDefeat(
                Data, 1, current, species, defeatedLevel: 5).Progress;
        }

        Assert.Equal(255, current.EffortValues.SpecialAttack);
        Assert.Equal(255, current.EffortValues.Speed);
        Assert.Equal(510, current.EffortValues.Hp + current.EffortValues.Attack
            + current.EffortValues.Defense + current.EffortValues.SpecialAttack
            + current.EffortValues.SpecialDefense + current.EffortValues.Speed);
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
