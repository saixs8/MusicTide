using System.IO;
using SpectrumKlinePlayer;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Drawing.Imaging;

class Verify
{
    static int checks;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    static SongRecording Record(string id, string title, double[] positions, bool simulated = false) => new(id, title, 30, DateTime.Now, simulated, true,
        positions.Select(p => new SongSample(p, 1, 1, 1, 1, .5f, "", 0)).ToArray(),
        [new(0, "作词：测试"), new(5, "青春勇敢不放弃"), new(10, "温柔心跳"), new(20, "未来没有听到")]);
    [STAThread] static void Main()
    {
        ApplicationConfiguration.Initialize();
        var songs = LyricRuleLearning.Corpus([Record("1", "歌曲甲", [6, 7, 11]), Record("2", "歌曲甲", [6]), Record("3", "模拟乙", [21], true)]);
        Check(MusicPlayground.DefaultRules.Length == 5, "five default emotional keywords");
        Check(LyricRuleLearning.LearningVocabulary.Length==67,"extended vocabulary remains available only for heard-song learning");
        var compact=LyricRuleLearning.CompactDefaults(LyricRuleLearning.LearningVocabulary.Concat([new LyricRule("自定义",LyricEffect.Attack),new LyricRule("学习词",LyricEffect.Calm,Source:"AI",EmotionIntensity:.3)]));
        Check(compact.Count(r=>r.Source=="Builtin")==5&&compact.Any(r=>r.Keyword=="自定义")&&compact.Any(r=>r.Keyword=="学习词"),"legacy defaults reduce to five while manual and learned rules survive");
        Check(songs.Length == 1 && songs[0].Lines.Length == 2, "heard positions deduplicate songs and exclude simulated records");
        Check(!songs[0].Lines.Any(l => l.Contains("没有听到")), "future cached lyrics are not learned");
        Check(LyricRuleLearning.Corpus([Record("4", "片头", [1])]).Length == 0, "credit-only sample excluded");
        var proposals = LyricRuleLearning.Local(songs);
        Check(proposals.Any(p => p.Rule.Keyword == "青春" && p.Rule.Source == "Local"), "actually heard new phrase added locally");
        Check(!proposals.Any(p => p.Rule.Keyword == "放弃"), "negated phrase does not learn contradictory generic rule");
        Check(!LyricRuleLearning.Local(songs, ["青春"]).Any(p => p.Rule.Keyword == "青春"), "deleted keyword stays suppressed");
        var manual = new LyricRule("勇敢", LyricEffect.Calm, 9, 1.8);
        var merged = LyricRuleLearning.Merge([manual], proposals);
        Check(merged.Single(r => r.Keyword == "勇敢") == manual, "manual mapping and strength protected");
        Check(merged[0] == manual, "manual rules keep first priority");
        var both = songs.Concat([new HeardSong("乙", ["勇敢青春"]) ]).ToArray();
        Check(LyricRuleLearning.Local(both).Single(p => p.Rule.Keyword == "青春").Rule.Seconds == proposals.Single(p => p.Rule.Keyword == "青春").Rule.Seconds, "heard-song frequency no longer determines duration");
        string json = """
            ```json
            {"rules":[{"keyword":"青春","effect":"Attack","intensity":0.8,"seconds":90,"strength":9,"reason":"积极强烈"},{"keyword":"杜撰词","effect":"Calm","intensity":0.5},{"keyword":"温柔","effect":"错误","intensity":0.2},{"keyword":"心跳","effect":"Volatile","intensity":0.2,"seconds":"坏值"}]}
            ```
            """;
        var ai = LyricRuleLearning.ParseAi(json, songs);
        Check(ai.Length == 2, "AI fences parsed and invented or invalid rules rejected");
        Check(ai[0].Rule.Seconds == 10 && ai[0].Rule.Strength == 1.3 && ai[0].Rule.EmotionIntensity == .8, "AI emotion determines seconds and strength; arbitrary values ignored");
        Check(ai[1].Rule.Seconds == 4, "mild emotion generates shorter duration");
        Check(!LyricRuleLearning.ParseAi(json, songs, ["青春"]).Any(p => p.Rule.Keyword == "青春"), "AI respects deletions");
        Check(LyricRuleLearning.EmotionSeconds(0)==2 && LyricRuleLearning.EmotionSeconds(1)==12, "automatic duration has reasonable minimum and maximum");
        Check(Enumerable.Range(0,101).Select(i=>LyricRuleLearning.EmotionSeconds(i/100d)).SequenceEqual(Enumerable.Range(0,101).Select(i=>LyricRuleLearning.EmotionSeconds(i/100d)).Order()), "stronger emotion never produces shorter duration");
        foreach(var value in new[]{"-0.1", "1.1", "null", "\"80%\""})
            Check(LyricRuleLearning.ParseAi("{\"rules\":[{\"keyword\":\"青春\",\"effect\":\"Attack\",\"intensity\":"+value+"}]}",songs).Length==0,"invalid emotion scores are rejected");
        Check(LyricRuleLearning.ParseAi("{\"rules\":[{\"keyword\":\"青春\",\"effect\":\"Attack\",\"seconds\":9}]}",songs).Length==0,"missing emotion does not fabricate AI duration");
        var remembered=LyricRuleLearning.Merge([ai[0].Rule],LyricRuleLearning.Local(songs));
        Check(remembered.Single(r=>r.Keyword=="青春").Seconds==10 && remembered.Single(r=>r.Keyword=="青春").EmotionIntensity==.8,"local fallback retains previously judged AI duration");
        var stronger=LyricRuleLearning.ParseAi("{\"rules\":[{\"keyword\":\"青春\",\"effect\":\"Attack\",\"intensity\":1}]}",songs);
        Check(LyricRuleLearning.Merge([ai[0].Rule],stronger)[0].Seconds==12,"new AI context can update earlier emotional duration");
        Check(ai[0].Reason.Contains("10 秒")&&LyricRuleLearning.EmotionText(ai[0].Rule).Contains("极强"),"emotion and duration evidence is visible to user");
        var filled = Enumerable.Range(0, 200).Select(i => new LyricRule("手动" + i, LyricEffect.Calm)).ToArray();
        Check(LyricRuleLearning.Merge(filled, proposals).Length == 200, "rule library capped at 200");
        Check(LyricRuleLearning.Enrich([manual]).Single(r => r.Keyword == "勇敢") == manual, "library migration preserves custom rules");
        var longRecord = Record("long", "长歌", Enumerable.Range(0, 15).Select(i => (double)i + .2).ToArray()) with { Lyrics = Enumerable.Range(0, 15).Select(i => new TimedLyricLine(i, "已听片段" + i)).ToArray() };
        var moving = LyricRuleLearning.Corpus([longRecord]);
        Check(moving[0].Lines.Length == 12 && moving[0].Lines.Contains("已听片段14"), "learning window continues advancing past first twelve lines");
        Check(LyricRuleLearning.Fingerprint(songs,new()) != LyricRuleLearning.Fingerprint(songs,new(AutoApply:false)), "apply preference changes invalidate learning cache");
        Check(OllamaLyricClient.LocalEndpoint("http://localhost:11434/v1").AbsoluteUri == "http://localhost:11434/", "local API address normalized");
        foreach (string address in new[] { "https://example.com", "http://192.168.1.5:11434", "http://localhost:11434/api/chat", "http://localhost:11434/?redirect=x" })
        { bool rejected = false; try { OllamaLyricClient.LocalEndpoint(address); } catch (ArgumentException) { rejected = true; } Check(rejected, "reject nonlocal or ambiguous endpoint"); }
        var game = new MusicPlayground(); game.Settings = new(LyricCards:true,Rules:LyricRuleLearning.Local([new HeardSong("已听歌曲",["青春勇敢不放弃"])]).Select(p=>p.Rule).ToArray()); game.Update(6, .1, 1, "青春勇敢不放弃");
        Check(game.Influence(7,0,0,0).BiasPerSecond > 0, "negated despair phrase triggers attack");
        game.Settings = new(LyricCards:true, Rules:[new("勇敢",LyricEffect.Attack,6,1.5)]); game.Reset(); game.Update(6,.1,1,"勇敢");
        Check(Math.Abs(game.Influence(7,0,0,0).BiasPerSecond - .033f)<.00001, "keyword strength affects market");
        game.Settings = new(LyricCards:true, Rules:[ai[0].Rule]); game.Reset(); game.Update(6,.1,1,"青春");
        Check(game.CurrentNotification(15).Contains("10秒") && game.Influence(15,0,0,0).BiasPerSecond>0, "description and market effect use AI generated duration");
        Check(game.CurrentNotification(16.1)=="" && game.Influence(16.1,0,0,0).BiasPerSecond==0, "description and effect expire together");
        Api(songs, false); Api(songs, true);
        using (var owner = new Form1())
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo F(string name) => typeof(Form1).GetField(name, flags)!;
            typeof(Form1).GetMethods(flags).Single(m=>m.Name=="OnFormClosing" && m.DeclaringType==typeof(Form1)).Invoke(owner, [null, new FormClosingEventArgs(CloseReason.None, false)]);
            F("closing").SetValue(owner, false);
            string records = (string)typeof(Form1).GetProperty("RecordingsDirectory", flags)!.GetValue(owner)!;
            SongRecordingStore.Save(records, Record("learning-fixture", "学习流程验证", [6, 11]));
            F("lyricAiSettings").SetValue(owner, new LyricAiSettings(Automatic:false, AutoApply:true));
            var playground=(MusicPlayground)F("playground").GetValue(owner)!;
            playground.Settings = new(Rules:[manual], KeywordLibraryVersion:1);
            void Learn()
            {
                var task=(Task)typeof(Form1).GetMethod("LearnLyrics",flags)!.Invoke(owner,[true])!;
                var deadline=DateTime.UtcNow.AddSeconds(5);
                while(!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
                Check(task.IsCompleted, "actual form learning completes in background"); task.GetAwaiter().GetResult();
            }
            Learn();
            Check(playground.Settings.Rules!.Any(r=>r.Keyword=="青春"&&r.Source=="Local") && playground.Settings.Rules!.Single(r=>r.Keyword=="勇敢")==manual, "actual form merges learned rules and preserves manual rules");
            F("lyricAiSettings").SetValue(owner, new LyricAiSettings(Automatic:false, AutoApply:false)); playground.Settings = new(Rules:[manual], KeywordLibraryVersion:1);
            Learn();
            var state=(LyricLearningState)F("lyricLearning").GetValue(owner)!;
            Check(playground.Settings.Rules!.Length==1 && state.Suggestions!.Length>0, "review mode stores suggestions without applying");
        }
        using (var editor = new LyricRulesForm(MusicPlayground.DefaultRules))
        {
            var flags = BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(LyricRulesForm).GetMethod("Save",flags)!.Invoke(editor,null);
            Check(editor.Rules[0].Source == "Builtin", "unchanged editor rows preserve provenance");
            var grid=(DataGridView)typeof(LyricRulesForm).GetField("grid",flags)!.GetValue(editor)!;
            grid.Rows[0].Cells[3].Value=1.6;
            typeof(LyricRulesForm).GetMethod("Save",flags)!.Invoke(editor,null);
            Check(editor.Rules[0].Source == "Manual" && editor.Rules[0].Strength==1.6, "user edit becomes protected manual rule");
        }
        using (var form = new LyricLearningForm(new(),()=>"验证：本机学习设置",()=>proposals,_=>Task.CompletedTask,_=>{}))
        {
            form.Show(); Application.DoEvents(); using var bmp=new Bitmap(form.Width,form.Height); form.DrawToBitmap(bmp,new(Point.Empty,bmp.Size));
            Directory.CreateDirectory("Docs/UiPreview"); bmp.Save("Docs/UiPreview/LyricLearning.png",ImageFormat.Png);
            Check(form.Settings.Endpoint.Contains("127.0.0.1") && !form.Settings.UseAi, "local model settings open with explicit AI toggle");
        }
        var history = SongRecordingStore.Load("Output-Major/Data/SongRecordings");
        var heard=LyricRuleLearning.Corpus(history); var learned=LyricRuleLearning.Local(heard);
        Console.WriteLine($"REAL HISTORY: {history.Length} records / {heard.Length} heard songs / {learned.Length} local proposals (not model inference)");
        Console.WriteLine($"Passed {checks} checks");
    }
    static void Api(HeardSong[] songs, bool cloud)
    {
        var reservation=new TcpListener(IPAddress.Loopback,0); reservation.Start(); int port=((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        using var server=new HttpListener(); server.Prefixes.Add($"http://127.0.0.1:{port}/"); server.Start(); int chat=0;
        var serving=Task.Run(async()=>
        {
            for(int i=0;i<(cloud?1:2);i++)
            {
                var c=await server.GetContextAsync(); string body=await new StreamReader(c.Request.InputStream).ReadToEndAsync();
                string reply;
                if(c.Request.Url!.AbsolutePath=="/api/show") { Check(!body.Contains("青春"),"model inspection carries no lyric data"); reply=cloud?"{\"remote_host\":\"https://ollama.com\"}":"{}"; }
                else { chat++; using var request=JsonDocument.Parse(body); Check(request.RootElement.GetProperty("format").GetProperty("properties").GetProperty("rules").GetProperty("items").GetProperty("required").EnumerateArray().Any(p=>p.GetString()=="intensity"),"Ollama request enforces emotion score schema"); Check(request.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!.Contains("青春"),"model receives readable Chinese context rather than nested Unicode escapes"); reply=JsonSerializer.Serialize(new{message=new{content="{\"rules\":[{\"keyword\":\"青春\",\"effect\":\"Attack\",\"intensity\":0.7}]}"}}); }
                var bytes=Encoding.UTF8.GetBytes(reply); c.Response.ContentType="application/json"; c.Response.ContentLength64=bytes.Length; await c.Response.OutputStream.WriteAsync(bytes); c.Response.Close();
            }
        });
        bool rejected=false; RuleSuggestion[] result=[];
        try { result=OllamaLyricClient.Suggest(new(UseAi:true,Endpoint:$"http://127.0.0.1:{port}",Model:"local-test"),songs,default).GetAwaiter().GetResult(); }
        catch(InvalidOperationException){rejected=true;}
        serving.GetAwaiter().GetResult();
        Check(cloud?rejected&&chat==0:result.Length==1&&chat==1,cloud?"cloud-backed model rejected before lyrics sent":"local Ollama protocol request and parse verified");
    }
}
