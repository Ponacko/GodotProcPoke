using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Topology;
using ProcPoke.Overworld;
using Xunit;

namespace ProcPoke.Generation.Tests;

public sealed class TransitionTests
{
    [Theory]
    [InlineData(2UL)]
    [InlineData(42UL)]
    [InlineData(777UL)]
    public void GeneratedConnectionsReplayToWalkablePairedArrivals(ulong seed)
    {
        var region = RegionGenerator.Generate(
            new GenerationSettings { Seed = seed, BadgeCount = 8 }, TestData.Data);
        var seenSeamless = false;
        var seenWarp = false;

        foreach (var connection in region.Graph.Connections)
        {
            var source = region.Carved[connection.AreaA];
            var opening = region.Openings.EdgesOf(connection.AreaA)
                .Single(candidate => candidate.NeighborAreaId == connection.AreaB);
            var position = opening.TileOn(source.Grid.Width, source.Grid.Height);
            if (source.Grid[position.X, position.Y] != LogicalTile.Warp) continue;

            var result = TransitionResolver.Resolve(
                region,
                connection.AreaA,
                new GridPosition(position.X, position.Y),
                TransitionResolver.OutwardFacing(opening.Edge));

            Assert.True(result.Succeeded, result.Failure);
            Assert.Equal(connection.AreaB, result.ToAreaId);
            Assert.NotNull(result.Arrival);
            Assert.Equal(TransitionResolver.OutwardFacing(opening.Edge), result.Arrival!.Facing);
            Assert.Equal(region.World.OriginOf(connection.AreaB), result.Arrival.WorldOrigin);

            var destination = region.Carved[connection.AreaB].Grid;
            var arrivalTile = destination[result.Arrival.Position.X, result.Arrival.Position.Y];
            Assert.True(arrivalTile.IsWalkable(),
                $"seed {seed}: arrival {result.Arrival.Position} is {arrivalTile}");
            Assert.NotEqual(LogicalTile.GateObstacle, arrivalTile);

            if (connection.Kind == ConnectionKind.Seamless)
            {
                seenSeamless = true;
                Assert.Equal(TransitionResultKind.Seamless, result.Kind);
            }
            else
            {
                seenWarp = true;
                Assert.Equal(TransitionResultKind.Warp, result.Kind);
            }
        }

        Assert.True(seenSeamless, $"seed {seed} had no replayable Seamless connection");
        Assert.True(seenWarp, $"seed {seed} had no replayable Warp connection");
    }

    [Fact]
    public void TransitionRequiresTheOpeningAndOutwardFacing()
    {
        var region = RegionGenerator.Generate(
            new GenerationSettings { Seed = 42, BadgeCount = 8 }, TestData.Data);
        var connection = region.Graph.Connections.First(c => c.Kind == ConnectionKind.Seamless);
        var source = region.Carved[connection.AreaA];
        var opening = region.Openings.EdgesOf(connection.AreaA)
            .Single(candidate => candidate.NeighborAreaId == connection.AreaB);
        var position = opening.TileOn(source.Grid.Width, source.Grid.Height);
        var outward = TransitionResolver.OutwardFacing(opening.Edge);

        var wrongFacing = outward == Facing.North ? Facing.South : Facing.North;
        var wrong = TransitionResolver.Resolve(
            region, connection.AreaA, new GridPosition(position.X, position.Y), wrongFacing);
        var interior = TransitionResolver.Resolve(
            region, connection.AreaA, new GridPosition(
                Math.Clamp(position.X, 1, source.Grid.Width - 2),
                Math.Clamp(position.Y, 1, source.Grid.Height - 2)), outward);

        Assert.Equal(TransitionResultKind.None, wrong.Kind);
        Assert.Equal(TransitionResultKind.None, interior.Kind);
    }
}
