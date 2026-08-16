namespace ProcPoke.Overworld;

/// <summary>Session-owned item inventory, counts by baked item id. No purchase/use logic yet — just holds
/// what item balls and rewards hand the player.</summary>
public sealed class Bag
{
    private readonly Dictionary<int, int> _items = [];

    public IReadOnlyDictionary<int, int> Items => _items;

    public void Add(int itemId, int count = 1)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        _items[itemId] = _items.GetValueOrDefault(itemId) + count;
    }

    public int Count(int itemId) => _items.GetValueOrDefault(itemId);

    public bool Has(int itemId) => Count(itemId) > 0;

    public bool Remove(int itemId, int count = 1)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        var have = Count(itemId);
        if (have < count) return false;
        var remaining = have - count;
        if (remaining == 0) _items.Remove(itemId);
        else _items[itemId] = remaining;
        return true;
    }
}
