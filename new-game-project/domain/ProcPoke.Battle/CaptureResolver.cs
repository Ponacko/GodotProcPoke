namespace ProcPoke.Battle;

/// <summary>Inputs to one Gen 5 capture attempt.</summary>
public sealed record CaptureAttempt
{
    public required int CatchRate { get; init; }
    public required int MaxHp { get; init; }
    public required int Hp { get; init; }
    public BattleStatus Status { get; init; }
    public double BallMultiplier { get; init; } = 1.0;
}

/// <summary>The four shake comparisons are retained for replay and presentation.</summary>
public sealed record CaptureResult(
    bool Captured,
    int ModifiedCatchValue,
    int ShakeThreshold,
    IReadOnlyList<bool> Shakes)
{
    public int SuccessfulShakes => Shakes.TakeWhile(shook => shook).Count();
}

/// <summary>
/// Gen 5 capture formula and shake checks. The caller supplies the ball multiplier because ball
/// inventory is a Phase 5 concern; a regular Poké Ball uses 1.0.
/// </summary>
public static class CaptureResolver
{
    public static CaptureResult Resolve(CaptureAttempt attempt, IBattleRandom random)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(random);
        Validate(attempt);

        var hpNumerator = (3L * attempt.MaxHp - 2L * attempt.Hp) * attempt.CatchRate;
        var hpDenominator = 3L * attempt.MaxHp;
        var hpAndBall = Math.Floor(hpNumerator * attempt.BallMultiplier / hpDenominator);
        var modified = (int)Math.Clamp(
            Math.Floor(hpAndBall * StatusMultiplier(attempt.Status)), 0, 255);

        if (modified >= 255)
            return new CaptureResult(true, modified, 65536, [true, true, true, true]);
        if (modified == 0)
            return new CaptureResult(false, 0, 0, [false, false, false, false]);

        var threshold = (int)Math.Floor(
            65536.0 / Math.Pow(255.0 / modified, 0.25));
        threshold = Math.Clamp(threshold, 0, 65535);
        var shakes = Enumerable.Range(0, 4)
            .Select(_ => random.NextInt(65536) < threshold)
            .ToArray();
        return new CaptureResult(shakes.All(shook => shook), modified, threshold, shakes);
    }

    private static double StatusMultiplier(BattleStatus status) => status switch
    {
        BattleStatus.Sleep or BattleStatus.Freeze => 2.5,
        BattleStatus.Poison or BattleStatus.BadPoison or BattleStatus.Burn or BattleStatus.Paralysis => 1.5,
        _ => 1.0,
    };

    private static void Validate(CaptureAttempt attempt)
    {
        if (attempt.CatchRate is < 1 or > 255)
            throw new ArgumentOutOfRangeException(nameof(attempt), "Catch rate must be in [1, 255].");
        if (attempt.MaxHp <= 0)
            throw new ArgumentOutOfRangeException(nameof(attempt), "Max HP must be positive.");
        if (attempt.Hp is < 1 || attempt.Hp > attempt.MaxHp)
            throw new ArgumentOutOfRangeException(nameof(attempt), "Current HP must be in [1, Max HP].");
        if (attempt.BallMultiplier <= 0 || double.IsNaN(attempt.BallMultiplier))
            throw new ArgumentOutOfRangeException(nameof(attempt), "Ball multiplier must be positive.");
    }
}
