namespace ProcPoke.Generation;

/// <summary>
/// Stamped into every Seed String. Determinism is promised per generator
/// version only (GDD §11) — bump on any generation-affecting change.
/// </summary>
public static class GeneratorVersion
{
    public const int Current = 1;
}
