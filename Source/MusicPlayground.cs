using System.Text.RegularExpressions;

namespace SpectrumKlinePlayer;

public enum SongPersonality { Wild, SlowBull, Rollercoaster, LimitFlip, SlowBear, Selloff, BullTrap }
public enum LyricEffect { Attack, Retreat, Volatile, Calm }
public sealed record LyricRule(string Keyword, LyricEffect Effect, double Seconds = 6,
    double Strength = 1, string Source = "Manual", int SongCount = 0, double? EmotionIntensity = null);
public sealed record PlaygroundSettings(bool BullBear = false, bool BlindBox = false,
    bool Chorus = false, bool LyricCards = false, float MenuSize = 10, LyricRule[]? Rules = null, int KeywordLibraryVersion = 0);
public sealed record PlayEvent(double Seconds, string Name, string Lyric);
public sealed record MusicAchievement(string Name, string Song, double Seconds, string Lyric, DateTime EarnedAt);

public sealed class MusicPlayground
{
    private readonly Random random;
    public PlaygroundSettings Settings { get; set; } = new();
    public SongPersonality Personality { get; private set; }
    public readonly List<PlayEvent> Events = new();
    private readonly Dictionary<string, double> lyricSeen = new();
    private readonly HashSet<string> lyricVisits = new();
    private int lastLyric = -1;
    private double played, lastChorus = -100, cardUntil, chorusUntil, lastPosition = -1;
    private LyricEffect cardEffect;
    private double cardStrength = 1;
    private int chorusType, flip = -1;
    private double notificationUntil;
    public string Notification { get; private set; } = "";
    public static LyricRule[] DefaultRules => LyricRuleLearning.BuiltInRules;
    public static readonly int[] MenuSizes = [10, 11, 12, 14, 16, 18, 20, 22, 24];
    public MusicPlayground(Random? random = null) { this.random = random ?? new Random(); Reset(); }
    public string PersonalityName => Personality switch { SongPersonality.Wild => "妖股", SongPersonality.SlowBull => "慢牛", SongPersonality.Rollercoaster => "过山车", SongPersonality.LimitFlip => "天地板", SongPersonality.SlowBear => "慢熊 · 持续阴跌", SongPersonality.Selloff => "杀跌 · 加速回落", SongPersonality.BullTrap => "诱多 · 先拉后砸", _ => "未知" };
    public string BoxStatus => !Settings.BlindBox ? "盲盒关闭" : played < 12 ? "歌曲盲盒 · 待揭晓" : "歌曲盲盒 · " + PersonalityName;
    public string CurrentNotification(double seconds) => seconds <= notificationUntil ? Notification : "";
    public void Reset()
    {
        Personality = (SongPersonality)random.Next(Enum.GetValues<SongPersonality>().Length); played = 0; cardUntil = chorusUntil = 0;
        lastLyric = -1; lastPosition = -1; lastChorus = -100; flip = -1;
        Events.Clear(); lyricSeen.Clear(); lyricVisits.Clear(); Notification = ""; notificationUntil = 0;
    }
    public void ClearSeekEffects()
    {
        cardUntil = chorusUntil = 0; lastLyric = -1; lastChorus = -100; lastPosition = -1;
        lyricSeen.Clear(); lyricVisits.Clear(); Notification = "";
    }
    public void Update(double position, double dt, int lyricIndex, string lyric)
    {
        dt = Math.Clamp(dt, 0, .25); played += dt;
        if (lastPosition >= 0 && (position < lastPosition - 1 || position - lastPosition > 3)) ClearSeekEffects();
        lastPosition = position;
        if (Settings.BlindBox && played >= 12 && played - dt < 12) Emit(position, "盲盒揭晓：" + PersonalityName, lyric);
        if (lyricIndex < 0 || lyricIndex == lastLyric || string.IsNullOrWhiteSpace(lyric)) return;
        lastLyric = lyricIndex;
        string key = Regex.Replace(lyric, @"[\s\p{P}\p{S}]", "");
        if (!lyricVisits.Add(lyricIndex + ":" + key)) return;
        if (Settings.Chorus && key.Length >= 5 && !key.Contains('：') &&
            lyricSeen.TryGetValue(key, out double prior) && position - prior >= 20 && position - lastChorus >= 25)
        {
            chorusType = random.Next(3); chorusUntil = position + 8; lastChorus = position;
            Emit(position, new[] { "副歌返场 · 二波上涨", "副歌返场 · 反包进攻", "副歌返场 · 冲高回落" }[chorusType], lyric);
        }
        lyricSeen[key] = position;
        if (Settings.LyricCards)
        {
            var rule = (Settings.Rules ?? DefaultRules).FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Keyword) && LyricRuleLearning.Matches(r, lyric));
            if (rule is not null)
            {
                cardEffect = rule.Effect; cardStrength = Math.Clamp(rule.Strength, .3, 2); cardUntil = position + rule.Seconds;
                Emit(position, $"歌词事件 · {rule.Keyword} · {EffectName(rule.Effect)} · {LyricRuleLearning.EmotionText(rule)} · {rule.Seconds:0.#}秒", lyric, rule.Seconds);
            }
        }
    }
    private void Emit(double seconds, string name, string lyric, double visibleSeconds = 8)
    {
        Notification = name; notificationUntil = seconds + visibleSeconds; Events.Add(new(seconds, name, lyric));
        if (Events.Count > 1000) Events.RemoveAt(0);
    }
    public (float BiasPerSecond, float Volatility) Influence(double seconds, float bass, float treble, float voice)
    {
        float bias = 0, volatility = 1;
        if (Settings.BullBear) { bias += (bass - treble) * .08f; volatility *= Math.Clamp(1 - voice * .45f, .5f, 1); }
        if (Settings.BlindBox)
        {
            switch (Personality)
            {
                case SongPersonality.Wild: volatility *= 2.5f; bias += (float)Math.Sin(seconds * 1.7) * .025f; break;
                case SongPersonality.SlowBull: volatility *= .45f; bias += .009f; break;
                case SongPersonality.Rollercoaster: volatility *= 1.7f; bias += (float)Math.Sin(seconds * .22) * .035f; break;
                case SongPersonality.SlowBear: volatility *= .45f; bias -= .009f; break;
                case SongPersonality.Selloff: volatility *= 1.8f; bias -= .025f; break;
                // Phase follows time actually played since drawing, including mid-song starts.
                case SongPersonality.BullTrap: volatility *= 1.2f; bias += played < 18 ? .012f : -.025f; break;
            }
        }
        if (Settings.Chorus && seconds < chorusUntil)
            bias += chorusType == 0 ? .018f : chorusType == 1 ? .032f : seconds < chorusUntil - 4 ? .025f : -.03f;
        if (Settings.LyricCards && seconds < cardUntil)
        {
            if (cardEffect == LyricEffect.Attack) bias += .022f * (float)cardStrength;
            if (cardEffect == LyricEffect.Retreat) bias -= .022f * (float)cardStrength;
            if (cardEffect == LyricEffect.Volatile) volatility *= 1 + (float)cardStrength;
            if (cardEffect == LyricEffect.Calm) volatility *= Math.Clamp(1 - .65f * (float)cardStrength, .15f, 1);
        }
        return (bias, Math.Clamp(volatility, .15f, 4));
    }
    public int NextFlipDirection() { flip = -flip; return flip; }
    public void Reroll(double seconds)
    {
        Personality = (SongPersonality)random.Next(Enum.GetValues<SongPersonality>().Length); played = 0; flip = -1;
        Emit(seconds, "重新抽取盲盒 · 待揭晓", "");
    }
    public static string EffectName(LyricEffect effect) => effect switch { LyricEffect.Attack => "进攻", LyricEffect.Retreat => "回落", LyricEffect.Volatile => "放大波动", _ => "稳定走势" };
}

