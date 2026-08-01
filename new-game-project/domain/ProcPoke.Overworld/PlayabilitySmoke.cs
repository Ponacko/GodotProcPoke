using ProcPoke.Generation.Carving;
using ProcPoke.Generation;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Overworld;

public sealed record SmokeFailure(
    string Stage,
    int AreaId,
    GridPosition Position,
    Facing Facing,
    string Message,
    string? Metadata = null)
{
    public override string ToString()
        => $"{Stage}: area {AreaId} at ({Position.X},{Position.Y}) facing {Facing}: {Message}"
            + (Metadata is null ? string.Empty : $" [{Metadata}]");
}

public sealed record PlayabilitySmokeResult(
    ulong Seed,
    int StartAreaId,
    int LeagueAreaId,
    int AreasVisited,
    int SeamlessConnections,
    int WarpConnections,
    bool ExercisedLedge,
    bool ExercisedNpc,
    bool ExercisedItem,
    bool ExercisedGate,
    IReadOnlyList<SmokeFailure> Failures)
{
    public bool Passed => Failures.Count == 0 && AreasVisited > 0;
}

/// <summary>
/// Headless Phase 3 route smoke. It walks the generated critical path with debug capabilities, resolving
/// movement and transitions through the same pure seams used by the Godot controller. It never edits a grid.
/// </summary>
public static class PlayabilitySmoke
{
    private static readonly (MovementIntent Intent, Facing Facing)[] Directions =
    [
        (MovementIntent.MoveNorth, Facing.North),
        (MovementIntent.MoveEast, Facing.East),
        (MovementIntent.MoveSouth, Facing.South),
        (MovementIntent.MoveWest, Facing.West),
    ];

    public static PlayabilitySmokeResult Run(GeneratedRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);

        var failures = new List<SmokeFailure>();
        var registry = InteractionRegistry.Build(region);
        var interactions = registry.Of(region.Graph.StartAreaId)
            .Concat(region.Graph.Areas.Skip(1).SelectMany(area => registry.Of(area.Id)))
            .ToArray();
        var session = new InteractionSession();
        session.EnableDebugUnlock();

        var exercisedNpc = ExerciseInteraction(interactions, InteractionKind.Npc, session, failures)
            || ExerciseInteraction(interactions, InteractionKind.Sign, session, failures);
        var exercisedItem = ExerciseInteraction(interactions, InteractionKind.Item, session, failures,
            InteractionOutcomeKind.ItemCollected);
        var exercisedGate = ExerciseGates(interactions, session, failures);

        var path = region.Graph.CriticalPath;
        var start = path.FirstOrDefault(area => area.Id == region.Graph.StartAreaId);
        var initial = start is null ? null : FindInitialState(region.Carved[start.Id].Grid);
        if (start is null || initial is null)
        {
            failures.Add(Failure("start", region.Graph.StartAreaId, default, Facing.South,
                "critical path has no walkable StartTown tile"));
            return Result(region, 0, 0, 0, false, exercisedNpc, exercisedItem, exercisedGate, failures);
        }

        var areaStarts = new Dictionary<int, PlayerState> { [start.Id] = initial };
        var currentState = initial;
        var seamless = 0;
        var warp = 0;
        var visited = 1;

        for (var index = 0; index < path.Count - 1; index++)
        {
            var current = path[index];
            var next = path[index + 1];
            var opening = region.Openings.EdgesOf(current.Id)
                .FirstOrDefault(candidate => candidate.NeighborAreaId == next.Id);
            if (opening is null)
            {
                failures.Add(Failure("opening", current.Id, currentState.Position, currentState.Facing,
                    $"no opening to critical-path area {next.Id}",
                    $"from={current.Id};to={next.Id}"));
                break;
            }

            var capabilities = session.ForMovement(current.Id, new MovementCapabilities(), registry);
            var search = Search(
                region.Carved[current.Id].Grid,
                currentState,
                capabilities,
                state => state.Position == ToPosition(opening, region.Carved[current.Id].Grid)
                    && state.Facing == TransitionResolver.OutwardFacing(opening.Edge));
            if (search.State is null)
            {
                failures.Add(Failure("movement", current.Id, currentState.Position, currentState.Facing,
                    $"could not reach opening to area {next.Id}",
                    $"edge={opening.Edge};offset={opening.Offset};to={next.Id}"));
                break;
            }

            var transition = TransitionResolver.Resolve(
                region, current.Id, search.State.Position, search.State.Facing);
            if (!transition.Succeeded || transition.ToAreaId != next.Id || transition.Arrival is null)
            {
                failures.Add(Failure("transition", current.Id, search.State.Position, search.State.Facing,
                    transition.Failure ?? $"resolved to area {transition.ToAreaId}, expected {next.Id}",
                    $"connection={current.Id}<->{next.Id};kind={transition.Kind}"));
                break;
            }

            if (transition.Kind == TransitionResultKind.Seamless) seamless++;
            if (transition.Kind == TransitionResultKind.Warp) warp++;
            currentState = new PlayerState(transition.Arrival.Position, transition.Arrival.Facing);
            areaStarts[next.Id] = currentState;
            visited++;
        }

