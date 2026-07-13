using ProcPoke.Generation.Rng;

namespace ProcPoke.Generation.Topology;

/// <summary>
/// The first pipeline pass (ADR-0004): grows a <see cref="RegionGraph"/> constructively. The backbone
/// chain is laid first, then quota-driven attachment operations satisfy §4.2's requirements *by
/// construction* — transit dungeons break up plain runs, destination dungeons and dead-end branches hang
/// off the spine, and at least one loop-back closes a cycle. There is no sample-and-validate step.
/// </summary>
public static class TopologyGenerator
{
    private static readonly AreaArchetype[] TransitArchetypes =
        [AreaArchetype.Forest, AreaArchetype.StandardCave, AreaArchetype.MountainPath];

    public static RegionGraph Generate(GenerationSettings settings, RngStreams streams)
    {
        settings.Validate();
        var rng = streams.Stream("topology");
        var builder = new Builder(settings.BadgeCount);

        var spine = BuildBackbone(builder, settings.BadgeCount);
        spine = IntersperseTransitDungeons(builder, rng, spine);
        Reindex(builder, spine);
        ConnectSpine(builder, spine);

        AttachDestinationDungeons(builder, rng, spine, settings.BadgeCount);
        AttachDeadEndBranches(builder, rng, spine, settings.BadgeCount);
        AddLoopBacks(builder, rng, spine, settings.BadgeCount);

        return builder.Build(spine[0], spine[^1]);
    }

    // ---- backbone -----------------------------------------------------------

    private static List<int> BuildBackbone(Builder b, int badgeCount)
    {
        var spine = new List<int> { b.Add(AreaArchetype.StartTown, SizeClass.Small) };
        for (var i = 0; i < badgeCount; i++)
        {
            spine.Add(b.Add(AreaArchetype.Route, SizeClass.Medium));
            spine.Add(b.Add(AreaArchetype.Town, SizeClass.Medium));
        }
        spine.Add(b.Add(AreaArchetype.Route, SizeClass.Medium)); // final approach route
        spine.Add(b.Add(AreaArchetype.VictoryRoad, SizeClass.Large));
        spine.Add(b.Add(AreaArchetype.League, SizeClass.Medium));
        return spine;
    }

    /// <summary>Splice a transit dungeon in wherever a third consecutive plain area would otherwise appear.</summary>
    private static List<int> IntersperseTransitDungeons(Builder b, Pcg32 rng, List<int> spine)
    {
        var result = new List<int>();
        var plainRun = 0;
        foreach (var id in spine)
        {
            if (b.ArchetypeOf(id).IsPlain())
            {
                if (plainRun == 2)
                {
                    result.Add(b.Add(rng.Pick(TransitArchetypes), SizeClass.Medium));
                    plainRun = 0;
                }
                result.Add(id);
                plainRun++;
            }
            else
            {
                result.Add(id);
                plainRun = 0;
            }
        }
        return result;
    }

    private static void Reindex(Builder b, List<int> spine)
    {
        for (var i = 0; i < spine.Count; i++)
            b.MarkOnPath(spine[i], i);
    }

    private static void ConnectSpine(Builder b, List<int> spine)
    {
        for (var i = 0; i < spine.Count - 1; i++)
            b.Connect(spine[i], spine[i + 1]);
    }

    // ---- attachment operations ---------------------------------------------

    /// <summary>Destination dungeons: a guaranteed villain hideout, a late deep cave, plus towers scaling with size.</summary>
    private static void AttachDestinationDungeons(Builder b, Pcg32 rng, List<int> spine, int badgeCount)
    {
        // Anchor candidates: towns along the spine (you branch off from settlements), excluding start/League.
        var towns = spine.Where(id => b.ArchetypeOf(id) == AreaArchetype.Town).ToList();
        if (towns.Count == 0) towns = spine.GetRange(1, spine.Count - 2);

        // Villain hideout, mid-game.
        b.Connect(towns[Mid(towns.Count, rng)], b.Add(AreaArchetype.VillainHideout, SizeClass.Medium));

        // Deep/post-game cave, hung off one of the last towns.
        b.Connect(towns[^1], b.Add(AreaArchetype.DeepCave, SizeClass.Large));

        // Towers and extra hideouts scale with region size.
        var extra = 1 + badgeCount / 3;
        for (var i = 0; i < extra; i++)
        {
            var archetype = rng.Chance(0.5) ? AreaArchetype.Tower : AreaArchetype.VillainHideout;
            b.Connect(rng.Pick(towns), b.Add(archetype, SizeClass.Medium));
        }
    }

    /// <summary>Short off-spine detour routes with rewards at the end.</summary>
    private static void AttachDeadEndBranches(Builder b, Pcg32 rng, List<int> spine, int badgeCount)
    {
        var anchors = spine.Where(id => b.ArchetypeOf(id) is AreaArchetype.Town or AreaArchetype.Route).ToList();
        var count = 1 + badgeCount / 4;
        for (var i = 0; i < count; i++)
        {
            var size = rng.Chance(0.3) ? SizeClass.Small : SizeClass.Medium;
            b.Connect(rng.Pick(anchors), b.Add(AreaArchetype.Route, size));
        }
    }

    /// <summary>
    /// Loop-backs (quota ≥1): give an off-spine dead-end a second exit to an *earlier* spine area, closing
    /// a cycle — the "this cave has a back way that opens later" shape. Falls back to a spine-to-spine
    /// shortcut if no dead-end is free.
    /// </summary>
    private static void AddLoopBacks(Builder b, Pcg32 rng, List<int> spine, int badgeCount)
    {
        var count = Math.Max(1, badgeCount / 4);
        for (var i = 0; i < count; i++)
        {
            var deadEnd = b.OffSpineWithSingleConnection(rng);
            if (deadEnd is int end)
            {
                var anchorIndex = b.AnchorPathIndex(end);
                var earlier = spine.Where(id => b.PathIndexOf(id) < anchorIndex - 1).ToList();
                if (earlier.Count > 0)
                {
                    b.Connect(end, rng.Pick(earlier), isLoopBack: true);
                    continue;
                }
            }

            // Fallback: connect two spine areas at least 3 apart.
            if (spine.Count >= 4)
            {
                var a = rng.NextInt(spine.Count - 3);
                var c = rng.NextIntInclusive(a + 3, spine.Count - 1);
                b.Connect(spine[a], spine[c], isLoopBack: true);
            }
        }
    }

    private static int Mid(int count, Pcg32 rng) => Math.Clamp(count / 2 + rng.NextInt(-1, 2), 0, count - 1);
}
