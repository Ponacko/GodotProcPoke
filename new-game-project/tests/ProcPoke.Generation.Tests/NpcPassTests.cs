using ProcPoke.Generation;
using ProcPoke.Generation.Debug;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Npcs;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>Ticket 8a: graph-level NPC posts, dialogue naming, and pre-gate hint reachability.</summary>
public class NpcPassTests
{
    private static GeneratedRegion Generate(ulong seed, int badges = 8)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void EveryGateHasAReachableNamedHint(int badges)
    {
        var checkedCount = 0;
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var region = Generate(seed, badges);
            var anchors = GatingGenerator.OffSpineAnchors(region.Graph);

            foreach (var gate in region.Gating.Gates)
            {
                var hintable = region.Graph.Areas
                    .Where(a => Position(a, anchors) <= gate.BlockPathIndex)
                    .Select(a => a.Id)
                    .ToHashSet();

                Assert.Contains(gate.HintAreaId, hintable);
                var hints = region.Npcs.Of(gate.HintAreaId).Where(p => p.Kind == NpcKind.Hint).ToList();
                Assert.Contains(hints, hint => hint.Text.Contains(region.Names.Of(gate.KeyAreaId)));
                Assert.All(hints, hint => Assert.DoesNotContain("area#", hint.Text, StringComparison.OrdinalIgnoreCase));
            }
            checkedCount++;
        }

        Assert.True(checkedCount > 0, "empty corpus — nothing was checked");
    }

    [Fact]
    public void FurnitureSignsAndFlavorFollowTheGraph()
    {
        var region = Generate(42);

        foreach (var city in region.Identity.GymTypes.Keys)
        {
            var posts = region.Npcs.Of(city);
            Assert.Contains(posts, p => p.Kind == NpcKind.GymGuide
                                        && p.Text.Contains(region.Names.Of(city)));
            Assert.Contains(posts, p => p.Kind == NpcKind.CenterGossip);
        }

        foreach (var route in region.Graph.Areas.Where(a => a.Archetype == AreaArchetype.Route))
        {
            var signs = region.Npcs.Of(route.Id).Where(p => p.Kind == NpcKind.Sign).ToList();
            Assert.Single(signs);
            Assert.Contains(region.Names.Of(route.Id), signs[0].Text);
        }

        foreach (var town in region.Graph.Areas.Where(a => a.Archetype is AreaArchetype.StartTown or AreaArchetype.Town))
            Assert.InRange(region.Npcs.Of(town.Id).Count(p => p.Kind == NpcKind.Flavor), 1, 2);
    }

    [Fact]
    public void NpcPlanAndDebugTextAreDeterministic()
    {
        var a = Generate(999);
        var b = Generate(999);
        Assert.Equal(Signature(a.Npcs), Signature(b.Npcs));

        var text = RegionGraphText.Render(a.Graph, names: a.Names, npcs: a.Npcs);
        Assert.Contains("NPC posts:", text);
        Assert.Contains("Hint:", text);
        Assert.Contains(a.Names.Of(a.Gating.Gates[0].KeyAreaId), text);
    }

    private static int Position(Area area, IReadOnlyDictionary<int, int> anchors)
        => area.OnCriticalPath ? area.PathIndex : anchors[area.Id];

    private static string Signature(NpcPlan plan)
        => string.Join("|", plan.ByArea.OrderBy(pair => pair.Key)
            .Select(pair => $"{pair.Key}:{string.Join(';', pair.Value.Select(p => $"{p.Kind}/{p.Text}"))}"));
}