        var ledge = SearchForLedge(region, areaStarts, session, registry);
        if (!ledge && region.Carved.Values.Any(area => area.Grid.Count(LogicalTile.Ledge) > 0))
        {
            failures.Add(Failure("ledge", region.Graph.StartAreaId, initial.Position, initial.Facing,
                "no reachable southbound ledge was exercised"));
        }

        if (visited != path.Count)
        {
            var last = path[Math.Min(visited - 1, path.Count - 1)];
            failures.Add(Failure("route", last.Id, currentState.Position, currentState.Facing,
                $"reached {visited}/{path.Count} critical-path areas; League is not reachable"));
        }

        return Result(region, visited, seamless, warp, ledge, exercisedNpc, exercisedItem, exercisedGate, failures);
    }

    private static PlayabilitySmokeResult Result(
        GeneratedRegion region, int visited, int seamless, int warp, bool ledge, bool npc, bool item,
        bool gate, IReadOnlyList<SmokeFailure> failures)
        => new(region.Settings.Seed, region.Graph.StartAreaId, region.Graph.LeagueAreaId, visited,
            seamless, warp, ledge, npc, item, gate, failures);

    private static bool ExerciseInteraction(
        IReadOnlyList<InteractionRegistration> interactions,
        InteractionKind kind,
        InteractionSession session,
        ICollection<SmokeFailure> failures,
        InteractionOutcomeKind expected = InteractionOutcomeKind.Dialogue)
    {
        var interaction = interactions.FirstOrDefault(candidate => candidate.Kind == kind);
        if (interaction is null)
        {
            failures.Add(Failure("interaction", -1, default, Facing.South,
                $"generated region has no {kind} registration"));
            return false;
        }

        var outcome = session.Interact(interaction);
        if (outcome.Kind != expected)
        {
            failures.Add(Failure("interaction", interaction.AreaId, interaction.Position, Facing.South,
                $"{kind} returned {outcome.Kind}, expected {expected}", $"id={interaction.Id}"));
            return false;
        }
        return true;
    }

    private static bool ExerciseGates(
        IReadOnlyList<InteractionRegistration> interactions,
        InteractionSession session,
        ICollection<SmokeFailure> failures)
    {
        var gates = interactions.Where(interaction => interaction.Kind == InteractionKind.Gate)
            .GroupBy(interaction => interaction.GateId)
            .ToList();
        if (gates.Count == 0)
        {
            failures.Add(Failure("interaction", -1, default, Facing.South,
                "generated region has no gate registration"));
            return false;
        }

        var passed = true;
        foreach (var gate in gates)
        {
            var interaction = gate.First();
            var outcome = session.Interact(interaction);
            if (outcome.Kind == InteractionOutcomeKind.GateUnlocked) continue;
            passed = false;
            failures.Add(Failure("gate", interaction.AreaId, interaction.Position, Facing.South,
                $"debug unlock returned {outcome.Kind}",
                $"gate={interaction.GateId};obstacle={interaction.Obstacle};key={interaction.KeyName}"));
        }
        return passed;
    }

    private static bool SearchForLedge(
        GeneratedRegion region,
        IReadOnlyDictionary<int, PlayerState> areaStarts,
        InteractionSession session,
        InteractionRegistry registry)
    {
        foreach (var (areaId, start) in areaStarts)
        {
            var grid = region.Carved[areaId].Grid;
            var capabilities = session.ForMovement(areaId, new MovementCapabilities(), registry);
            var search = Search(grid, start, capabilities,
                state => grid[state.Position.X, state.Position.Y] == LogicalTile.Ledge);
            if (search.State is not null) return true;
        }
        return false;
    }

    private static SearchResult Search(
        TileGrid grid,
        PlayerState start,
        MovementCapabilities capabilities,
        Func<PlayerState, bool> target)
    {
        var queue = new Queue<PlayerState>();
        var seen = new HashSet<PlayerState> { start };
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            if (target(state)) return new SearchResult(state);

            foreach (var (intent, _) in Directions)
            {
                var result = MovementResolver.Resolve(grid, state, intent, capabilities);
                if (result.Result != MovementResultKind.Moved || result.Target is null) continue;
                if (seen.Add(result.State)) queue.Enqueue(result.State);
            }
        }

        return new SearchResult(null);
    }

    private static PlayerState? FindInitialState(TileGrid grid)
        => Enumerable.Range(0, grid.Height)
            .SelectMany(y => Enumerable.Range(0, grid.Width)
                .Select(x => new GridPosition(x, y)))
            .Where(position => grid[position.X, position.Y].IsWalkable())
            .Select(position => new PlayerState(position))
            .FirstOrDefault();

    private static GridPosition ToPosition(AreaOpening opening, TileGrid grid)
    {
        var tile = opening.TileOn(grid.Width, grid.Height);
        return new GridPosition(tile.X, tile.Y);
    }

    private static SmokeFailure Failure(
        string stage, int areaId, GridPosition position, Facing facing, string message, string? metadata = null)
        => new(stage, areaId, position, facing, message, metadata);

    private sealed record SearchResult(PlayerState? State);
}