public sealed record TradeFill(double Seconds, string Side, int Quantity, double Price, double Equity, string Lyric);
public sealed record TradeRound(string Song, DateTime StartedAt, double FinalEquity, TradeFill[] Fills);
public sealed class PaperTrading
{
    public const double StartingCash = 100000;
    public double Cash { get; private set; } = StartingCash;
    public int Shares { get; private set; }
    public double Cost { get; private set; }
    public readonly List<TradeFill> Fills = new();
    public long Revision { get; private set; }
    public double Equity(double price) => Cash + Shares * price;
    public void Reset() { Cash = StartingCash; Shares = 0; Cost = 0; Fills.Clear(); Revision++; }
    public bool Buy(double price, double fraction, double seconds, string lyric)
    {
        if (!double.IsFinite(price) || price <= 0 || fraction <= 0 || fraction > 1 || Fills.Count >= 2000) return false;
        int quantity = (int)Math.Min(int.MaxValue, Math.Floor(Cash * fraction / price));
        if (quantity <= 0) return false;
        Cost = (Cost * Shares + quantity * price) / (Shares + quantity);
        Cash -= quantity * price; Shares += quantity;
        Fills.Add(new(seconds, "买入", quantity, price, Equity(price), lyric)); Revision++; return true;
    }
    public bool Sell(double price, double seconds, string lyric)
    {
        if (!double.IsFinite(price) || price <= 0 || Shares == 0 || Fills.Count >= 2000) return false;
        int quantity = Shares; Cash += Shares * price; Shares = 0; Cost = 0;
        Fills.Add(new(seconds, "卖出", quantity, price, Equity(price), lyric)); Revision++; return true;
    }
}

public sealed record ArenaScore(double Burst, double Rhythm, double Boards)
{
    public double Total => Burst * .4 + Rhythm * .3 + Boards * .3;
    public static ArenaScore Calculate(SongRecording r)
    {
        var values = r.Samples;
        if (values.Length == 0) return new(0, 0, 0);
        double burst = values.Max(s => s.Energy) * 100;
        var steps = values.Zip(values.Skip(1)).Where(p => p.Second.Segment == p.First.Segment && p.Second.Seconds - p.First.Seconds is > 0 and < 2)
            .Select(p => Math.Abs(p.Second.Energy - p.First.Energy)).ToArray();
        double rhythm = steps.Length == 0 ? 0 : Math.Min(100, steps.Average() * 500);
        int boards = values.Select(s => Regex.Match(s.Event, @"[涨跌]停 (\d+)/")).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(0).Max();
        return new(Math.Clamp(burst, 0, 100), rhythm, Math.Min(100, boards * 20));
    }
}
