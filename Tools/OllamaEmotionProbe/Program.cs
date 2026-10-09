using SpectrumKlinePlayer;
using System.IO;
using System.Text.Json;
using System.Diagnostics;

var settings = new LyricAiSettings(UseAi:true,Model:args.FirstOrDefault(a=>!a.StartsWith("--")) ?? "qwen2.5:1.5b");
using var cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(4));
var timer=Stopwatch.StartNew();
await OllamaLyricClient.EnsureProjectRuntime(settings,cancellation.Token);
var cases=new[]{
    (Text:"微风轻轻吹过今晚心情平静",Expected:LyricEffect.Calm,Strong:false),
    (Text:"我将勇敢向前追逐梦想永不放弃",Expected:LyricEffect.Attack,Strong:true),
    (Text:"孤独压得我喘不过气眼泪止不住",Expected:LyricEffect.Retreat,Strong:true),
    (Text:"狂风怒吼烈火燃烧我的心",Expected:LyricEffect.Volatile,Strong:true)
};
for(int i=0;i<cases.Length;i++)
{
    timer.Restart();var example=new[]{new HeardSong("测试片段"+(i+1),[cases[i].Text])};
    var proposals=await OllamaLyricClient.Suggest(settings,example,cancellation.Token,raw => File.WriteAllText("Docs/Diagnostics/Emotion_LastRawResponse.json",raw));
    Console.WriteLine($"REAL MODEL {settings.Model} / case {i+1} / {proposals.Length} judgments / {timer.Elapsed.TotalSeconds:F1}s");
    foreach(var p in proposals)Console.WriteLine($"{p.Rule.Keyword}: {p.Rule.Effect} / {LyricRuleLearning.EmotionText(p.Rule)} / {p.Rule.Seconds:0.0}s / {p.Reason}");
    if(!proposals.Any(p=>p.Rule.Effect==cases[i].Expected && (cases[i].Strong?p.Rule.EmotionIntensity>=.5:p.Rule.EmotionIntensity<.5)))
        throw new Exception("Model did not correctly identify the explicit emotion in test case "+(i+1));
    if(proposals.Any(p=>p.Rule.EmotionIntensity is null || p.Rule.Seconds != LyricRuleLearning.EmotionSeconds(p.Rule.EmotionIntensity.Value)))throw new Exception("Duration mapping mismatch");
}
var heard=LyricRuleLearning.Corpus(SongRecordingStore.Load("Output-Major/Data/SongRecordings"));
if(heard.Length>0)
{
    timer.Restart();var real=await OllamaLyricClient.Suggest(settings,heard,cancellation.Token);
    Console.WriteLine($"REAL HEARD CORPUS: {heard.Length} songs / {real.Length} AI judgments / {timer.Elapsed.TotalSeconds:F1}s");
    foreach(var p in real)Console.WriteLine($"{p.Rule.Keyword}: {p.Rule.Effect} / {LyricRuleLearning.EmotionText(p.Rule)} / {p.Rule.Seconds:0.0}s");
    Directory.CreateDirectory("Docs/Diagnostics");File.WriteAllText("Docs/Diagnostics/Emotion_RealModelSuggestions.json",JsonSerializer.Serialize(real));
    var preview=new Thread(()=>
    {
        ApplicationConfiguration.Initialize(); using var form=new LyricLearningForm(settings,()=>"真实本机模型 · 已听片段情绪判断",()=>real,_=>Task.CompletedTask,_=>{});
        form.Show();Application.DoEvents();using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new(Point.Empty,bitmap.Size));
        bitmap.Save("Docs/UiPreview/Emotion_RealModelLearning.png",System.Drawing.Imaging.ImageFormat.Png);form.Close();
    });preview.SetApartmentState(ApartmentState.STA);preview.Start();preview.Join();
    if(args.Contains("--apply"))
    {
        string path="Output-Major/Settings/playground.json";
        var current=File.Exists(path)?JsonSerializer.Deserialize<PlaygroundSettings>(File.ReadAllText(path))??new():new PlaygroundSettings();
        var merged=LyricRuleLearning.Merge(current.Rules??MusicPlayground.DefaultRules,real);
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(current with {Rules=merged}));File.Move(path+".tmp",path,true);
        Console.WriteLine("Applied valid emotional rules to published profile; manual rules protected.");
    }
}
Console.WriteLine("Actual inference verified; lyric snippets sent only to loopback.");
