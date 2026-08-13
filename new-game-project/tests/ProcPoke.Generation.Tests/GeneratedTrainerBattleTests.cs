using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Overworld;
using Xunit;

namespace ProcPoke.Generation.Tests;

public sealed class GeneratedTrainerBattleTests
{
    private static GeneratedRegion Region(ulong seed = 42)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = 8 }, TestData.Data);

    private static OverworldSession Session(GeneratedRegion region)
        => new(region, new PlayerIdentity(CharacterChoice.CharacterA, "Leaf"));

    [Theory]
    [InlineData("Bug Catcher", 12)]
    [InlineData("Youngster", 16)]
    [InlineData("Hiker", 32)]
    [InlineData("Grunt", 40)]
    [InlineData("Ace Trainer", 60)]
    [InlineData("Veteran", 100)]
    [InlineData("Gentleman", 200)]
    public void PayoutTablePinsClassBaseRates(string trainerClass, int expectedRate)
        => Assert.Equal(expectedRate, TrainerPayout.BaseRateFor(trainerClass));

    [Fact]
    public void GeneratedTrainerBuildsBattleRequestFromItsPlannedRosterAndPayout()
    {
        var region = Region();
        var registry = InteractionRegistry.Build(region);
        var trainer = region.Graph.Areas
            .SelectMany(area => registry.Of(area.Id))
            .First(interaction => interaction.Kind == InteractionKind.Trainer);
        var planned = Assert.IsType<ProcPoke.Generation.Trainers.TrainerEncounter>(trainer.Trainer);
        var returnState = new PlayerState(new GridPosition(3, 4), Facing.West);

        var request = Session(region).TryStartTrainerBattle(trainer, returnState);

        Assert.NotNull(request);
        Assert.Equal(trainer.Id, request!.TrainerId);
        Assert.Equal(planned.DisplayClass, request.TrainerName);
        Assert.Equal(returnState, request.ReturnState);
        Assert.Equal(
            planned.Roster.Select(member => (member.SpeciesId, member.Level)),
            request.Roster.Select(member => (member.SpeciesId, member.Level)));
        Assert.Equal(TrainerPayout.BaseRateFor(planned.ClassName) * planned.AceLevel, request.PrizeMoney);
    }

    [Fact]
    public void VictoryAwardsMoneyAndMakesTrainerOneTimeButLossDoesNeither()
    {
        var region = Region(7);
        var registry = InteractionRegistry.Build(region);
        var trainer = region.Graph.Areas
            .SelectMany(area => registry.Of(area.Id))
            .First(interaction => interaction.Kind == InteractionKind.Trainer);
        var session = Session(region);
        var request = Assert.IsType<GeneratedTrainerBattleTransitionRequest>(
            session.TryStartTrainerBattle(trainer, new PlayerState(new GridPosition(1, 1))));

        Assert.Equal(0, session.Money);
        Assert.False(session.HasDefeatedTrainer(trainer.Id));
        Assert.NotNull(session.TryStartTrainerBattle(trainer, request.ReturnState));

        Assert.True(session.CompleteTrainerBattle(request));
        Assert.Equal(request.PrizeMoney, session.Money);
        Assert.True(session.HasDefeatedTrainer(trainer.Id));
        Assert.Null(session.TryStartTrainerBattle(trainer, request.ReturnState));

        Assert.False(session.CompleteTrainerBattle(request));
        Assert.Equal(request.PrizeMoney, session.Money);
    }

    [Fact]
    public void SightlineRequiresAnUnobstructedRowOrColumnAndPicksNearestTrainer()
    {
        var grid = new TileGrid(9, 7, LogicalTile.Ground);
        var far = TrainerAt("trainer:far", 7, 3);
        var near = TrainerAt("trainer:near", 4, 3);
        var vertical = TrainerAt("trainer:vertical", 2, 1);

        Assert.Equal(near, TrainerSightlines.FindEngaging(
            [far, near, vertical], grid, new GridPosition(2, 3), new HashSet<string>()));

        grid[3, 3] = LogicalTile.Wall;
        Assert.Null(TrainerSightlines.FindEngaging(
            [far, near], grid, new GridPosition(2, 3), new HashSet<string>()));

        Assert.Equal(vertical, TrainerSightlines.FindEngaging(
            [vertical], grid, new GridPosition(2, 5), new HashSet<string>()));
        Assert.Null(TrainerSightlines.FindEngaging(
            [vertical], grid, new GridPosition(2, 5), new HashSet<string> { vertical.Id }));
    }

    private static InteractionRegistration TrainerAt(string id, int x, int y) => new()
    {
        Id = id,
        AreaId = 1,
        Position = new GridPosition(x, y),
        Kind = InteractionKind.Trainer,
        Text = "Battle!",
        TrainerClass = "Ace Trainer",
    };
}
