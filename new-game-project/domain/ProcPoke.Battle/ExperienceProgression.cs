using ProcPoke.Data;

namespace ProcPoke.Battle;

public sealed record ExperienceProgress
{
    public required int TotalExperience { get; init; }
    public required StatBlock EffortValues { get; init; }
}

public sealed record ExperienceGain(
    int ExperienceGained,
    int PreviousLevel,
    int NewLevel,
    IReadOnlyList<int> LevelsGained,
    ExperienceProgress Progress);

/// <summary>Gen 5 scaled experience formula and growth-curve lookup.</summary>
public static class ExperienceCalculator
{
    /// <summary>
    /// Calculates the B2W2 scaled reward. Trainer battles use the 1.5 multiplier and multiple
    /// participants divide the reward; the recipient's own level supplies the scaling denominator.
    /// </summary>
    public static int ForDefeat(
        int defeatedBaseExperience,
        int defeatedLevel,
        int recipientLevel,
        bool trainerBattle = false,
        int participants = 1)
    {
        if (defeatedBaseExperience < 0)
            throw new ArgumentOutOfRangeException(nameof(defeatedBaseExperience));
        if (defeatedLevel is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(defeatedLevel));
        if (recipientLevel is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(recipientLevel));
        if (participants < 1)
            throw new ArgumentOutOfRangeException(nameof(participants));
        if (recipientLevel == 100 || defeatedBaseExperience == 0) return 0;

        var scale = Math.Pow(
            (2.0 * defeatedLevel + 10.0) / (defeatedLevel + recipientLevel + 10.0),
            2.5);
        var trainerMultiplier = trainerBattle ? 1.5 : 1.0;
        var reward = defeatedBaseExperience * defeatedLevel / 5.0
            * scale * trainerMultiplier / participants;
        return Math.Max(1, (int)Math.Floor(reward) + 1);
    }

    public static int LevelAt(GrowthRate growthRate, int totalExperience)
    {
        ArgumentNullException.ThrowIfNull(growthRate);
        if (growthRate.CumulativeExperience.Count != 101)
            throw new ArgumentException("Growth curves must contain levels 1 through 100.", nameof(growthRate));
        if (totalExperience < 0)
            throw new ArgumentOutOfRangeException(nameof(totalExperience));

        var curve = growthRate.CumulativeExperience;
        var experience = Math.Min(totalExperience, curve[100]);
        var low = 1;
        var high = 100;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (curve[middle] <= experience) low = middle;
            else high = middle - 1;
        }
        return low;
    }
}

/// <summary>Applies a defeat reward, including the Gen 5 EV caps.</summary>
public static class ExperienceProgression
{
    private const int PerStatEvCap = 255;
    private const int TotalEvCap = 510;

    public static ExperienceGain ApplyDefeat(
        GameData data,
        int recipientSpeciesId,
        ExperienceProgress current,
        int defeatedSpeciesId,
        int defeatedLevel,
        bool trainerBattle = false,
        int participants = 1)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(current);
        if (!data.Species.TryGetValue(recipientSpeciesId, out var recipient))
            throw new ArgumentException($"Unknown recipient species {recipientSpeciesId}.", nameof(recipientSpeciesId));
        if (!data.Species.TryGetValue(defeatedSpeciesId, out var defeated))
            throw new ArgumentException($"Unknown defeated species {defeatedSpeciesId}.", nameof(defeatedSpeciesId));
        if (!data.GrowthRates.TryGetValue(recipient.GrowthRate, out var growthRate))
            throw new InvalidOperationException($"Missing growth rate {recipient.GrowthRate}.");

        var curve = growthRate.CumulativeExperience;
        if (current.TotalExperience < 0 || current.TotalExperience > curve[100])
            throw new ArgumentOutOfRangeException(nameof(current), "Total experience is outside the growth curve.");
        ValidateEffortValues(current.EffortValues);

        var previousLevel = ExperienceCalculator.LevelAt(growthRate, current.TotalExperience);
        var gained = ExperienceCalculator.ForDefeat(
            defeated.BaseExperience, defeatedLevel, previousLevel, trainerBattle, participants);
        var totalExperience = Math.Min(curve[100], current.TotalExperience + gained);
        var newLevel = ExperienceCalculator.LevelAt(growthRate, totalExperience);
        var levels = Enumerable.Range(previousLevel + 1, newLevel - previousLevel).ToArray();
        var effortValues = AddEffortValues(current.EffortValues, defeated.EvYield);

        return new ExperienceGain(
            gained,
            previousLevel,
            newLevel,
            levels,
            new ExperienceProgress
            {
                TotalExperience = totalExperience,
                EffortValues = effortValues,
            });
    }

    private static StatBlock AddEffortValues(StatBlock current, StatBlock yield)
    {
        var values = new[] { current.Hp, current.Attack, current.Defense,
            current.SpecialAttack, current.SpecialDefense, current.Speed };
        var yields = new[] { yield.Hp, yield.Attack, yield.Defense,
            yield.SpecialAttack, yield.SpecialDefense, yield.Speed };
        var total = values.Sum();
        for (var i = 0; i < values.Length; i++)
        {
            var room = Math.Min(PerStatEvCap - values[i], TotalEvCap - total);
            var added = Math.Min(Math.Max(0, yields[i]), Math.Max(0, room));
            values[i] += added;
            total += added;
        }

        return new StatBlock(values[0], values[1], values[2], values[3], values[4], values[5]);
    }

    private static void ValidateEffortValues(StatBlock values)
    {
        var entries = new[] { values.Hp, values.Attack, values.Defense,
            values.SpecialAttack, values.SpecialDefense, values.Speed };
        if (entries.Any(value => value is < 0 or > PerStatEvCap) || entries.Sum() > TotalEvCap)
            throw new ArgumentOutOfRangeException(nameof(values), "Effort values exceed Gen 5 caps.");
    }
}
