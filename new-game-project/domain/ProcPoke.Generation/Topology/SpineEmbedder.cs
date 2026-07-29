using ProcPoke.Generation.Rng;

namespace ProcPoke.Generation.Topology;

/// <summary>
/// Lays the spine sequence out as a self-avoiding walk on the overview lattice, so the critical path
/// <em>wanders</em> the way a real region's does instead of running dead straight in one direction.
/// <para>
/// The walk keeps runs short (<see cref="MinRun"/>..<see cref="MaxRun"/> cells before it may turn) and is
/// bounded to a roughly square box, which together produce the switchbacks a hand-drawn region has: travel
/// a few areas, turn, travel a few more. The box is deliberately about twice the area the spine needs, so
/// most spine cells keep a free neighbour for a branch to hang off.
/// </para>
/// <para>
/// Two guarantees the rest of the pass leans on:
/// <list type="bullet">
/// <item>Consecutive spine areas land on adjacent cells, so the spine is embeddable by construction.</item>
/// <item>The walk contains at least one U-turn — headings <c>h, perpendicular, h.Opposite()</c> — which
/// puts two spine areas three path indices apart on adjacent cells. That is the pair
/// <see cref="TopologyGenerator"/> needs for its guaranteed loop-back; without it a region could have no
/// legal place to close a cycle at all.</item>
/// </list>
/// </para>
/// </summary>
internal static class SpineEmbedder
{
    private const int MinRun = 1;
    private const int MaxRun = 5;

    /// <summary>Backtracking budget before falling back to the serpentine walk. Generous: the lookahead
    /// means a typical walk never backtracks, so hitting this is a pathological box/length combination.</summary>
    private const int NodeBudget = 20_000;

    /// <summary>
    /// Cells for a spine of <paramref name="count"/> areas, in path order. Never fails: exhausting the
    /// search budget falls back to a serpentine walk, which satisfies both guarantees by shape.
    /// </summary>
    public static List<GridCell> Embed(int count, Pcg32 rng)
    {
        if (count <= 0) return [];

        var side = Math.Max(3, (int)Math.Ceiling(Math.Sqrt(count * 2.0)) + 1);
        var start = StartCorner(side, rng);

        var path = new List<GridCell> { start };
        var occupied = new HashSet<GridCell> { start };
        var headings = new List<Heading>();
        var budget = NodeBudget;

        return Walk(path, occupied, headings, count, side, rng, ref budget)
            ? path
            : Serpentine(count, side);
    }

    /// <summary>A random corner, so regions don't all read as starting from the same place.</summary>
    private static GridCell StartCorner(int side, Pcg32 rng)
        => new(rng.Chance(0.5) ? 0 : side - 1, rng.Chance(0.5) ? 0 : side - 1);

    /// <summary>
    /// Depth-first placement of the remaining spine areas. <paramref name="headings"/> holds the heading of
    /// each step taken so far, so the run length and the U-turn check are both local reads.
    /// </summary>
    private static bool Walk(
        List<GridCell> path,
        HashSet<GridCell> occupied,
        List<Heading> headings,
        int count,
        int side,
        Pcg32 rng,
        ref int budget)
    {
        // A walk too short to switch back cannot be asked for a U-turn; no such spine is ever generated
        // (the shortest is start/route/town/route/victory-road/league), but the guard keeps Embed total.
        if (path.Count == count)
        {
            if (count >= 4 && !HasUTurn(headings)) return false;
            return EntersLeagueFromTheWest(headings);
        }
        if (--budget <= 0) return false;

        var current = path[^1];
        var remaining = count - path.Count;
        foreach (var heading in Candidates(headings, remaining, rng))
        {
            var next = current.Step(heading);
            if (!InBox(next, side) || occupied.Contains(next)) continue;

            // Reject a cell whose free region can no longer hold what is left to place — this is what keeps
            // the search from walking into a pocket and backtracking its way out one cell at a time.
            occupied.Add(next);
            if (FreeReachableFrom(next, occupied, side, remaining - 1) < remaining - 1)
            {
                occupied.Remove(next);
                continue;
            }

            path.Add(next);
            headings.Add(heading);
            if (Walk(path, occupied, headings, count, side, rng, ref budget)) return true;
            headings.RemoveAt(headings.Count - 1);
            path.RemoveAt(path.Count - 1);
            occupied.Remove(next);
        }

        return false;
    }

