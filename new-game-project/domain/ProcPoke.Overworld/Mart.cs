using ProcPoke.Data;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Overworld;

/// <summary>
/// A town's Mart stock, scaled by how far into the region it sits — the same availability-fraction idea
/// TrainerPass/EncounterPass use for item/level scaling, applied to a shop's catalog instead. Zero-cost
/// items (Master Ball, key-item templates) are never for sale.
/// </summary>
public static class MartCatalog
{
    private const string StandardBalls = "standard-balls";
    private const string Healing = "healing";
    private const string StatusCures = "status-cures";
    private const string Revival = "revival";

    private static readonly string[] EarlyCategories = [StandardBalls, Healing];
    private static readonly string[] LateCategories = [StandardBalls, Healing, StatusCures, Revival];

    /// <summary>Progression fraction past which every Mart stocks Link Cables (GDD §8.4's "late-game
    /// Marts"); before it, only the services pass's one guaranteed vendor does.</summary>
    public const double LinkCableFraction = 0.75;

    public static IReadOnlyList<int> For(GameData data, double availabilityFraction, bool stocksLinkCable = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        var priceCeiling = 200 + availabilityFraction * 2800;
        var categories = availabilityFraction >= 0.5 ? LateCategories : EarlyCategories;

        var stock = data.Items.Values
            .Where(item => item.Cost > 0 && item.Cost <= priceCeiling && categories.Contains(item.Category))
            .OrderBy(item => item.Category, StringComparer.Ordinal)
            .ThenBy(item => item.Cost)
            .Select(item => item.Id)
            .ToList();

        // The Link Cable is stocked by name, not by the price/category filter above: at 8000 it is far over
        // any ceiling, and its category ("evolution") would drag in every stone with it.
        if (stocksLinkCable || availabilityFraction >= LinkCableFraction)
            stock.Add(LinkCable.IdIn(data));
        return stock;
    }
}

/// <summary>
/// The Link Cable (CONTEXT.md): ProcPoke's stand-in for trading, and the only bag item any evolution rule
/// needs that the baked data does not name — trade rows carry no trigger item, so the item is resolved here
/// by its canonical name against the pinned bake.
/// </summary>
public static class LinkCable
{
    public const string ItemName = "Linking Cord";

    public static int IdIn(GameData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.Items.Values.FirstOrDefault(item => item.Name == ItemName)?.Id
            ?? throw new InvalidOperationException($"baked items contain no \"{ItemName}\"");
    }
}

/// <summary>Where a town sits along the region's first-availability order (0 = StartTown, 1 = just before
/// the League), reused from the topology helpers so Overworld never recomputes its own ordering.</summary>
public static class MartAvailability
{
    public static double FractionOf(RegionGraph graph, int areaId)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var order = AvailabilityOrder.Of(graph, GatingGenerator.OffSpineAnchors(graph));
        var index = order.ToList().IndexOf(areaId);
        return order.Count <= 1 ? 1 : index / (order.Count - 1.0);
    }
}

public enum MartPurchaseKind
{
    Bought,
    NothingAffordable,
    StockEmpty,
}

public sealed record MartPurchaseResult(MartPurchaseKind Kind, string Message, int? ItemId = null, int Cost = 0);

/// <summary>
/// Binds a town's Mart clerk to the fixed interior template. There is no shop-menu UI yet (a Godot list
/// screen was deliberately out of scope for this pass — it can't be visually verified without one), so
/// talking to the clerk is a minimal vending interaction: it buys the single most expensive item currently
/// affordable from stock, rather than presenting a choice.
/// </summary>
public static class MartInteractions
{
    public static IReadOnlyList<InteractionRegistration> Build(int areaId, InteriorTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (template.Kind != InteriorKind.Mart)
            throw new ArgumentException("template is not a mart", nameof(template));

        var positions = TileGridQuery.PositionsOf(template.Grid, LogicalTile.NpcPost);
        if (positions.Count != 1)
            throw new InvalidOperationException($"mart template has {positions.Count} clerk posts, expected 1");

        return
        [
            new InteractionRegistration
            {
                Id = $"mart:{areaId}",
                AreaId = areaId,
                Position = positions[0],
                Kind = InteractionKind.Shop,
                Text = "Welcome! Take a look at what we have in stock.",
            },
        ];
    }
}
