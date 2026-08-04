using ProcPoke.Battle;

namespace ProcPoke.Overworld;

/// <summary>
/// The player's owned Pokémon, in party order. <see cref="BattleProgressionState"/> carries no HP field
/// (HP is battle-session-local, not persisted between battles), so there is no "active"/"fainted" member
/// selection here yet — with only ever one member possible until Bag/PC/multi-catch systems exist,
/// callers simply use <see cref="Members"/>[0]. Real active-member selection is a Trainer-battles/Gyms
/// concern, once the party can actually hold more than one Pokémon.
/// </summary>
public sealed class Party
{
    public const int MaxSize = 6;

    private readonly List<BattleProgressionState> _members = [];

    public IReadOnlyList<BattleProgressionState> Members => _members;

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
