namespace ProcPoke.Generation.Trainers;

/// <summary>A species and level in an NPC trainer's roster. Phase 2 deliberately omits movesets.</summary>
public sealed record TrainerMember(int SpeciesId, int Level);

/// <summary>One generated trainer, either standing on a carved TrainerPost or hosted by a gym city.</summary>
public sealed record TrainerEncounter(
    int AreaId,
    (int X, int Y)? Position,
    string ClassName,
    string? TeamName,
    IReadOnlyList<TrainerMember> Roster,
    bool IsGymTrainer = false)
{
    public string DisplayClass => TeamName is null ? ClassName : $"{TeamName} {ClassName}";
    public int AceLevel => Roster.Count == 0 ? 0 : Roster.Max(m => m.Level);
}

/// <summary>Contents assigned to one carved ItemBall tile.</summary>
public sealed record ItemBallPlacement(int AreaId, (int X, int Y) Position, int ItemId);

/// <summary>All trainer and item-ball population generated for a region.</summary>
public sealed record TrainerPlan
{
    public required IReadOnlyDictionary<int, IReadOnlyList<TrainerEncounter>> ByArea { get; init; }
    public required IReadOnlyDictionary<int, IReadOnlyList<ItemBallPlacement>> ItemsByArea { get; init; }

    public IReadOnlyList<TrainerEncounter> TrainersOf(int areaId)
        => ByArea.TryGetValue(areaId, out var trainers) ? trainers : [];

    public IReadOnlyList<ItemBallPlacement> ItemsOf(int areaId)
        => ItemsByArea.TryGetValue(areaId, out var items) ? items : [];
}
