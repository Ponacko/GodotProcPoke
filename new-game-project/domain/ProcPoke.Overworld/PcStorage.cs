using ProcPoke.Battle;

namespace ProcPoke.Overworld;

/// <summary>
/// The PC boxes: where a Pokémon goes when the party is full, and where the player parks the rest of a
/// collection. Boxes are fixed-size and ordered, and a deposit always lands in the first box with room —
/// the mainline "Box 1 is full, using Box 2" rule — so nothing is ever silently dropped.
/// </summary>
public sealed class PcStorage
{
    public const int BoxCount = 8;
    public const int BoxCapacity = 30;
    public const int Capacity = BoxCount * BoxCapacity;

    private readonly List<BattleProgressionState> _stored = [];

    /// <summary>Everything in storage, in deposit order — box <c>i / BoxCapacity</c>, slot
    /// <c>i % BoxCapacity</c>.</summary>
    public IReadOnlyList<BattleProgressionState> Stored => _stored;

    public int Count => _stored.Count;
    public bool IsFull => _stored.Count >= Capacity;

    /// <summary>Which box a deposit would land in, or null when storage is full.</summary>
    public int? NextBox => IsFull ? null : _stored.Count / BoxCapacity;

    public void Deposit(BattleProgressionState member)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (IsFull) throw new InvalidOperationException("Every PC box is full.");
        _stored.Add(member);
    }

    /// <summary>Takes one Pokémon out of storage. Later deposits shift down a slot, the same way a mainline
    /// box closes the gap.</summary>
    public BattleProgressionState Withdraw(int index)
    {
        if (index < 0 || index >= _stored.Count)
            throw new ArgumentOutOfRangeException(nameof(index), index, "no such stored Pokémon");
        var member = _stored[index];
        _stored.RemoveAt(index);
        return member;
    }

    public IReadOnlyList<BattleProgressionState> BoxAt(int box)
    {
        if (box < 0 || box >= BoxCount)
            throw new ArgumentOutOfRangeException(nameof(box), box, "no such box");
        return _stored.Skip(box * BoxCapacity).Take(BoxCapacity).ToArray();
    }
}

/// <summary>Where a newly gained Pokémon ended up.</summary>
public enum PokemonGainKind
{
    JoinedParty,
    SentToBox,

    /// <summary>Party and every box full — the one case a Pokémon cannot be accepted.</summary>
    NoRoom,
}

public sealed record PokemonGainResult(PokemonGainKind Kind, string Message, int? BoxIndex = null)
{
    public bool Accepted => Kind is PokemonGainKind.JoinedParty or PokemonGainKind.SentToBox;
}
