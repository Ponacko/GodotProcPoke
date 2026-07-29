using ProcPoke.Generation;
using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Roster;
using ProcPoke.Generation.Topology;
using ProcPoke.Generation.Trainers;
using Xunit;

namespace ProcPoke.Generation.Tests;

public class TrainerPassTests
{
    private static GeneratedRegion Generate(ulong seed, int badges = 8)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void CarvedMarkersAreFullyPopulatedAcrossCorpus(int badges)
    {
        for (ulong seed = 1; seed <= 100; seed++)
        {
            var region = Generate(seed, badges);
            foreach (var area in region.Graph.Areas)
            {
                var carved = region.Carved[area.Id];
                var plan = region.Trainers.TrainersOf(area.Id);
                var posts = RowMajor(carved.Grid, LogicalTile.TrainerPost).ToList();
                var postTrainers = plan.Where(t => !t.IsGymTrainer).ToList();
                Assert.Equal(posts.Count, postTrainers.Count);
                Assert.Equal(posts, postTrainers.Select(t => t.Position!.Value));

                var items = region.Trainers.ItemsOf(area.Id);
                Assert.Equal(carved.Grid.Count(LogicalTile.ItemBall), items.Count);
                Assert.All(items, item => Assert.Contains(item.ItemId, TestData.Data.Items.Keys));
                Assert.All(plan, trainer =>
                {
                    Assert.NotEmpty(trainer.Roster);
                    Assert.All(trainer.Roster, member =>
                        Assert.Contains(member.SpeciesId, region.Dex.Entries.Select(e => e.SpeciesId)));
                });

                if (region.Identity.GymTypes.ContainsKey(area.Id))
                    Assert.Equal(2, plan.Count(t => t.IsGymTrainer));
            }
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void TrainerClassesFollowPinnedArchetypeRules(int badges)
    {
        for (ulong seed = 1; seed <= 60; seed++)
        {
            var region = Generate(seed, badges);
            foreach (var area in region.Graph.Areas)
            {
                foreach (var trainer in region.Trainers.TrainersOf(area.Id).Where(t => !t.IsGymTrainer))
                {
                    if (area.Archetype == AreaArchetype.VictoryRoad) Assert.Equal("Veteran", trainer.ClassName);
                    if (area.Archetype == AreaArchetype.Tower) Assert.Equal("Psychic", trainer.ClassName);
                    if (area.Archetype is AreaArchetype.StandardCave or AreaArchetype.MountainPath or AreaArchetype.DeepCave)
                        Assert.Equal("Hiker", trainer.ClassName);
                    if (area.Archetype == AreaArchetype.Forest || region.Biomes.Of(area.Id) == Biome.Forest)
                        Assert.Equal("Bug Catcher", trainer.ClassName);
                    if (area.Archetype == AreaArchetype.VillainHideout)
                        Assert.EndsWith(" Grunt", trainer.DisplayClass);
                }

                Assert.All(region.Trainers.TrainersOf(area.Id).Where(t => t.IsGymTrainer),
                    trainer => Assert.Equal("Ace Trainer", trainer.ClassName));
            }
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void TrainerLevelsProgressWithAvailability(int badges)
    {
        for (ulong seed = 1; seed <= 100; seed++)
        {
            var region = Generate(seed, badges);
            var previousAce = 0;
            foreach (var area in region.Graph.CriticalPath)
            {
                var trainers = region.Trainers.TrainersOf(area.Id);
                if (trainers.Count == 0) continue;
                var ace = trainers.Max(t => t.AceLevel);
                Assert.True(ace + 4 >= previousAce,
                    $"seed {seed}/{badges}: {area.Archetype} ace {ace} fell below prior {previousAce}");
                previousAce = Math.Max(previousAce, ace);
            }
        }
    }

    [Fact]
    public void TrainersAreDeterministic()
    {
        foreach (var seed in new ulong[] { 7, 42, 999 })
        {
            var a = Generate(seed).Trainers;
            var b = Generate(seed).Trainers;
            Assert.Equal(a.ByArea.Keys.OrderBy(id => id), b.ByArea.Keys.OrderBy(id => id));
            foreach (var areaId in a.ByArea.Keys)
            {
                var left = a.TrainersOf(areaId);
                var right = b.TrainersOf(areaId);
                Assert.Equal(left.Count, right.Count);
                for (var i = 0; i < left.Count; i++)
                {
                    Assert.Equal(left[i].AreaId, right[i].AreaId);
                    Assert.Equal(left[i].Position, right[i].Position);
                    Assert.Equal(left[i].ClassName, right[i].ClassName);
                    Assert.Equal(left[i].TeamName, right[i].TeamName);
                    Assert.Equal(left[i].IsGymTrainer, right[i].IsGymTrainer);
                    Assert.Equal(left[i].Roster.Select(m => (m.SpeciesId, m.Level)),
                        right[i].Roster.Select(m => (m.SpeciesId, m.Level)));
                }
                Assert.Equal(a.ItemsOf(areaId), b.ItemsOf(areaId));
            }
        }
    }

    [Fact]
    public void ItemBallsUseThePinnedProgressionPools()
    {
        var region = Generate(42);
        var order = AvailabilityOrder.Of(region.Graph, GatingGenerator.OffSpineAnchors(region.Graph));
        foreach (var (areaId, index) in order.Select((id, index) => (id, index)))
        {
            var fraction = order.Count <= 1 ? 1 : index / (order.Count - 1.0);
            foreach (var placement in region.Trainers.ItemsOf(areaId))
            {
                var item = TestData.Data.Items[placement.ItemId];
                if (item.Category == "all-machines") continue;
                if (fraction < 1.0 / 3)
                {
                    Assert.Equal("healing", item.Category);
                    Assert.InRange(item.Cost, 0, 700);
                }
                else if (fraction < 2.0 / 3)
                    Assert.True((item.Category == "healing" && item.Cost <= 2000)
                        || item.Category == "standard-balls");
                else
                    Assert.Contains(item.Category, new[] { "revival", "held-items" });
            }
        }
    }

    [Fact]
    public void TrainerSummaryIsPrinted()
    {
        var region = Generate(42);
        var text = ProcPoke.Generation.Debug.RegionGraphText.Render(
            region.Graph, trainers: region.Trainers);
        Assert.Contains("Trainers & items:", text);
        Assert.Contains("items[", text);
    }

    private static IEnumerable<(int X, int Y)> RowMajor(TileGrid grid, LogicalTile tile)
    {
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
                if (grid[x, y] == tile) yield return (x, y);
    }
}
