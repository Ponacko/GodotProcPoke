namespace ProcPoke.Generation.Topology;

/// <summary>A compass step on the overview lattice. Row grows downward (screen convention).</summary>
public enum Heading { East, West, South, North }

/// <summary>
/// An area's cell on the region's overview lattice — the region's 2-D shape, decided by the topology pass
/// rather than derived afterwards (ADR-0004). One cell per area; two areas never share one.
/// <para>
/// This type is what makes the region embeddable: connections may only join cells that are orthogonally
/// adjacent (<see cref="IsAdjacentTo"/>), so an edge in the graph always corresponds to a border two areas
/// physically share. Without that constraint the graph can express connections no 2-D layout can honour —
/// a tower touching both the 4th and the 9th area of the spine.
/// </para>
/// </summary>
public readonly record struct GridCell(int Col, int Row)
{
    public static readonly Heading[] AllHeadings = [Heading.East, Heading.West, Heading.South, Heading.North];

    public GridCell Step(Heading heading) => heading switch
    {
        Heading.East => new GridCell(Col + 1, Row),
        Heading.West => new GridCell(Col - 1, Row),
        Heading.South => new GridCell(Col, Row + 1),
        _ => new GridCell(Col, Row - 1),
    };

    /// <summary>The four orthogonally adjacent cells.</summary>
    public IEnumerable<GridCell> Neighbors()
    {
        foreach (var h in AllHeadings) yield return Step(h);
    }

    /// <summary>True if the two cells share a border — the precondition for connecting their areas.</summary>
    public bool IsAdjacentTo(GridCell other) => ManhattanTo(other) == 1;

    public int ManhattanTo(GridCell other) => Math.Abs(Col - other.Col) + Math.Abs(Row - other.Row);

    /// <summary>The heading that steps from this cell to an adjacent <paramref name="other"/>.</summary>
    public Heading HeadingTo(GridCell other)
    {
        foreach (var h in AllHeadings)
            if (Step(h) == other) return h;
        throw new ArgumentException($"{other} is not orthogonally adjacent to {this}", nameof(other));
    }

    public override string ToString() => $"({Col},{Row})";
}

public static class HeadingExtensions
{
    /// <summary>The two headings perpendicular to this one.</summary>
    public static Heading[] Turns(this Heading h) => h switch
    {
        Heading.East or Heading.West => [Heading.North, Heading.South],
        _ => [Heading.East, Heading.West],
    };

    public static Heading Opposite(this Heading h) => h switch
    {
        Heading.East => Heading.West,
        Heading.West => Heading.East,
        Heading.South => Heading.North,
        _ => Heading.South,
    };
}
