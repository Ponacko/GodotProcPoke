using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Topology;
using ProcPoke.Overworld;
using Xunit;

namespace ProcPoke.Generation.Tests;

public sealed class InteractionTests
{
    private static GeneratedRegion Generate(ulong seed = 42)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = 8 }, TestData.Data);

    [Fact]
    public void RegistryCoversGeneratedPostsTrainersItemsAndGates()
    {
        var region = Generate();
        var registry = InteractionRegistry.Build(region);

        foreach (var area in region.Graph.Areas)
        {
            var interactions = registry.Of(area.Id);
            Assert.Equal(region.Npcs.Of(area.Id).Count, interactions.Count(i =>
                i.Kind is InteractionKind.Npc or InteractionKind.Sign));
            Assert.Equal(region.Trainers.TrainersOf(area.Id).Count(t => t.Position is not null),
                interactions.Count(i => i.Kind == InteractionKind.Trainer));
            Assert.Equal(region.Trainers.ItemsOf(area.Id).Count,
                interactions.Count(i => i.Kind == InteractionKind.Item));
        }

        Assert.Equal(region.Gating.Gates.Sum(gate => region.Carved[
            region.Graph.Areas.Single(area => area.PathIndex == gate.BlockPathIndex).Id].GateTiles.Count),
            region.Graph.Areas.SelectMany(area => registry.GatesOf(area.Id)).Count());
        Assert.All(registry.Of(region.Graph.Areas[0].Id), interaction =>
            Assert.False(string.IsNullOrWhiteSpace(interaction.Text)));
    }

    [Fact]
    public void NpcAndSignTextIsTalkableWithoutChangingTheMap()
    {
        var region = Generate(2026);
        var registry = InteractionRegistry.Build(region);
        var post = region.Graph.Areas
            .SelectMany(area => registry.Of(area.Id))
            .First(interaction => interaction.Kind is InteractionKind.Npc or InteractionKind.Sign);
        var before = region.Carved[post.AreaId].Grid[post.Position.X, post.Position.Y];

        var outcome = new InteractionSession().Interact(post);

        Assert.Equal(InteractionOutcomeKind.Dialogue, outcome.Kind);
        Assert.Equal(post.Text, outcome.Message);
        Assert.Equal(before, region.Carved[post.AreaId].Grid[post.Position.X, post.Position.Y]);
    }

    [Fact]
    public void ItemCollectionIsOneTimePerSessionAndAddsToTheBag()
    {
        var region = Generate();
        var registry = InteractionRegistry.Build(region);
        var item = region.Graph.Areas
            .SelectMany(area => registry.Of(area.Id))
            .First(interaction => interaction.Kind == InteractionKind.Item);
        var session = new InteractionSession();

        Assert.Equal(InteractionOutcomeKind.ItemCollected, session.Interact(item).Kind);
        Assert.True(session.Bag.Has(item.ItemId!.Value));
        Assert.Equal(1, session.Bag.Count(item.ItemId!.Value));
        Assert.Equal(InteractionOutcomeKind.AlreadyCollected, session.Interact(item).Kind);
        Assert.Equal(1, session.Bag.Count(item.ItemId!.Value));
    }

    [Fact]
    public void GateRequiresItsKeyThenClearsOnlySessionMovementState()
    {
        var region = Generate();
        var registry = InteractionRegistry.Build(region);
        var gate = region.Graph.Areas
            .SelectMany(area => registry.GatesOf(area.Id))
            .First();
        var grid = region.Carved[gate.AreaId].Grid;
        var before = grid[gate.Position.X, gate.Position.Y];
        var session = new InteractionSession();
        var badges = gate.BadgePrerequisite ?? 0;

        Assert.Equal(InteractionOutcomeKind.GateBlocked, session.Interact(gate, badges).Kind);
        session.GrantKey(gate.KeyName!);
        Assert.Equal(InteractionOutcomeKind.GateUnlocked, session.Interact(gate, badges).Kind);
        Assert.True(session.ForMovement(gate.AreaId, new MovementCapabilities(), registry, region.Gating, badges)
            .ClearedGatePositions.Contains(gate.Position));
        Assert.Equal(before, grid[gate.Position.X, gate.Position.Y]);
    }

    [Fact]
    public void HmGateAlsoRequiresItsBadgePrerequisite()
    {
        var region = Generate();
        var registry = InteractionRegistry.Build(region);
        var hmGate = region.Gating.Gates.First(gate => gate.BadgePrerequisite is > 0);
        var interaction = region.Graph.Areas
            .SelectMany(area => registry.GatesOf(area.Id))
            .First(candidate => candidate.GateId == hmGate.Id);
        var session = new InteractionSession();
        session.GrantKey(hmGate.KeyName);

        Assert.Equal(InteractionOutcomeKind.GateBlocked,
            session.Interact(interaction, hmGate.BadgePrerequisite!.Value - 1).Kind);
        Assert.Equal(InteractionOutcomeKind.GateUnlocked,
            session.Interact(interaction, hmGate.BadgePrerequisite!.Value).Kind);
    }

    [Fact]
    public void ForMovementDerivesFieldMoveCapabilitiesFromKeysAndBadges()
    {
        var region = Generate();
        var registry = InteractionRegistry.Build(region);
        var cutGate = region.Gating.Gates.Single(gate => gate.KeyName == "Cut");
        var session = new InteractionSession();

        Assert.False(session.ForMovement(
            region.Graph.StartAreaId, new MovementCapabilities(), registry, region.Gating,
            cutGate.BadgePrerequisite!.Value).HasCut);

        session.GrantKey("Cut");
        Assert.False(session.ForMovement(
            region.Graph.StartAreaId, new MovementCapabilities(), registry, region.Gating,
            cutGate.BadgePrerequisite!.Value - 1).HasCut);
        Assert.True(session.ForMovement(
            region.Graph.StartAreaId, new MovementCapabilities(), registry, region.Gating,
            cutGate.BadgePrerequisite!.Value).HasCut);

        Assert.False(session.ForMovement(
            region.Graph.StartAreaId, new MovementCapabilities(), registry, region.Gating, 0).CanBicycle);
        session.GrantKey("Bicycle");
        Assert.True(session.ForMovement(
            region.Graph.StartAreaId, new MovementCapabilities(), registry, region.Gating, 0).CanBicycle);
    }

    [Fact]
    public void DebugUnlockClearsGatesAndRevealsDarkCavesWithoutMapMutation()
    {
        var region = Generate(7);
        var registry = InteractionRegistry.Build(region);
        var gate = region.Graph.Areas
            .SelectMany(area => registry.GatesOf(area.Id))
            .First();
        var before = region.Carved[gate.AreaId].Grid[gate.Position.X, gate.Position.Y];
        var session = new InteractionSession();
        session.EnableDebugUnlock();

        Assert.Equal(InteractionOutcomeKind.GateUnlocked, session.Interact(gate).Kind);
        Assert.True(session.ForMovement(gate.AreaId, new MovementCapabilities(), registry, region.Gating, 0)
            .DebugUnlock);
        Assert.Equal(before, region.Carved[gate.AreaId].Grid[gate.Position.X, gate.Position.Y]);

        var dark = DarkCaveVision.For(darkArea: true, hasFlash: false, debugUnlock: false, radius: 2);
        Assert.True(DarkCaveVision.IsVisible(dark, new GridPosition(4, 4), new GridPosition(6, 4)));
        Assert.False(DarkCaveVision.IsVisible(dark, new GridPosition(4, 4), new GridPosition(7, 4)));
        Assert.False(DarkCaveVision.For(true, hasFlash: true, debugUnlock: false).IsDark);
        Assert.False(DarkCaveVision.For(true, hasFlash: false, debugUnlock: true).IsDark);
        Assert.True(new InteractionSession().VisionFor(true).IsDark);
        var flashed = new InteractionSession();
        flashed.GrantKey("Flash");
        Assert.False(flashed.VisionFor(true).IsDark);
    }
}
