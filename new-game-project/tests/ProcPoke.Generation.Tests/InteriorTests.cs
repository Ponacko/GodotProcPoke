using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Topology;
using ProcPoke.Overworld;
using Xunit;

namespace ProcPoke.Generation.Tests;

public sealed class InteriorTests
{
    [Theory]
    [InlineData(2UL)]
    [InlineData(42UL)]
    [InlineData(777UL)]
    public void GeneratedTownsExposeCenterAndMartDoorsThatRoundTrip(ulong seed)
    {
        var region = RegionGenerator.Generate(
            new GenerationSettings { Seed = seed, BadgeCount = 8 }, TestData.Data);
        var towns = region.Graph.Areas.Where(area =>
            area.Archetype is AreaArchetype.StartTown or AreaArchetype.Town).ToArray();

        Assert.NotEmpty(towns);
        foreach (var town in towns)
        {
            var carved = region.Carved[town.Id];
            Assert.Contains(carved.BuildingEntrances, entrance => entrance.Kind == BuildingKind.Center);
            Assert.Contains(carved.BuildingEntrances, entrance => entrance.Kind == BuildingKind.Mart);

            foreach (var entrance in carved.BuildingEntrances)
            {
                Assert.Equal(LogicalTile.Warp, carved.Grid[entrance.X, entrance.Y]);
                var streetFacing = InteriorTransitionResolver.FacingFor(entrance.StreetEdge);
                var entered = InteriorTransitionResolver.Enter(
                    carved,
                    entrance,
                    new GridPosition(entrance.X, entrance.Y),
                    InteriorTransitionResolver.Opposite(streetFacing));

                Assert.NotNull(entered);
                Assert.True(entered!.Template.Grid[entered.InteriorPosition.X, entered.InteriorPosition.Y].IsWalkable());
                var exited = InteriorTransitionResolver.Exit(
                    town.Id,
                    entrance,
                    entered.Template,
                    entered.InteriorPosition,
                    streetFacing);

                Assert.NotNull(exited);
                Assert.Equal(town.Id, exited!.OverworldAreaId);
                Assert.Equal(new GridPosition(entrance.X, entrance.Y), exited.OverworldPosition);
            }
        }
    }

    [Fact]
    public void FixedTemplatesCoverAllInteriorCategories()
    {
        var buildingKinds = Enum.GetValues<BuildingKind>();
        foreach (var kind in buildingKinds)
        {
            var template = InteriorTemplateCatalog.For(kind);
            Assert.Equal(kind switch
            {
                BuildingKind.Center => InteriorKind.Center,
                BuildingKind.Mart => InteriorKind.Mart,
                BuildingKind.Gym => InteriorKind.Gym,
                _ => InteriorKind.House,
            }, template.Kind);
            Assert.Equal(LogicalTile.Warp, template.Grid[template.Door.X, template.Door.Y]);
            Assert.True(template.Grid[template.Arrival.X, template.Arrival.Y].IsWalkable());
        }

        Assert.Equal(InteriorKind.Cave, InteriorTemplateCatalog.For(AreaArchetype.StandardCave).Kind);
        Assert.Equal(InteriorKind.Tower, InteriorTemplateCatalog.For(AreaArchetype.Tower).Kind);
        Assert.Equal(InteriorKind.Hideout, InteriorTemplateCatalog.For(AreaArchetype.VillainHideout).Kind);
    }

    [Fact]
    public void GeneratedSeedsExerciseAllTownAndDungeonInteriorCategories()
    {
        var seen = new HashSet<InteriorKind>();
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var region = RegionGenerator.Generate(
                new GenerationSettings { Seed = seed, BadgeCount = 8 }, TestData.Data);
            foreach (var area in region.Graph.Areas)
            {
                if (area.Archetype is AreaArchetype.StartTown or AreaArchetype.Town)
                    seen.UnionWith(region.Carved[area.Id].BuildingEntrances
                        .Select(entrance => InteriorTemplateCatalog.For(entrance.Kind).Kind));
                if (area.Archetype is AreaArchetype.StandardCave or AreaArchetype.MountainPath
                    or AreaArchetype.DeepCave or AreaArchetype.VictoryRoad or AreaArchetype.Tower
                    or AreaArchetype.VillainHideout)
                    seen.Add(InteriorTemplateCatalog.For(area.Archetype).Kind);
            }
        }

        Assert.Contains(InteriorKind.Center, seen);
        Assert.Contains(InteriorKind.Mart, seen);
        Assert.Contains(InteriorKind.House, seen);
        Assert.Contains(InteriorKind.Gym, seen);
        Assert.Contains(InteriorKind.Cave, seen);
        Assert.Contains(InteriorKind.Tower, seen);
        Assert.Contains(InteriorKind.Hideout, seen);
    }
}
