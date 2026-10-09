using System.IO;
using System.Text.Json;

namespace SpectrumKlinePlayer;

public sealed partial class Form1
{
    private LyricAiSettings lyricAiSettings = new();
    private LyricLearningState lyricLearning = new();
    private bool lyricLearningBusy, lyricLearningReady;
    private DateTime nextLyricLearning;
    private readonly CancellationTokenSource learningCancellation = new();
    private string LyricAiSettingsPath => Path.Combine(AppContext.BaseDirectory, "Settings", "lyric-ai.json");
    private string LyricLearningPath => Path.Combine(PlayDataDirectory, "lyric-learning.json");
    private void LoadLyricLearning()
    {
        try { if (File.Exists(LyricAiSettingsPath)) lyricAiSettings = JsonSerializer.Deserialize<LyricAiSettings>(File.ReadAllText(LyricAiSettingsPath)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        try { if (File.Exists(LyricLearningPath)) lyricLearning = JsonSerializer.Deserialize<LyricLearningState>(File.ReadAllText(LyricLearningPath)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        lyricLearningReady = true;
        Shown += async (_, _) => await LearnLyrics(false);
        FormClosing += (_, _) => learningCancellation.Cancel();
    }
    private void SaveLyricLearning()
    { SavePlayJson(LyricAiSettingsPath, lyricAiSettings); SavePlayJson(LyricLearningPath, lyricLearning); }
    private async Task LearnLyrics(bool force)
    {
        if (!lyricLearningReady || closing || lyricLearningBusy || (!force && (!lyricAiSettings.Automatic || DateTime.UtcNow < nextLyricLearning))) return;
        lyricLearningBusy = true; nextLyricLearning = DateTime.UtcNow.AddSeconds(60);
        var settings = lyricAiSettings;
        try
        {
            var songs = await Task.Run(() => LyricRuleLearning.Corpus(SongRecordingStore.Load(RecordingsDirectory)));
            if (closing) return;
            var fingerprint = LyricRuleLearning.Fingerprint(songs, settings);
            if (!force && fingerprint == lyricLearning.Fingerprint) return;
            var suggestions = LyricRuleLearning.Local(songs, settings.Suppressed);
            string status = OptionalAiInstall.Pending ? "离线词库学习完成；本地 AI 模型待补装" : "本地词库学习完成";
            bool success = !OptionalAiInstall.Pending;
            if (settings.UseAi && songs.Length > 0 && !OptionalAiInstall.Pending)
            {
                try
                {
                    await OllamaLyricClient.EnsureProjectRuntime(settings, learningCancellation.Token);
                    var ai = await OllamaLyricClient.Suggest(settings, songs, learningCancellation.Token);
                    suggestions = ai.Concat(suggestions).DistinctBy(s => s.Rule.Keyword, StringComparer.OrdinalIgnoreCase).ToArray();
                    status = ai.Length > 0 ? $"Ollama 情绪判断 {ai.Length} 条，自动生成 2–12 秒；共 {suggestions.Length} 条建议"
                        : "Ollama 未返回有效情绪程度；未生成新的 AI 时长，保留已有判断";
                    if (ai.Length == 0) success = false;
                }
                catch (Exception e) when (e is System.Net.Http.HttpRequestException or OperationCanceledException or JsonException or FormatException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                { status = "AI 情绪判断暂不可用；保留已有 AI 时长，新词默认 6 秒：" + e.Message; success = false; }
            }
            if (closing) return;
            // Apply using current settings: a user edit during inference remains authoritative.
            if (settings != lyricAiSettings) return;
            suggestions = suggestions.Where(s => !(lyricAiSettings.Suppressed ?? []).Contains(s.Rule.Keyword, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (settings.AutoApply)
            { playground.Settings = playground.Settings with { Rules = LyricRuleLearning.Merge(playground.Settings.Rules ?? MusicPlayground.DefaultRules, suggestions) }; SavePlaySettings(); }
            lyricLearning = new(success ? fingerprint : "", DateTime.Now, songs.Length, status, suggestions);
            SaveLyricLearning();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        { if (!closing) { lyricLearning = lyricLearning with { Status = "听歌记录读取失败：" + e.Message }; SaveLyricLearning(); } }
        finally { lyricLearningBusy = false; }
    }
    private void OpenLyricLearning()
    {
        using var dialog = new LyricLearningForm(lyricAiSettings,
            () => lyricLearningBusy ? "正在后台学习…" : $"已听歌曲：{lyricLearning.HeardSongs}；最近学习：{lyricLearning.UpdatedAt:MM-dd HH:mm}\n{lyricLearning.Status}",
            () => lyricLearning.Suggestions ?? [],
            async settings =>
            {
                OllamaLyricClient.LocalEndpoint(settings.Endpoint); lyricAiSettings = settings; SaveLyricLearning(); SaveCurrentRecording();
                while (lyricLearningBusy && !closing) await Task.Delay(100);
                await LearnLyrics(true);
            },
            selected => { playground.Settings = playground.Settings with { Rules = LyricRuleLearning.Merge(playground.Settings.Rules ?? MusicPlayground.DefaultRules, selected) }; SavePlaySettings(); });
        if (dialog.ShowDialog(this) == DialogResult.OK) { lyricAiSettings = dialog.Settings; SaveLyricLearning(); }
    }
}
