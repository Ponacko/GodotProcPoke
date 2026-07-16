using ProcPoke.Data;

namespace ProcPoke.Generation.Roster;

/// <summary>The three closed effectiveness-triangle templates a seed may draw (GDD §5.3).</summary>
public enum StarterTriangle
{
    Classic,
    MindAndBody,
    Elemental,
}

/// <summary>One triangle corner: its type, and the 3-stage evolution line (base, mid, final species id)
/// chosen to fill it.</summary>
public sealed record StarterCorner
{
    public required PokeType Type { get; init; }
    public required IReadOnlyList<int> LineSpeciesIds { get; init; }

    public int BaseSpeciesId => LineSpeciesIds[0];
    public int MidSpeciesId => LineSpeciesIds[1];
    public int FinalSpeciesId => LineSpeciesIds[2];
}

/// <summary>The chosen starter triangle and its three corners, in the template's cycle order.</summary>
public sealed record StarterPlan
{
    public required StarterTriangle Triangle { get; init; }
    public required IReadOnlyList<StarterCorner> Corners { get; init; }
}
