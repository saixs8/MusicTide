using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SpectrumKlinePlayer;

public sealed record HeardSong(string Title, string[] Lines);
public sealed record RuleSuggestion(LyricRule Rule, string Reason);
public sealed record LyricAiSettings(bool Automatic = true, bool UseAi = false, bool AutoApply = true,
    string Endpoint = "http://127.0.0.1:11434", string Model = "qwen2.5:1.5b",
    string[]? Suppressed = null);
public sealed record LyricLearningState(string Fingerprint = "", DateTime? UpdatedAt = null,
    int HeardSongs = 0, string Status = "尚未学习", RuleSuggestion[]? Suggestions = null);

public static class LyricRuleLearning
{
    public const int MaximumRules = 200;
    private static readonly string[][] Words =
    [
        ["燃烧", "勇敢", "梦想", "希望", "奔跑", "飞翔", "胜利", "坚持", "突破", "追逐", "热爱", "闪耀", "向前", "自由", "光芒", "力量"],
        ["再见", "离开", "失去", "孤独", "眼泪", "心碎", "遗憾", "放弃", "难过", "悲伤", "绝望", "凋零", "沉默", "迷失", "告别", "坠落"],
        ["疯狂", "爆炸", "风暴", "雷鸣", "呐喊", "狂奔", "沸腾", "失控", "震撼", "狂欢", "闪电", "汹涌", "撕裂", "挣扎", "颤抖", "躁动"],
        ["平静", "温柔", "安静", "宁静", "微风", "月光", "星光", "拥抱", "陪伴", "守候", "晚安", "安心", "轻轻", "慢慢", "安然", "停泊"]
    ];
    private static readonly string[][] Extra =
    [
        ["青春", "启程", "明天", "远方", "盛开", "绽放", "翅膀", "扬帆", "重生", "不认输", "不退缩", "不放弃", "冲破", "逆风", "追梦", "炽热", "出发", "未来", "抬起头", "向阳"],
        ["想念", "思念", "寂寞", "落寞", "回不去", "舍不得", "错过", "散场", "离别", "心痛", "破碎", "流泪", "疲惫", "空荡", "忘不了", "失落", "最后", "过去", "远去", "孤单"],
        ["疯狂的", "心跳", "澎湃", "狂野", "尖叫", "旋转", "动荡", "逆流", "崩塌", "疯狂地", "浪潮", "海啸", "怒吼", "狂风", "暴雨", "烈火", "狂热", "颠倒", "颠覆", "激荡"],
        ["细雨", "清晨", "黄昏", "夜色", "暖阳", "港湾", "等候", "相依", "依偎", "牵手", "暖暖", "静静", "悠悠", "轻柔", "安心睡", "靠近", "缓缓", "休息", "恬静", "柔软"]
    ];
    public static LyricRule[] BuiltInRules => [new("燃烧",LyricEffect.Attack,Source:"Builtin"),new("梦想",LyricEffect.Attack,Source:"Builtin"),new("再见",LyricEffect.Retreat,Source:"Builtin"),new("疯狂",LyricEffect.Volatile,Source:"Builtin"),new("平静",LyricEffect.Calm,Source:"Builtin")];
    public static LyricRule[] LearningVocabulary
    {
        get
        {
            // Keep the four legacy examples first, preserving their familiar priority.
            var all = Words.SelectMany((words, i) => words.Select(w => new LyricRule(w, (LyricEffect)i, 6, 1, "Builtin"))).ToList();
            all.AddRange(new[] { "不放弃", "不认输", "不退缩" }.Select(w => new LyricRule(w, LyricEffect.Attack, 6, 1, "Builtin")));
            string[] first = ["燃烧", "再见", "疯狂", "平静", "不放弃", "不认输", "不退缩"];
            return first.Select(w => all.Single(r => r.Keyword == w)).Concat(all.Where(r => !first.Contains(r.Keyword))).ToArray();
        }
    }
    public static LyricRule[] Enrich(IEnumerable<LyricRule> rules) => rules.Concat(BuiltInRules)
        .DistinctBy(r => r.Keyword, StringComparer.OrdinalIgnoreCase).Take(MaximumRules).ToArray();
    public static LyricRule[] CompactDefaults(IEnumerable<LyricRule> rules) =>
        Enrich(rules.Where(r => r.Source != "Builtin" || BuiltInRules.Any(d => d.Keyword == r.Keyword)));
    public static HeardSong[] Corpus(IEnumerable<SongRecording> records)
    {
        return records.Where(r => !r.Simulated && r.Samples.Length > 0 && r.Lyrics.Length > 0)
            .OrderByDescending(r => r.RecordedAt).GroupBy(r => r.Title, StringComparer.OrdinalIgnoreCase).Take(20)
            .Select(group => new HeardSong(group.Key, group.SelectMany(r =>
            {
                var heard = r.Samples.Select(s => LyricPlaybackClock.FindActive(r.Lyrics, s.Seconds)).Where(i => i >= 0).Distinct().Order().ToArray();
                return heard.Select(i => r.Lyrics[i].Text.Trim());
            }).Where(t => t.Length is >= 2 and <= 100 && !System.Text.RegularExpressions.Regex.IsMatch(t, @"^(作词|作曲|编曲|制作|演唱|词|曲)\s*[:：]"))
                .Distinct(StringComparer.OrdinalIgnoreCase).TakeLast(12).ToArray()))
            .Where(s => s.Lines.Length > 0).ToArray();
    }
    public static string Fingerprint(HeardSong[] songs, LyricAiSettings settings) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { songs, settings.UseAi, settings.AutoApply, settings.Endpoint, settings.Model, settings.Suppressed, Version = 2 }))));
    public static int SongCount(string keyword, HeardSong[] songs) => songs.Count(s => s.Lines.Any(l => l.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
    public static RuleSuggestion[] Local(HeardSong[] songs, string[]? suppressed = null)
    {
        var blocked = new HashSet<string>(suppressed ?? [], StringComparer.OrdinalIgnoreCase);
        var vocabulary = LearningVocabulary.Concat(Extra.SelectMany((words, i) => words.Select(w => new LyricRule(w, (LyricEffect)i))));
        return vocabulary.DistinctBy(r => r.Keyword, StringComparer.OrdinalIgnoreCase).Select(r => (Rule: r, Count: songs.Count(s => s.Lines.Any(l => Matches(r, l)))))
            .Where(p => p.Count > 0 && !blocked.Contains(p.Rule.Keyword))
            .OrderByDescending(p => p.Count).ThenByDescending(p => p.Rule.Keyword.Length).Take(32)
            .Select(p => new RuleSuggestion(p.Rule with { Source = "Local", SongCount = p.Count,
                Strength = Math.Min(1.4, .85 + p.Count * .1), Seconds = 6 }, $"在 {p.Count} 首已听歌曲中出现；未做 AI 情绪判断，默认 6 秒"))
            .ToArray();
    }
    public static RuleSuggestion[] ParseAi(string content, HeardSong[] songs, string[]? suppressed = null)
    {
        content = System.Text.RegularExpressions.Regex.Replace(content, @"<think>.*?</think>", "", System.Text.RegularExpressions.RegexOptions.Singleline).Trim();
        if (content.StartsWith("```")) { int first = content.IndexOf('\n'), last = content.LastIndexOf("```"); if (first >= 0 && last > first) content = content[(first + 1)..last].Trim(); }
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("rules", out var rules)) root = rules;
        if (root.ValueKind != JsonValueKind.Array) throw new FormatException("模型没有返回规则数组。");
        var blocked = new HashSet<string>(suppressed ?? [], StringComparer.OrdinalIgnoreCase);
        var result = new List<RuleSuggestion>();
        foreach (var item in root.EnumerateArray().Take(32))
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("keyword", out var k) || k.ValueKind != JsonValueKind.String || !item.TryGetProperty("effect", out var e) || e.ValueKind != JsonValueKind.String) continue;
            string keyword = k.GetString()!.Trim(), effect = e.GetString()!;
            if (keyword.Length is < 2 or > 12 || blocked.Contains(keyword) || SongCount(keyword, songs) == 0) continue;
            var effects = Enum.GetValues<LyricEffect>();
            int index = Array.FindIndex(effects, v => string.Equals(v.ToString(), effect, StringComparison.OrdinalIgnoreCase) || MusicPlayground.EffectName(v) == effect);
            if (index < 0) continue;
            if (!item.TryGetProperty("intensity", out var n) || n.ValueKind != JsonValueKind.Number || !n.TryGetDouble(out var intensity)
                || !double.IsFinite(intensity) || intensity is < 0 or > 1) continue;
            double seconds = EmotionSeconds(intensity), strength = Math.Round(.5 + intensity, 2);
            string reason = item.TryGetProperty("reason", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString()! : "模型根据已听歌词建议";
            result.Add(new(new(keyword, effects[index], seconds, strength, "AI", SongCount(keyword, songs), intensity),
                $"{EmotionName(intensity)} {intensity:P0} → {seconds:0.#} 秒；{reason[..Math.Min(reason.Length, 60)]}"));
        }
        return result.DistinctBy(r => r.Rule.Keyword, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static LyricRule[] Merge(LyricRule[] existing, IEnumerable<RuleSuggestion> suggestions)
    {
        var result = existing.DistinctBy(r => r.Keyword, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var proposal in suggestions)
        {
            var rule = proposal.Rule; int index = result.FindIndex(r => r.Keyword.Equals(rule.Keyword, StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && result[index].Source == "Manual") continue;
            if (index >= 0 && result[index].Source == "AI" && rule.Source == "Local")
            { result[index] = result[index] with { SongCount = rule.SongCount }; continue; }
            if (index >= 0) result[index] = rule;
            else if (result.Count < MaximumRules) result.Add(rule);
        }
        return result.OrderBy(r => r.Source == "Manual" ? 0 : r.Source == "Builtin" ? 2 : 1).Take(MaximumRules).ToArray();
    }
    public static string SourceName(string source) => source switch { "Builtin" => "内置", "Local" => "本地学习", "AI" => "AI 学习", _ => "手动" };
    public static double EmotionSeconds(double intensity)
    {
        if (!double.IsFinite(intensity) || intensity is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(intensity));
        // A bounded linear policy makes duration reproducible and monotonic in emotional intensity.
        return Math.Round(2 + 10 * intensity, 1, MidpointRounding.AwayFromZero);
    }
    public static string EmotionName(double intensity) => intensity < .25 ? "轻微" : intensity < .5 ? "中等" : intensity < .8 ? "强烈" : "极强";
    public static string EmotionText(LyricRule rule) => rule.EmotionIntensity is { } intensity && double.IsFinite(intensity) && intensity is >= 0 and <= 1
        ? $"{EmotionName(intensity)} {intensity:P0}" : rule.Source == "Manual" ? "手动设定" : "待 AI 判断";
    public static bool Matches(LyricRule rule, string line)
    {
        int offset = 0;
        while (offset < line.Length)
        {
            int index = line.IndexOf(rule.Keyword, offset, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return false;
            // Preserve literal manual rules; avoid treating "不放弃" as the generic "放弃".
            if (rule.Source == "Manual" || index == 0 || !"不别莫无未".Contains(line[index - 1]) || "不别莫无未".Contains(rule.Keyword[0])) return true;
            offset = index + rule.Keyword.Length;
        }
        return false;
    }
}
