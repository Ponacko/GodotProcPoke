using ProcPoke.Battle;
using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Debug;
using ProcPoke.Generation.Rivals;
using ProcPoke.Overworld;
using ProcPoke.Overworld.Save;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// The save system. A save's whole job is to come back identical, so the assertions are round-trip
/// comparisons: the reloaded region must render byte-identical debug text (which covers topology, gating,
/// biomes, names, identity, starters, dex, specials, encounters, trainers, bosses, rivals, NPCs and
/// services), carve tile-for-tile, and pass the same invariant checker; the reloaded session must report
/// the same progress.
/// </summary>
public sealed class SaveTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "procpoke-save-tests", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private SaveStore Store() => new(_directory);

    private static GeneratedRegion Region(ulong seed = 42, int badges = 8)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);

    private static string Render(GeneratedRegion region)
        => RegionGraphText.Render(
            region.Graph, region.Gating, region.Biomes, region.Names, region.Identity, region.Starters,
            region.Dex, TestData.Data, region.Special, region.Encounters, region.Trainers, region.Bosses,
            region.Rivals, region.Npcs, region.Services);

    private static void AssertSameRegion(GeneratedRegion expected, GeneratedRegion actual)
    {
        Assert.Equal(Render(expected), Render(actual));
        Assert.Equal(expected.Settings, actual.Settings);
        Assert.Equal(expected.Carved.Keys.Order(), actual.Carved.Keys.Order());

        foreach (var (areaId, carved) in expected.Carved)
        {
            var reloaded = actual.Carved[areaId];
            Assert.Equal(carved.Grid.Width, reloaded.Grid.Width);
            Assert.Equal(carved.Grid.Height, reloaded.Grid.Height);
            for (var y = 0; y < carved.Grid.Height; y++)
            for (var x = 0; x < carved.Grid.Width; x++)
                Assert.Equal(carved.Grid[x, y], reloaded.Grid[x, y]);

            Assert.Equal(carved.Openings, reloaded.Openings);
            Assert.Equal(carved.GateTiles, reloaded.GateTiles);
            Assert.Equal(
                carved.BuildingWallTiles.OrderBy(p => p.Y).ThenBy(p => p.X),
                reloaded.BuildingWallTiles.OrderBy(p => p.Y).ThenBy(p => p.X));
            Assert.Equal(carved.BuildingEntrances, reloaded.BuildingEntrances);
            Assert.Equal(carved.Rooms, reloaded.Rooms);
            Assert.Equal(carved.TrunkRow, reloaded.TrunkRow);
            Assert.Equal(carved.HideoutBossPost, reloaded.HideoutBossPost);
        }

        // The two derived members a save deliberately does not store must come back the same anyway.
        Assert.Equal(expected.World.Grid.Width, actual.World.Grid.Width);
        Assert.Equal(expected.World.Grid.Height, actual.World.Grid.Height);
        Assert.Equal(expected.World.GateTiles.Count, actual.World.GateTiles.Count);
        Assert.Equal(
            expected.Graph.Areas.Select(area => expected.World.OriginOf(area.Id)),
            actual.Graph.Areas.Select(area => actual.World.OriginOf(area.Id)));
        Assert.All(
            expected.Graph.Areas,
            area => Assert.Equal(
                expected.Openings.EdgesOf(area.Id), actual.Openings.EdgesOf(area.Id)));

        Assert.Equal(
            Invariants.CheckAll(expected, TestData.Data), Invariants.CheckAll(actual, TestData.Data));

        // Building the interaction registry re-validates every trainer post, item ball and NPC post against
        // the reloaded grids — it throws if a coordinate no longer lands on the tile it belongs to, which is
        // exactly what a mis-serialized position would look like.
        var expectedInteractions = InteractionRegistry.Build(expected);
        var actualInteractions = InteractionRegistry.Build(actual);
        Assert.All(expected.Graph.Areas, area => Assert.Equal(
            expectedInteractions.Of(area.Id).Select(interaction => (interaction.Id, interaction.Position)),
            actualInteractions.Of(area.Id).Select(interaction => (interaction.Id, interaction.Position))));
    }

    /// <summary>Compares Pokémon field by field. Record equality is not enough: a move list read back from
    /// JSON is a List where the original was an array, and records compare those by reference.</summary>
    private static void AssertSameMembers(
        IReadOnlyList<BattleProgressionState> expected, IReadOnlyList<BattleProgressionState> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i] with { MoveIds = [] }, actual[i] with { MoveIds = [] });
            Assert.Equal(expected[i].MoveIds, actual[i].MoveIds);
        }
    }

    /// <summary>A session with something of everything in it, so a round trip has something to lose.</summary>
    private static (OverworldSession Session, PlayerState Player) PlayedSession(GeneratedRegion region)
    {
        var session = new OverworldSession(
            region, NewGameIdentity.Create(CharacterChoice.CharacterB, "Ren"));
        session.SetStarterCorner(1);
        session.GainPokemon(TestData.Data, BattleProgressionFactory.AtLevel(TestData.Data, 1, 12));
        session.GainPokemon(
            TestData.Data,
            BattleProgressionFactory.AtLevel(TestData.Data, 4, 9) with { Nickname = "Pip" });
        for (var i = 0; i < Party.MaxSize + 1; i++)
            session.GainPokemon(TestData.Data, BattleProgressionFactory.AtLevel(TestData.Data, 16, 5));

        session.SetBadges(3);
        session.SetTime(19 * 60 + 30);
        session.MarkSpeciesSeen(19);
        session.MarkLegendaryDefeated(region.Special.Legendaries[0].SpeciesId);
        session.MarkLegendaryCaptured(region.Special.Legendaries[1].SpeciesId);
        session.MarkRivalBeatDefeated(RivalBeat.PostStarter);
        session.CollectFossils(region.Dex.FossilAreaId);
        session.GrantFly();
        session.Interactions.GrantKey("Surf");
        session.Interactions.Bag.Add(17, 4);
        session.CompleteTrainerBattle(new GeneratedTrainerBattleTransitionRequest(
            "trainer:1:2:3", "Youngster", new PlayerState(new GridPosition(0, 0)), [], 480));

        // Somewhere other than the start town, and standing inside a building.
        var town = region.Graph.Areas.First(area =>
            area.Id != region.Graph.StartAreaId
            && region.Carved[area.Id].BuildingEntrances.Count > 0);
        session.VisitArea(town.Id);
        var entrance = region.Carved[town.Id].BuildingEntrances[0];
        session.EnterInterior(new InteriorTransition(
            InteriorTransitionKind.Enter,
            InteriorTemplateCatalog.For(entrance.Kind).Kind,
            town.Id,
            new GridPosition(entrance.X, entrance.Y),
            InteriorTemplateCatalog.For(entrance.Kind).Arrival,
            Facing.North,
            InteriorTemplateCatalog.For(entrance.Kind)));

        return (session, new PlayerState(new GridPosition(3, 4), Facing.West, MovementMode.Bicycle));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void ARegionSurvivesASaveAndLoadUnchanged(int badges)
    {
        var store = Store();
        var checkedCount = 0;

        for (ulong seed = 1; seed <= 6; seed++)
        {
            var region = Region(seed, badges);
            var session = new OverworldSession(region, NewGameIdentity.Create(CharacterChoice.CharacterA, "P"));

            store.Save(1, session, new PlayerState(new GridPosition(1, 1)), DateTimeOffset.UnixEpoch);
            var (loaded, _) = store.Load(1);

            AssertSameRegion(region, loaded.Region);
            checkedCount++;
        }

        Assert.True(checkedCount > 0);
    }

    [Fact]
    public void EverythingThePlayerDidSurvivesASaveAndLoad()
    {
        var store = Store();
        var region = Region();
        var (session, playerState) = PlayedSession(region);

        var saved = store.Save(2, session, playerState, DateTimeOffset.UnixEpoch);
        var (loaded, loadedState) = store.Load(2);

        Assert.Equal(SaveFile.CurrentFormatVersion, saved.FormatVersion);
        Assert.Equal(GeneratorVersion.Current, saved.GeneratorVersion);
        Assert.Equal(SeedString.Encode(region.Settings), saved.SeedString);

        Assert.Equal(playerState, loadedState);
        Assert.Equal(session.Player, loaded.Player);
        Assert.Equal(session.CurrentAreaId, loaded.CurrentAreaId);
        Assert.Equal(session.LastPokemonCenterAreaId, loaded.LastPokemonCenterAreaId);
        Assert.Equal(session.Badges, loaded.Badges);
        Assert.Equal(session.Money, loaded.Money);
        Assert.Equal(session.Time.MinutesSinceMidnight, loaded.Time.MinutesSinceMidnight);
        Assert.Equal(session.HasFly, loaded.HasFly);
        Assert.Equal(session.Map.VisitedAreaIds.Order(), loaded.Map.VisitedAreaIds.Order());
        Assert.Equal(session.DefeatedTrainerIds.Order(), loaded.DefeatedTrainerIds.Order());
        Assert.True(loaded.HasDefeatedRivalBeat(RivalBeat.PostStarter));
        Assert.Equal(session.PendingFossilRevivals, loaded.PendingFossilRevivals);
        Assert.All(
            region.Special.Fossils,
            fossil => Assert.True(loaded.HasCollectedFossil(fossil.SpeciesId)));
        Assert.True(loaded.HasResolvedLegendary(region.Special.Legendaries[0].SpeciesId));
        Assert.True(loaded.HasResolvedLegendary(region.Special.Legendaries[1].SpeciesId));

        // Party, boxes, bag and dex, member for member.
        AssertSameMembers(session.Party.Members, loaded.Party.Members);
        AssertSameMembers(session.Storage.Stored, loaded.Storage.Stored);
        Assert.Equal("Pip", loaded.Party.Members[1].Nickname);
        Assert.Equal(session.Interactions.Bag.Items, loaded.Interactions.Bag.Items);
        Assert.Equal(session.Pokedex.Seen.Order(), loaded.Pokedex.Seen.Order());
        Assert.Equal(session.Pokedex.Caught.Order(), loaded.Pokedex.Caught.Order());
        Assert.True(loaded.Interactions.HasKey("Surf"));

        // Save anywhere includes inside a building.
        Assert.NotNull(loaded.ActiveInterior);
        Assert.Equal(session.ActiveInterior!.Entrance, loaded.ActiveInterior!.Entrance);
        Assert.Equal(session.ActiveInterior.OverworldAreaId, loaded.ActiveInterior.OverworldAreaId);
    }

    /// <summary>Loading must not replay the world: re-entering an area hands out keys and Fly, and a load
    /// that did that would gift the player everything their save file had already earned.</summary>
    [Fact]
    public void LoadingDoesNotHandOutKeysTheSaveNeverHad()
    {
        var store = Store();
        var region = Region(7);
        var keyArea = region.Gating.Gates[0].KeyAreaId;
        var session = new OverworldSession(region, NewGameIdentity.Create(CharacterChoice.CharacterA, "P"));
        session.VisitArea(keyArea);
        var keyName = region.Gating.Gates[0].KeyName;
        Assert.True(session.Interactions.HasKey(keyName));

        // A save taken in the key's area, then hand-edited to forget the key it granted.
        var snapshot = session.ToSnapshot(new PlayerState(new GridPosition(1, 1)));
        var forgetful = snapshot with
        {
            Interactions = snapshot.Interactions with { Keys = [] },
        };

        var loaded = OverworldSession.Restore(region, forgetful);

        Assert.False(loaded.Interactions.HasKey(keyName));
        Assert.Equal(keyArea, loaded.CurrentAreaId);
    }

    /// <summary>
    /// A save stores the generated region so it outlives generator bumps, which only works if the stamp
    /// keeps naming the generator that <em>built</em> the region. Re-saving a loaded region must therefore
    /// carry its version forward rather than relabelling it with the running build's.
    /// </summary>
    [Fact]
    public void ResavingALoadedRegionKeepsItsOriginalGeneratorStamp()
    {
        var store = Store();
        var region = Region();
        var session = new OverworldSession(region, NewGameIdentity.Create(CharacterChoice.CharacterA, "P"));
        var fresh = store.Save(1, session, new PlayerState(new GridPosition(1, 1)), DateTimeOffset.UnixEpoch);

        // A region generated here and now is stamped with the running generator.
        Assert.Equal(GeneratorVersion.Current, fresh.GeneratorVersion);
        Assert.Equal(GeneratorVersion.Current, session.RegionGeneratorVersion);

        // Stand in for a save written by an older generator, load it, and save again.
        var older = fresh.Session with { RegionGeneratorVersion = 1 };
        var reloaded = OverworldSession.Restore(fresh.Region.ToRegion(), older);
        Assert.Equal(1, reloaded.RegionGeneratorVersion);

        var resaved = store.Save(2, reloaded, new PlayerState(new GridPosition(1, 1)), DateTimeOffset.UnixEpoch);

        Assert.Equal(1, resaved.GeneratorVersion);
        Assert.Equal(1, SeedString.Decode(resaved.SeedString).GeneratorVersion);
        Assert.Equal(1, store.TrySummarize(2)!.GeneratorVersion);
        // The settings still decode, so the string stays shareable either way.
        Assert.Equal(region.Settings, SeedString.Decode(resaved.SeedString).Settings);
    }

    [Fact]
    public void SlotsAreIndependentAndListable()
    {
        var store = Store();
        var first = Region(2);
        var second = Region(99, badges: 4);

        var (sessionA, stateA) = PlayedSession(first);
        var sessionB = new OverworldSession(second, NewGameIdentity.Create(CharacterChoice.CharacterA, "Blue"));
        sessionB.SetBadges(7);

        store.Save(1, sessionA, stateA, DateTimeOffset.UnixEpoch);
        store.Save(3, sessionB, new PlayerState(new GridPosition(2, 2)), DateTimeOffset.UnixEpoch.AddDays(1));

        var slots = store.List();
        Assert.Equal(SaveStore.SlotCount, slots.Count);
        Assert.Equal("Ren", slots[0]!.PlayerName);
        Assert.Equal(3, slots[0]!.Badges);
        Assert.Equal(Party.MaxSize, slots[0]!.PartyCount);
        Assert.Null(slots[1]);
        Assert.Equal("Blue", slots[2]!.PlayerName);
        Assert.Equal(7, slots[2]!.Badges);
        Assert.Equal(SeedString.Encode(second.Settings), slots[2]!.SeedString);

        // Each slot keeps its own region.
        Assert.Equal(first.Settings.Seed, store.Load(1).Session.Region.Settings.Seed);
        Assert.Equal(second.Settings.Seed, store.Load(3).Session.Region.Settings.Seed);

        Assert.True(store.Delete(1));
        Assert.False(store.Delete(1));
        Assert.Null(store.TrySummarize(1));
        Assert.NotNull(store.TrySummarize(3));
    }

    [Fact]
    public void AnUnreadableSlotReadsAsEmptyRatherThanBreakingTheOthers()
    {
        var store = Store();
        var region = Region();
        var session = new OverworldSession(region, NewGameIdentity.Create(CharacterChoice.CharacterA, "P"));
        store.Save(2, session, new PlayerState(new GridPosition(1, 1)), DateTimeOffset.UnixEpoch);

        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.PathOf(1), "{ not a save file");

        var slots = store.List();
        Assert.Null(slots[0]);
        Assert.NotNull(slots[1]);
        Assert.Throws<FileNotFoundException>(() => store.Read(3));
    }

    [Fact]
    public void SlotNumbersOutsideTheRangeAreRejected()
    {
        var store = Store();
        Assert.Throws<ArgumentOutOfRangeException>(() => store.PathOf(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.PathOf(SaveStore.SlotCount + 1));
    }

    /// <summary>The Phase 7 provisions (GDD §15): a save has somewhere to put a second region and its level
    /// cap before either exists, so region 1 never needs regenerating to make room.</summary>
    [Fact]
    public void TheSchemaReservesRoomForASecondRegionAndItsLevelCap()
    {
        var store = Store();
        var region = Region();
        var session = new OverworldSession(region, NewGameIdentity.Create(CharacterChoice.CharacterA, "P"));
        var file = store.Save(1, session, new PlayerState(new GridPosition(1, 1)), DateTimeOffset.UnixEpoch);

        Assert.Null(file.SecondRegion);
        Assert.Null(file.LevelCapBadges);

        var withSecond = file with
        {
            SecondRegion = RegionSnapshot.From(Region(1234, badges: 4)),
            LevelCapBadges = 5,
        };
        var json = System.Text.Json.JsonSerializer.Serialize(withSecond, SaveJson.Options);
        var reloaded = System.Text.Json.JsonSerializer.Deserialize<SaveFile>(json, SaveJson.Options)!;

        Assert.Equal(5, reloaded.LevelCapBadges);
        AssertSameRegion(withSecond.SecondRegion!.ToRegion(), reloaded.SecondRegion!.ToRegion());
    }
}
