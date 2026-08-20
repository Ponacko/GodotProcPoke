using System.Text.Json;
using ProcPoke.Generation;

namespace ProcPoke.Overworld.Save;

/// <summary>
/// The save slots on disk (GDD §11: multiple slots, save anywhere). Engine-independent — it takes a
/// directory, so Godot passes <c>user://</c> and tests pass a temporary folder.
///
/// Writes go to a temporary file and are then moved into place, so a crash mid-write costs the new save
/// rather than the old one: a half-written slot is the one bug a save system must not have.
/// </summary>
public sealed class SaveStore
{
    public const int SlotCount = 3;

    private readonly string _directory;

    public SaveStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public string PathOf(int slot)
    {
        RequireSlot(slot);
        return Path.Combine(_directory, $"slot{slot}.json");
    }

    public bool Exists(int slot) => File.Exists(PathOf(slot));

    /// <summary>
    /// Writes the session to a slot. <paramref name="playerState"/> is where the player is standing, which
    /// the session does not own (movement lives in the presentation layer) but a save must record;
    /// <paramref name="savedAt"/> is passed in because domain code never reads the clock.
    /// </summary>
    public SaveFile Save(int slot, OverworldSession session, PlayerState playerState, DateTimeOffset savedAt)
    {
        RequireSlot(slot);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(playerState);

        var file = new SaveFile
        {
            FormatVersion = SaveFile.CurrentFormatVersion,
            GeneratorVersion = GeneratorVersion.Current,
            SeedString = Generation.SeedString.Encode(session.Region.Settings),
            SavedAt = savedAt,
            Region = RegionSnapshot.From(session.Region),
            Session = session.ToSnapshot(playerState),
        };

        Directory.CreateDirectory(_directory);
        var path = PathOf(slot);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(file, SaveJson.Options));
        File.Move(temporary, path, overwrite: true);
        return file;
    }

    public SaveFile Read(int slot)
    {
        RequireSlot(slot);
        var path = PathOf(slot);
        if (!File.Exists(path)) throw new FileNotFoundException($"save slot {slot} is empty", path);

        var file = JsonSerializer.Deserialize<SaveFile>(File.ReadAllText(path), SaveJson.Options)
            ?? throw new InvalidDataException($"save slot {slot} is not a save file");
        if (file.FormatVersion != SaveFile.CurrentFormatVersion)
            throw new InvalidDataException(
                $"save slot {slot} uses save format {file.FormatVersion}; this build reads " +
                $"{SaveFile.CurrentFormatVersion}");
        return file;
    }

    /// <summary>
    /// Loads a slot back into a playable session. The region comes out of the file as it was generated, so
    /// a save made by an older generator keeps working — that is the whole reason a save stores the region
    /// rather than its seed.
    /// </summary>
    public (OverworldSession Session, PlayerState PlayerState) Load(int slot)
    {
        var file = Read(slot);
        var region = file.Region.ToRegion();
        return (OverworldSession.Restore(region, file.Session), file.Session.Location.Player);
    }

    /// <summary>What the slot picker shows. A slot that cannot be read is reported as empty rather than
    /// throwing: one corrupt file must not stop the player loading the other two.</summary>
    public IReadOnlyList<SaveSlotSummary?> List()
    {
        return Enumerable.Range(1, SlotCount).Select(TrySummarize).ToArray();
    }

    public SaveSlotSummary? TrySummarize(int slot)
    {
        try
        {
            var file = Read(slot);
            return new SaveSlotSummary(
                slot,
                file.Session.Player.Name,
                file.Session.Badges,
                file.Session.Party.Count,
                file.Session.PokedexCaught.Count,
                file.Session.CreditsReached,
                file.SeedString,
                file.GeneratorVersion,
                file.SavedAt);
        }
        catch (Exception exception)
            when (exception is IOException or JsonException or InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }

    public bool Delete(int slot)
    {
        var path = PathOf(slot);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    private static void RequireSlot(int slot)
    {
        if (slot is < 1 || slot > SlotCount)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, $"slots are 1..{SlotCount}");
    }
}
