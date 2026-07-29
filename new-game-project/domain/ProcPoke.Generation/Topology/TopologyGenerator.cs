using ProcPoke.Generation.Rng;

namespace ProcPoke.Generation.Topology;

/// <summary>
/// The first pipeline pass (ADR-0004): grows a <see cref="RegionGraph"/> constructively. The backbone
/// chain is laid first, then quota-driven attachment operations satisfy §4.2's requirements *by
/// construction* — transit dungeons break up plain runs, destination dungeons and dead-end branches hang
/// off the spine, and at least one loop-back closes a cycle. There is no sample-and-validate step.
/// <para>
/// The pass is <em>spatial</em>: it decides the archetype sequence, embeds that sequence as a wandering
/// self-avoiding walk on the overview lattice (<see cref="SpineEmbedder"/>), and only then attaches
/// anything — and every attachment goes on a cell bordering its anchor. Because <see cref="Builder.Connect"/>
/// rejects non-adjacent cells, a connection always means a shared border, so the graph and the 2-D layout
/// can never disagree.
/// </para>
/// </summary>
public static class TopologyGenerator
{
    private static readonly AreaArchetype[] TransitArchetypes =
        [AreaArchetype.Forest, AreaArchetype.StandardCave, AreaArchetype.MountainPath];

    /// <summary>How far apart along the spine two areas must be for a shortcut between them to be a
    /// meaningful loop-back rather than a redundant door between neighbours.</summary>
    private const int MinLoopBackSpan = 3;

    private readonly record struct AreaSpec(AreaArchetype Archetype, SizeClass Size);

    public static RegionGraph Generate(GenerationSettings settings, RngStreams streams)
    {
        settings.Validate();
        var rng = streams.Stream("topology");
        var builder = new Builder(settings.BadgeCount);

        var specs = IntersperseTransitDungeons(rng, Backbone(settings.BadgeCount));
        var spine = LaySpine(builder, rng, specs);

        AttachDestinationDungeons(builder, rng, spine, settings.BadgeCount);
        AttachDeadEndBranches(builder, rng, spine, settings.BadgeCount);
        AddLoopBacks(builder, rng, settings.BadgeCount);

        return builder.Build(spine[0], spine[^1]);
    }

    // ---- the spine ----------------------------------------------------------

    /// <summary>The progression sequence: one route/town pair per badge, then the final approach and League.</summary>
    private static List<AreaSpec> Backbone(int badgeCount)
    {
        var specs = new List<AreaSpec> { new(AreaArchetype.StartTown, SizeClass.Small) };
        for (var i = 0; i < badgeCount; i++)
        {
            specs.Add(new AreaSpec(AreaArchetype.Route, SizeClass.Medium));
            specs.Add(new AreaSpec(AreaArchetype.Town, SizeClass.Medium));
        }
        specs.Add(new AreaSpec(AreaArchetype.Route, SizeClass.Medium)); // final approach route
        specs.Add(new AreaSpec(AreaArchetype.VictoryRoad, SizeClass.Large));
        specs.Add(new AreaSpec(AreaArchetype.League, SizeClass.Large));
        return specs;
    }

    /// <summary>Splice a transit dungeon in wherever a third consecutive plain area would otherwise appear.</summary>
    private static List<AreaSpec> IntersperseTransitDungeons(Pcg32 rng, List<AreaSpec> specs)
    {
        var result = new List<AreaSpec>();
        var plainRun = 0;
        foreach (var spec in specs)
        {
            if (spec.Archetype.IsPlain())
            {
                if (plainRun == 2)
                {
                    result.Add(new AreaSpec(rng.Pick(TransitArchetypes), SizeClass.Medium));
                    plainRun = 0;
                }
                result.Add(spec);
                plainRun++;
            }
            else
            {
                result.Add(spec);
                plainRun = 0;
            }
        }
        return result;
    }

    /// <summary>Embeds the sequence on the lattice, then creates and chains the areas along that walk.</summary>
    private static List<int> LaySpine(Builder b, Pcg32 rng, List<AreaSpec> specs)
    {
        var cells = SpineEmbedder.Embed(specs.Count, rng);
        var spine = new List<int>();
        for (var i = 0; i < specs.Count; i++)
        {
            var id = b.Add(specs[i].Archetype, specs[i].Size, cells[i]);
            b.MarkOnPath(id, i);
            spine.Add(id);
        }
        for (var i = 0; i < spine.Count - 1; i++)
            b.Connect(spine[i], spine[i + 1]);
        return spine;
    }

    // ---- attachment operations ---------------------------------------------

