namespace SpectrumKlinePlayer;

public sealed record ClimaxSettings(bool Enabled = true, double Threshold = .64,
    double HoldSeconds = 1.5, double TriggerProbability = .65, int MinimumBoards = 2,
    int MaximumBoards = 5, double OneWordProbability = .65,
    double BreakProbabilityPerSecond = .003, double ExtremeSeconds = 12);

public sealed record LimitBoard(int Direction, double PreviousClose, double Limit,
    double Open, bool OneWord, int Number, int Total);

// Values use the same logarithmic price index as the indicator calculations.
public sealed class ClimaxMarket
{
    private readonly Random random;
    public ClimaxSettings Settings { get; set; } = new();
    public LimitBoard? Board { get; private set; }
    public bool Broken { get; private set; }
    public bool Resealed { get; private set; }
    public bool AllowReseal { get; set; }
    public bool HasTriggered { get; private set; }
    private double brokenSeconds;
    public double HighSeconds { get; private set; }
    public double LowSeconds { get; private set; }
    private double cooldown;
    private int remaining, total, direction;
    public ClimaxMarket(Random? random = null) => this.random = random ?? new Random();

    public void Reset()
    {
        HasTriggered = false;
        Cancel();
    }

    // Seeks and settings changes cancel the active group without granting another one.
    public void Cancel()
    {
        Board = null; Broken = Resealed = false; brokenSeconds = 0; remaining = total = direction = 0;
        HighSeconds = LowSeconds = cooldown = 0;
    }

    public void Observe(double energy, double seconds)
    {
        seconds = Math.Clamp(seconds, 0, .25);
        if (!Settings.Enabled) { Cancel(); return; }
        cooldown = Math.Max(0, cooldown - seconds);
        HighSeconds = energy >= Settings.Threshold || HighSeconds > 0 && energy >= Settings.Threshold - .04 ? HighSeconds + seconds : 0;
        LowSeconds = energy <= .25 ? LowSeconds + seconds : 0;
        if (Board is { OneWord: true } && !Broken && !Resealed)
        {
            double extreme = Math.Max(HighSeconds, LowSeconds);
            double rate = Settings.BreakProbabilityPerSecond;
            if (extreme > Settings.ExtremeSeconds)
                rate += Math.Min(.35, (extreme - Settings.ExtremeSeconds) * .015);
            if (random.NextDouble() < 1 - Math.Exp(-rate * seconds)) Broken = true;
        }
        if (Broken)
        {
            brokenSeconds += seconds;
            if (AllowReseal && brokenSeconds >= 2 && energy >= Settings.Threshold + .05 && random.NextDouble() < 1 - Math.Exp(-.12 * seconds))
            { Broken = false; Resealed = true; }
        }
    }

    public LimitBoard? BeginBar(double previousClose)
    {
        Board = null; Broken = Resealed = false; brokenSeconds = 0;
        if (!Settings.Enabled) { remaining = 0; return null; }
        if (!HasTriggered && remaining == 0 && cooldown <= 0 && HighSeconds >= Settings.HoldSeconds
            && random.NextDouble() < Settings.TriggerProbability)
        {
            direction = random.Next(2) == 0 ? -1 : 1;
            total = remaining = random.Next(Settings.MinimumBoards, Settings.MaximumBoards + 1);
        }
        if (remaining == 0) return null;
        // Stop at the existing chart domain edge instead of drawing a fake limit.
        double limit = previousClose + Math.Log(direction > 0 ? 1.10 : .90);
        if (Math.Abs(limit) > 3.9) { remaining = 0; cooldown = 8; return null; }
        bool oneWord = random.NextDouble() < Settings.OneWordProbability;
        double open = oneWord ? limit : previousClose + (limit - previousClose) * (.55 + random.NextDouble() * .35);
        Board = new(direction, previousClose, limit, open, oneWord, total - remaining + 1, total);
        HasTriggered = true;
        if (--remaining == 0) cooldown = 8;
        return Board;
    }

    public string Status => Board is not { } b ? "等待高潮" :
        $"{(b.Direction > 0 ? "涨停" : "跌停")} {b.Number}/{b.Total} · {(Broken ? "炸板" : Resealed ? "炸板回封" : b.OneWord ? "一字板" : b.Direction > 0 ? "大幅高开" : "大幅低开")}";
    public void QueueSingleBoard(int sign)
    {
        if (!Settings.Enabled || HasTriggered || remaining > 0) return;
        direction = sign > 0 ? 1 : -1; remaining = total = 1;
    }
}
