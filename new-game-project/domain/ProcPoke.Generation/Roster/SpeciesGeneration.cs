namespace ProcPoke.Generation.Roster;

/// <summary>Maps a national dex id to its generation (the pinned dataset is Gen 1–5, ids 1–649
/// contiguous, DataPin.MaxSpeciesId), for enforcing <see cref="GenerationSettings.RosterCap"/>.</summary>
public static class SpeciesGeneration
{
    public static int Of(int speciesId) => speciesId switch
    {
        <= 151 => 1,
        <= 251 => 2,
        <= 386 => 3,
        <= 493 => 4,
        _ => 5,
    };
}