    /// <summary>
    /// Headings to try, best first. Straight is favoured while the current run is short and forbidden once
    /// it hits <see cref="MaxRun"/>; turns come shuffled. When the walk is running out of steps without
    /// having made a U-turn yet, the turn that completes one is tried first — cheaper than discovering the
    /// shortfall at the end and backtracking the whole walk.
    /// </summary>
    private static List<Heading> Candidates(List<Heading> headings, int remaining, Pcg32 rng)
    {
        if (headings.Count == 0)
        {
            var all = GridCell.AllHeadings.ToList();
            rng.Shuffle(all);
            return all;
        }

        var last = headings[^1];
        var turns = last.Turns().ToList();
        rng.Shuffle(turns);

        var run = CurrentRun(headings);
        var ordered = new List<Heading>();

        // The heading that would close a U-turn: we are one step past a turn, so reversing the heading
        // before it brings the walk alongside the cell three back.
        if (!HasUTurn(headings) && remaining <= 3 && headings.Count >= 2 && run == 1)
        {
            var closing = headings[^2].Opposite();
            if (turns.Remove(closing)) ordered.Add(closing);
        }

        var straightAllowed = run < MaxRun;
        var preferStraight = run < MinRun || rng.Chance(0.62);
        if (straightAllowed && preferStraight) ordered.Add(last);
        ordered.AddRange(turns);
        if (straightAllowed && !preferStraight) ordered.Add(last);

        return ordered;
    }

    /// <summary>
    /// Whether the walk's final step runs east, putting the League on the eastern side of the border it
    /// shares with Victory Road. <c>LeagueCarver</c> authors the Elite Four gauntlet left-to-right with the
    /// throne at the far east, so the challenger has to arrive on the western edge or the path to the throne
    /// skips the four doorways entirely. Constraining the last step is much cheaper than making the gauntlet
    /// orientable — and a straight final approach to the League reads right anyway.
    /// </summary>
    private static bool EntersLeagueFromTheWest(List<Heading> headings)
        => headings.Count == 0 || headings[^1] == Heading.East;

    /// <summary>How many steps the walk has taken on its current heading.</summary>
    private static int CurrentRun(List<Heading> headings)
    {
        var run = 1;
        for (var i = headings.Count - 2; i >= 0 && headings[i] == headings[^1]; i--) run++;
        return run;
    }

    /// <summary>
    /// Whether some three consecutive steps read <c>h, perpendicular, h.Opposite()</c> — the switchback that
    /// leaves the cell before it adjacent to the cell after it.
    /// </summary>
    private static bool HasUTurn(List<Heading> headings)
    {
        for (var i = 0; i + 2 < headings.Count; i++)
            if (headings[i + 2] == headings[i].Opposite() && headings[i + 1] != headings[i]
                && headings[i + 1] != headings[i].Opposite())
                return true;
        return false;
    }

    /// <summary>Free cells reachable from <paramref name="from"/>, counted up to <paramref name="cap"/>.</summary>
    private static int FreeReachableFrom(GridCell from, HashSet<GridCell> occupied, int side, int cap)
    {
        var seen = new HashSet<GridCell> { from };
        var queue = new Queue<GridCell>([from]);
        var reached = 0;
        while (queue.Count > 0 && reached < cap)
        {
            foreach (var n in queue.Dequeue().Neighbors())
            {
                if (!InBox(n, side) || occupied.Contains(n) || !seen.Add(n)) continue;
                reached++;
                queue.Enqueue(n);
            }
        }
        return reached;
    }

    private static bool InBox(GridCell c, int side)
        => c.Col >= 0 && c.Col < side && c.Row >= 0 && c.Row < side;

    /// <summary>
    /// Boustrophedon fallback: fill a row, drop one, fill it back the other way. Always self-avoiding, and
    /// every row transition (<c>west, south, east</c>) is a U-turn, so those guarantees hold by shape.
    /// <para>
    /// Built with its first band running west and then reversed, which is what makes the final step east and
    /// so satisfies <see cref="EntersLeagueFromTheWest"/> too: the reversed walk's last step is the opposite
    /// of the original's first. Reversing a serpentine leaves it a serpentine, so nothing else is lost.
    /// </para>
    /// </summary>
    private static List<GridCell> Serpentine(int count, int side)
    {
        var cells = new List<GridCell>();
        for (var row = 0; cells.Count < count; row++)
        {
            for (var i = 0; i < side && cells.Count < count; i++)
            {
                // Band 0 runs west (col side-1 → 0), band 1 east, and so on.
                var col = row % 2 == 0 ? side - 1 - i : i;
                cells.Add(new GridCell(col, row));
            }
        }
        cells.Reverse();
        return cells;
    }
}