    /// <summary>Destination dungeons: a guaranteed villain hideout, a late deep cave, plus towers scaling with size.</summary>
    private static void AttachDestinationDungeons(Builder b, Pcg32 rng, List<int> spine, int badgeCount)
    {
        // Anchor candidates: towns along the spine (you branch off from settlements), excluding start/League.
        var towns = spine.Where(id => b.ArchetypeOf(id) == AreaArchetype.Town).ToList();
        if (towns.Count == 0) towns = spine.GetRange(1, spine.Count - 2);

        // Villain hideout, mid-game.
        Attach(b, rng, [towns[Mid(towns.Count, rng)]], spine, AreaArchetype.VillainHideout, SizeClass.Medium);

        // Deep/post-game cave, hung off one of the last towns.
        Attach(b, rng, [towns[^1]], spine, AreaArchetype.DeepCave, SizeClass.Large);

        // Towers and extra hideouts scale with region size.
        var extra = 1 + badgeCount / 3;
        for (var i = 0; i < extra; i++)
        {
            var archetype = rng.Chance(0.5) ? AreaArchetype.Tower : AreaArchetype.VillainHideout;
            Attach(b, rng, towns, spine, archetype, SizeClass.Medium);
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
            Attach(b, rng, anchors, spine, AreaArchetype.Route, size);
        }
    }

    /// <summary>
    /// Hangs a new area off one of <paramref name="preferred"/>, on a free cell bordering that anchor. Falls
    /// back to <paramref name="fallback"/> when every preferred anchor is boxed in by its neighbours — a
    /// spine area on the walk's edge always has room, so the fallback cannot come up empty. Anchors are
    /// always critical-path areas, which is what lets <c>GatingGenerator.OffSpineAnchors</c> assume every
    /// off-spine area has a spine neighbour.
    /// </summary>
    private static void Attach(
        Builder b, Pcg32 rng, IReadOnlyList<int> preferred, IReadOnlyList<int> fallback,
        AreaArchetype archetype, SizeClass size)
    {
        var anchor = PickAnchorWithRoom(b, rng, preferred) ?? PickAnchorWithRoom(b, rng, fallback);
        if (anchor is not int id) return; // no room anywhere: skip this quota item rather than fail the region

        var cell = rng.Pick(b.FreeCellsAdjacentTo(id).ToList());
        b.Connect(id, b.Add(archetype, size, cell));
    }

    private static int? PickAnchorWithRoom(Builder b, Pcg32 rng, IReadOnlyList<int> candidates)
    {
        var viable = candidates.Where(id => b.FreeCellsAdjacentTo(id).Any()).ToList();
        return viable.Count == 0 ? null : rng.Pick(viable);
    }

    /// <summary>
    /// Loop-backs (quota ≥1): close a cycle across a border two areas already share. Preferred shape is an
    /// off-spine dead-end that turns out to touch an earlier spine area — the "this cave has a back way that
    /// opens later" reading — otherwise a shortcut between two spine areas far enough apart to be worth
    /// walking. Because the walk always switches back at least once, a qualifying spine pair always exists.
    /// </summary>
    private static void AddLoopBacks(Builder b, Pcg32 rng, int badgeCount)
    {
        var quota = Math.Max(1, badgeCount / 4);
        for (var i = 0; i < quota; i++)
        {
            var candidates = LoopBackCandidates(b).ToList();
            if (candidates.Count == 0) return;

            var backDoors = candidates.Where(p => b.PathIndexOf(p.A) < 0 || b.PathIndexOf(p.B) < 0).ToList();
            var (a, c) = rng.Pick(backDoors.Count > 0 ? backDoors : candidates);
            b.Connect(a, c, isLoopBack: true);
        }
    }

    /// <summary>
    /// Every unconnected pair of bordering areas that would make a meaningful cycle: a dead-end reaching an
    /// earlier spine area, or two spine areas at least <see cref="MinLoopBackSpan"/> apart.
    /// </summary>
    private static IEnumerable<(int A, int B)> LoopBackCandidates(Builder b)
    {
        foreach (var id in b.AllIds)
        {
            foreach (var other in b.AreasAdjacentTo(b.CellOf(id)))
            {
                if (other <= id || b.AreConnected(id, other)) continue;

                var (pa, pb) = (b.PathIndexOf(id), b.PathIndexOf(other));
                if (pa >= 0 && pb >= 0)
                {
                    if (Math.Abs(pa - pb) >= MinLoopBackSpan) yield return (id, other);
                    continue;
                }

                // Off-spine end: only a dead-end gains anything from a second exit, and it should lead to a
                // spine area earlier than the one it already hangs off.
                var (offSpine, spineEnd) = pa < 0 ? (id, other) : (other, id);
                if (b.PathIndexOf(spineEnd) < 0 || b.DegreeOf(offSpine) != 1) continue;
                if (b.PathIndexOf(spineEnd) < b.AnchorPathIndex(offSpine)) yield return (id, other);
            }
        }
    }

    private static int Mid(int count, Pcg32 rng) => Math.Clamp(count / 2 + rng.NextInt(-1, 2), 0, count - 1);
}
