using ProcPoke.Battle;
using ProcPoke.Data;
using Xunit;

namespace ProcPoke.Battle.Tests;

public sealed class EvolutionResolverTests
{
    private static readonly GameData Data = GameDataLoader.Load(FindDataDir());

    [Fact]
    public void LevelEvolutionRequiresItsMinimumLevel()
    {
        Assert.Null(EvolutionResolver.TryResolve(Data, new EvolutionContext
        {
            SpeciesId = 1,
            Level = 15,
        }));

        var result = EvolutionResolver.TryResolve(Data, new EvolutionContext
        {
            SpeciesId = 1,
            Level = 16,
        });

        Assert.Equal(2, result?.ToSpeciesId);
    }

    [Fact]
    public void FriendshipAndTimeOfDayRulesSelectTheMatchingEeveeEvolution()
    {
        var day = EvolutionResolver.TryResolve(Data, new EvolutionContext
        {
            SpeciesId = 133,
            Level = 20,
            Happiness = 160,
            TimeOfDay = "day",
        });
        var night = EvolutionResolver.TryResolve(Data, new EvolutionContext
        {
            SpeciesId = 133,
            Level = 20,
            Happiness = 160,
            TimeOfDay = "night",
        });
        var unhappy = EvolutionResolver.TryResolve(Data, new EvolutionContext
        {
            SpeciesId = 133,
            Level = 20,
            Happiness = 159,
            TimeOfDay = "day",
        });

        Assert.Equal(196, day?.ToSpeciesId);
        Assert.Equal(197, night?.ToSpeciesId);
        Assert.Null(unhappy);
    }

    [Fact]
    public void ItemEvolutionRequiresTheMatchingItem()
    {
        var result = EvolutionResolver.TryResolve(Data, new EvolutionContext
        {
            SpeciesId = 133,
            Level = 5,
            Trigger = EvolutionTrigger.UseItem,
            ItemId = 82,
        });

        Assert.Equal(136, result?.ToSpeciesId);
    }

    [Fact]
    public void LinkCableEvolutionPreservesHeldItemRequirements()
    {
        Assert.Equal(65, EvolutionResolver.TryResolve(Data, new EvolutionContext
        {
            SpeciesId = 64,
            Level = 30,
            Trigger = EvolutionTrigger.LinkCable,
        })?.ToSpeciesId);

        Assert.Null(EvolutionResolver.TryResolve(Data, new EvolutionContext
        {
            SpeciesId = 137,
            Level = 30,
            Trigger = EvolutionTrigger.LinkCable,
        }));
        Assert.Equal(233, EvolutionResolver.TryResolve(Data, new EvolutionContext
        {
            SpeciesId = 137,
            Level = 30,
            Trigger = EvolutionTrigger.LinkCable,
            HeldItemId = 229,
        })?.ToSpeciesId);
    }

    [Fact]
    public void ConditionalLevelRulesUseStatsMovesAndLocations()
    {
        var hitmonlee = EvolutionResolver.TryResolve(Data, new EvolutionContext
        {
            SpeciesId = 236,
            Level = 20,
            Attack = 20,
            Defense = 10,
        });
        var hitmonchan = EvolutionResolver.TryResolve(Data, new EvolutionContext
        {
            SpeciesId = 236,
            Level = 20,
            Attack = 10,
            Defense = 20,
        });
        var leafeon = EvolutionResolver.TryResolve(Data, new EvolutionContext
        {
            SpeciesId = 133,
            Level = 5,
            LocationId = 375,
            NearSpecialRock = true,
        });

        Assert.Equal(106, hitmonlee?.ToSpeciesId);
        Assert.Equal(107, hitmonchan?.ToSpeciesId);
        Assert.Equal(470, leafeon?.ToSpeciesId);
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
