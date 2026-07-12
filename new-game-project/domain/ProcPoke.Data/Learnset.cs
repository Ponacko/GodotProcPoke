namespace ProcPoke.Data;

/// <summary>How a species acquires a move.</summary>
public enum LearnMethod
{
    LevelUp,
    Egg,
    Tutor,
    Machine,
}

/// <summary>
/// One way a species learns one move in B2W2. <see cref="Level"/> is meaningful only for
/// <see cref="LearnMethod.LevelUp"/> (0 otherwise). Egg moves are baked but inert until breeding ships.
/// </summary>
public readonly record struct LearnsetEntry(int MoveId, LearnMethod Method, int Level);
