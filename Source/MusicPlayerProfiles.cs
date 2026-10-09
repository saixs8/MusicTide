using System.Text.RegularExpressions;

namespace SpectrumKlinePlayer;

public sealed record MusicPlayerProfile(string Id, string Name, string[] Aliases);

public static class MusicPlayerProfiles
{
    public static readonly MusicPlayerProfile[] All =
    [
        new("kugou", "酷狗音乐", ["kugou", "kgmusic", "酷狗"]),
        new("qq", "QQ音乐", ["qqmusic", "qq音乐"]),
        new("netease", "网易云音乐", ["cloudmusic", "netease", "网易云"]),
        new("soda", "汽水音乐", ["sodamusic", "soda", "qishui", "luna", "汽水"]),
        new("kuwo", "酷我音乐", ["kuwo", "酷我"]),
        new("spotify", "Spotify", ["spotify"])
    ];

    public static MusicPlayerProfile? Find(string? identity) => string.IsNullOrWhiteSpace(identity) ? null
        : All.FirstOrDefault(p => p.Aliases.Any(a => identity.Contains(a, StringComparison.OrdinalIgnoreCase)));
    public static string Key(string? text) => Regex.Replace((text ?? "").ToLowerInvariant(), @"[^\p{L}\p{Nd}]", "");
    public static bool IsGeneric(string text) => All.Any(p => Key(p.Name) == Key(text))
        || text.Trim() is "歌词" or "桌面歌词" or "Soda Music" or "CloudMusic" or "QQMusic" or "发现音乐" or "我的音乐" or "音乐馆" or "首页" or "正在播放";
    public static bool SameTrack(string? a, string? b)
    {
        if (Key(a) == Key(b)) return true;
        var left = Regex.Split(a ?? "", @"\s+[-–—]\s+");
        var right = Regex.Split(b ?? "", @"\s+[-–—]\s+");
        return left.Length == 2 && right.Length == 2 && Key(left[0]) == Key(right[1]) && Key(left[1]) == Key(right[0]);
    }

    public static string? CleanWindowTitle(string text)
    {
        text = Regex.Replace(text, @"\s*[-–—|]\s*(?:QQ音乐|网易云音乐|网易云|汽水音乐|Soda Music|酷狗音乐|酷我音乐|Spotify)\s*$", "", RegexOptions.IgnoreCase).Trim();
        text = Regex.Replace(text, @"^正在播放\s*[:：]\s*", "").Trim();
        return text.Length is >= 2 and <= 160 && !IsGeneric(text)
            && !text.Contains("桌面歌词") && !text.Contains("歌词秀") ? text : null;
    }
}
