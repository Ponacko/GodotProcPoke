using ProcPoke.Battle;

namespace ProcPoke.Overworld;

/// <summary>
/// The player's owned Pokémon, in party order. <see cref="BattleProgressionState.CurrentHp"/> persists
/// between battles, but there is still no "active"/"fainted" member selection here — with only ever one
/// member possible until Bag/PC/multi-catch systems exist, callers simply use <see cref="Members"/>[0].
/// Real active-member selection is a Trainer-battles/Gyms
/// concern, once the party can actually hold more than one Pokémon.
/// </summary>
public sealed class Party
{
    public const int MaxSize = 6;

    private readonly List<BattleProgressionState> _members = [];

    public IReadOnlyList<BattleProgressionState> Members => _members;

    /// <summary>Whether <paramref name="additional"/> more members would still fit without overflowing
    /// <see cref="MaxSize"/> — the same room-check <see cref="Add"/> itself enforces, kept in one place.</summary>
    public bool HasRoomFor(int additional) => _members.Count + additional < MaxSize;

    /// <summary>Whether one more member would fit at all — <c>HasRoomFor(0)</c> read the way callers
    /// that only ever add one Pokémon at a time want to ask it.</summary>
    public bool IsFull => !HasRoomFor(0);

    public void Add(BattleProgressionState member)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (_members.Count >= MaxSize)
            throw new InvalidOperationException($"A party cannot hold more than {MaxSize} Pokémon.");
        _members.Add(member);
    }

    public void Replace(int index, BattleProgressionState updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        if (index < 0 || index >= _members.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        _members[index] = updated;
    }
}
