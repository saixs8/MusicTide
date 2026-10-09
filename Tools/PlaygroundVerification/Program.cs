using SpectrumKlinePlayer;
using System.Reflection;
using System.IO;
using System.Drawing.Imaging;
using System.Text.Json;

class Verify
{
    sealed class PersonalityRandom(SongPersonality personality) : Random
    {
        public override int Next(int maxValue) => (int)personality;
    }
    static int checks;
    static void Check(bool result, string name) { if (!result) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static string Output => Path.GetFullPath("Docs/UiPreview");
    static void Preview(Form form, string name, Size? size = null)
    {
        if (size is { } s) form.Size = s;
        form.Show(); form.PerformLayout(); Application.DoEvents();
        using var bmp = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bmp, new(Point.Empty, bmp.Size));
        bmp.Save(Path.Combine(Output, name + ".png"), ImageFormat.Png);
    }
    [STAThread] static void Main()
    {
        ApplicationConfiguration.Initialize(); Directory.CreateDirectory(Output);
        var games = new MusicPlayground(new Random(6));
        var none = games.Influence(0, .8f, .1f, .5f);
        Check(none.BiasPerSecond == 0 && none.Volatility == 1, "disabled games preserve normal market influence");
        games.Settings = new(BullBear: true);
        Check(games.Influence(1, .8f, .1f, .5f).BiasPerSecond > 0 && games.Influence(1, .1f, .8f, .5f).BiasPerSecond < 0, "bull bear bass and treble oppose each other");
        Check(games.Influence(1, .5f, .5f, .9f).Volatility < games.Influence(1, .5f, .5f, .1f).Volatility, "voice band stabilizes bull bear volatility");
        games.Settings = new(BlindBox: true); var personalities = new HashSet<SongPersonality>();
        for (int i = 0; i < 80; i++) { games.Reset(); personalities.Add(games.Personality); }
        Check(personalities.Count == Enum.GetValues<SongPersonality>().Length, "blind box includes positive, mixed and negative personalities");
        foreach (var type in new[] { SongPersonality.SlowBear, SongPersonality.Selloff, SongPersonality.BullTrap })
        {
            var risk = new MusicPlayground(new PersonalityRandom(type)) { Settings = new(BlindBox: true) };
            float initial = risk.Influence(120, .5f, .5f, .5f).BiasPerSecond;
            Check(type == SongPersonality.BullTrap ? initial > 0 : initial < 0, type + " actual opening direction");
            double totalBias = 0;
            for (int i = 0; i < 600; i++)
            {
                risk.Update(120 + i * .1, .1, -1, "");
                totalBias += risk.Influence(120 + i * .1, .5f, .5f, .5f).BiasPerSecond * .1;
            }
            Check(totalBias < 0 && risk.Influence(180, .5f, .5f, .5f).BiasPerSecond < 0, type + " negative cumulative drift starting mid-song");
            Check(risk.BoxStatus.Contains(risk.PersonalityName) && risk.Events.Any(e => e.Name.Contains(risk.PersonalityName)), type + " outcome shown in status and history");
            risk.Settings = new();
            Check(risk.Influence(180, .5f, .5f, .5f) == (0f, 1f), type + " disabled has no influence");
            risk.Settings = new(BlindBox: true); risk.Reroll(180);
            Check(risk.BoxStatus.Contains("待揭晓") && (type != SongPersonality.BullTrap || risk.Influence(180, .5f, .5f, .5f).BiasPerSecond > 0), type + " reroll resets phase");
        }
        games.Reset(); Check(games.BoxStatus.Contains("待揭晓"), "blind box initially concealed");
        for (int i = 0; i < 130; i++) games.Update(i * .1, .1, -1, "");
        Check(games.BoxStatus.Contains(games.PersonalityName) && games.Events.Any(e => e.Name.Contains("揭晓")), "blind box reveals after actual played interval");
        int priorEvents = games.Events.Count; games.Reroll(14);
        Check(games.Events.Count > priorEvents && games.BoxStatus.Contains("待揭晓"), "reroll preserves prior event history and conceals new personality");
        Check(games.NextFlipDirection() == -games.NextFlipDirection(), "limit flip alternates direction");
        games.Settings = new(Chorus: true); games.Reset();
        games.Update(10, .1, 1, "陪你走过每一个春天"); games.Update(11, .1, 2, "看着天空慢慢变蓝");
        games.Update(39, .1, 3, "另一个句子"); // seeks clear matching; use continuous samples below
        games.Reset(); games.Update(10, .1, 1, "陪你走过每一个春天");
        for (int i = 11; i < 41; i++) games.Update(i, .1, 2, "看着天空慢慢变蓝");
        games.Update(41, .1, 3, "陪你走过每一个春天");
        Check(games.Events.Any(e => e.Name.Contains("副歌返场")), "repeated later lyric triggers chorus event");
        int events = games.Events.Count; games.Update(42, .1, 3, "陪你走过每一个春天");
        Check(games.Events.Count == events, "same active lyric does not repeatedly trigger chorus");
        games.Update(3, .1, 1, "陪你走过每一个春天");
        Check(games.Influence(3, .5f, .5f, .5f).BiasPerSecond == 0, "backward seek clears chorus influence");
        games.Settings = new(LyricCards: true, Rules: [new("燃烧", LyricEffect.Attack, 4)]); games.Reset();
        games.Update(10, .1, 1, "让梦想燃烧");
        Check(games.Influence(11, .5f, .5f, .5f).BiasPerSecond > 0 && games.Events.Count == 1, "custom keyword card applies attack bias");
        games.Update(11, .1, 1, "让梦想燃烧"); Check(games.Events.Count == 1, "keyword card triggers once per lyric visit");
        Check(games.Influence(15, .5f, .5f, .5f).BiasPerSecond == 0, "keyword influence expires at configured duration");
        games.Settings = new(LyricCards: true, Rules: [new("再见", LyricEffect.Retreat), new("再见", LyricEffect.Attack)]); games.Reset(); games.Update(10, .1, 1, "说再见");
        Check(games.Influence(11, .5f, .5f, .5f).BiasPerSecond < 0, "keyword priority follows first configured match");
        var wallet = new PaperTrading();
        Check(wallet.Buy(100, .5, 12, "买入歌词") && wallet.Shares == 500 && wallet.Cash == 50000, "virtual buy obeys available-cash fraction");
        Check(wallet.Equity(120) == 110000, "open position equity marks to current music price");
        Check(wallet.Sell(120, 24, "卖出歌词") && wallet.Cash == 110000 && wallet.Shares == 0, "virtual sell realizes profit without losing cash");
        Check(wallet.Fills.Count == 2 && wallet.Fills[0].Lyric == "买入歌词", "trade journal retains ordered timestamps and lyrics");
        Check(!wallet.Buy(double.NaN, 1, 0, "") && !wallet.Buy(-1, 1, 0, "") && !wallet.Sell(100, 0, ""), "invalid orders cannot mutate wallet");
        long revision = wallet.Revision; wallet.Reset(); Check(wallet.Cash == 100000 && wallet.Fills.Count == 0 && wallet.Revision > revision, "new round resets virtual balances and revises views");
        var samples = Enumerable.Range(0, 180).Select(i => new SongSample(i * .5, 0, .2f, -.2f,
            (float)Math.Sin(i * .07) * .2f, (float)(.45 + .35 * Math.Sin(i * .05)), i is >= 100 and < 115 ? "涨停 5/5 · 一字板" : "", 0)).ToArray();
        var a = new SongRecording("arena-a", "擂台歌曲 A（演示）", 120, DateTime.Now, true, true, samples, [new(0, "追逐梦想"), new(30, "让梦想燃烧"), new(60, "奔向远方")]);
        var b = a with { Id = "arena-b", Title = "擂台歌曲 B（演示）", Samples = samples.Select(s => s with { Energy = .25f, Event = "" }).ToArray() };
        var scores = ArenaScore.Calculate(a); Check(scores.Boards == 100 && scores.Burst <= 100 && scores.Total > ArenaScore.Calculate(b).Total, "arena applies documented scores and weight ordering");
        Check(ArenaScore.Calculate(a with { Samples = [] }).Total == 0, "arena safely handles empty record");
        using (var arena = new SongArenaForm([a, b])) { Preview(arena, "Playground_Arena"); arena.Close(); }
        wallet.Buy(100, .5, 30, "让梦想燃烧"); wallet.Sell(120, 60, "奔向远方");
        using (var trading = new PaperTradingForm(wallet, () => (a.Title, 120, 60, true), (_, _) => false, wallet.Reset, () => []))
        {
            Preview(trading, "Playground_Trading");
            typeof(PaperTradingForm).GetMethod("ShowTradeHistory", Flags)!.Invoke(trading, new object[] { new[] { new TradeRound(a.Title, DateTime.Now, 110000, wallet.Fills.ToArray()) } });
            Application.DoEvents(); var historyWindow = trading.OwnedForms.Single();
            var grid = historyWindow.Controls.OfType<DataGridView>().Single();
            typeof(DataGridView).GetMethod("OnCellDoubleClick", Flags)!.Invoke(grid, new object[] { new DataGridViewCellEventArgs(0, 0) });
            Application.DoEvents();
            Check(historyWindow.OwnedForms.Single().Controls.OfType<DataGridView>().Single().Rows.Count == 2, "saved trade round opens complete per-order replay from history");
            historyWindow.Close(); trading.Close();
        }
        using (var rules = new LyricRulesForm(MusicPlayground.DefaultRules))
        {
            Preview(rules, "Playground_LyricRules");
            var grid = (DataGridView)typeof(LyricRulesForm).GetField("grid", Flags)!.GetValue(rules)!;
            grid.Rows[0].Cells[0].Value = "梦想"; grid.Rows[0].Cells[2].Value = "8";
            typeof(LyricRulesForm).GetMethod("Save", Flags)!.Invoke(rules, null);
            Check(rules.Rules[0].Keyword == "梦想" && rules.Rules[0].Seconds == 8, "actual keyword editor validates and saves user rows");
            rules.Close();
        }
        using (var form = new Form1())
        {
            FieldInfo F(string n) => typeof(Form1).GetField(n, Flags)!;
            void Set(string n, object v) => F(n).SetValue(form, v);
            object? Call(string n, params object?[] args) => typeof(Form1).GetMethods(Flags).Single(x => x.Name == n && x.DeclaringType == typeof(Form1)).Invoke(form, args);
            Call("OnFormClosing", null, new FormClosingEventArgs(CloseReason.None, false)); Set("closing", false); Set("saveIndicatorSettings", false);
            Call("ResetSongChart", 180d, "七玩法演示（验证数据）");
            var game = (MusicPlayground)F("playground").GetValue(form)!;
            game.Settings = new(true, true, true, true, 12, MusicPlayground.DefaultRules);
            Set("currentLyric", "让梦想燃烧"); Set("activeLyricIndex", 0);
            var menu = (MenuStrip)F("menuBar").GetValue(form)!;
            menu.Items.OfType<ToolStripMenuItem>().Single(i=>i.Text=="音乐玩法").DropDownItems.OfType<ToolStripMenuItem>().Single(i=>i.Text=="菜单字号").DropDownItems.OfType<ToolStripMenuItem>().Single(i=>(int)i.Tag! ==12).PerformClick();
            Check(menu.Font.Size >= 12 && menu.Items.OfType<ToolStripMenuItem>().All(i => i.Font.Size >= 12), "main menu font enlarged to minimum 12 pt");
            Preview(form, "Playground_Main_1180x720", new(1180, 720));
            Check(menu.Items.OfType<ToolStripMenuItem>().All(i => i.Placement == ToolStripItemPlacement.Main), "default enlarged menu fits minimum width without hiding commands");
            var music = menu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "音乐玩法");
            Check(music.DropDown.Font.Size >= 12 && music.DropDownItems.OfType<ToolStripMenuItem>().All(i => i.Padding.Vertical >= 6), "dropdown font selection and compact row padding applied");
            music.ShowDropDown(); Application.DoEvents();
            using (var bmp = new Bitmap(music.DropDown.Width, music.DropDown.Height)) { music.DropDown.DrawToBitmap(bmp, new(Point.Empty, bmp.Size)); bmp.Save(Path.Combine(Output, "Playground_Menu.png")); }
            music.HideDropDown();
            var setting = game.Settings; Call("SavePlaySettings"); game.Settings = new(); Call("LoadPlaygroundData");
            Check(game.Settings.BullBear && game.Settings.BlindBox && game.Settings.Chorus && game.Settings.LyricCards && game.Settings.MenuSize == 12, "all game toggles and menu size reload from saved settings");
            Check(game.Settings.Rules?.Length == MusicPlayground.DefaultRules.Length, "custom keyword rules persist with settings");
            game.Reset(); Set("lyricTimeline", Array.Empty<TimedLyricLine>()); Set("activeLyricIndex", -1);
            var liveLyrics = (List<TimedLyricLine>)F("liveLyricHistory").GetValue(form)!;
            liveLyrics.Clear(); liveLyrics.Add(new(0, "让梦想燃烧")); Set("currentLyric", "让梦想燃烧"); Set("trackIsPlaying", true); Call("ObserveMusicEnergy");
            Check(game.Events.Any(e => e.Name.Contains("歌词事件")), "live captured lyric without full timeline can still trigger keyword card");
            var market = (ClimaxMarket)F("climaxMarket").GetValue(form)!;
            market.Reset(); market.Settings = new(TriggerProbability: 1, OneWordProbability: 1, MinimumBoards: 5, MaximumBoards: 5, BreakProbabilityPerSecond: 0);
            for (int i = 0; i < 100; i++) market.Observe(.8, .03);
            var ct = ((Array)F("timelineCandles").GetValue(form)!).GetType().GetElementType()!;
            object Candle(double close) { var c = Activator.CreateInstance(ct)!; foreach (string name in new[] { "Open", "Close", "High", "Low" }) ct.GetField(name)!.SetValue(c, (float)close); return c; }
            double price = 0;
            for (int i = 0; i < 5; i++) { var board = market.BeginBar(price)!; price = board.Limit; Call("ObserveCompletedBoard", Candle(price)); }
            var unlocked = (List<MusicAchievement>)F("achievements").GetValue(form)!;
            Check(unlocked.Any(x => x.Song == "七玩法演示（验证数据）" && x.Name == "五连板"), "five actually sealed consecutive boards earn persistent achievement");
            Set("sealedBoards", 0); Set("priorSealedDirection", 0);
            // Inject historical achievement fixtures; production queue is once per song.
            market.Reset(); market.QueueSingleBoard(-1); var down = market.BeginBar(0)!; Call("ObserveCompletedBoard", Candle(down.Limit));
            market.Reset(); market.QueueSingleBoard(1); var up = market.BeginBar(down.Limit)!; Call("ObserveCompletedBoard", Candle(up.Limit));
            Check(unlocked.Any(x => x.Name == "地天反转（两板）" && x.Song == "七玩法演示（验证数据）"), "alternating sealed down/up boards earn explicitly named two-board reversal");
            Call("OpenAchievements"); Application.DoEvents();
            var historyWindow = form.OwnedForms.Single(f => f.Text.Contains("连板成就")); Preview(historyWindow, "Playground_Achievements"); historyWindow.Close();
            var paper = (PaperTrading)F("paperTrading").GetValue(form)!;
            paper.Buy(100, .5, 1, "验证买入"); Set("timelineClose", (float)Math.Log(1.2));
            Call("ResetSongChart", 180d, "下一曲（验证数据）");
            var rounds = (List<TradeRound>)F("tradeRounds").GetValue(form)!;
            Check(rounds.Any(r => r.Song == "七玩法演示（验证数据）" && Math.Abs(r.FinalEquity - 110000) < .1) && paper.Cash == 100000, "song switch settles virtual position at final price and opens fresh round");
            var record = (SongRecording)Call("CurrentRecording")!;
            Check(record.Personality.Length > 0 && record.Gameplay is not null, "song snapshots include blind personality and gameplay events");
            // Exercise the actual font option, then restore test preference.
            var fontMenu = music.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == "菜单字号");
            Check(fontMenu.DropDownItems.Count==9,"font menu offers nine sizes from ten through twenty-four");
            foreach(int size in MusicPlayground.MenuSizes)
            {
                fontMenu.DropDownItems.OfType<ToolStripMenuItem>().Single(i => (int)i.Tag! == size).PerformClick();
                Check(menu.Font.Size==size,$"menu font size {size} applied");
            }
            Preview(form,"Playground_Menu24_1180x720",new(1180,720));
            Console.WriteLine($"MENU24 layout={menu.LayoutStyle} width={menu.Width} overflow={menu.OverflowButton.Visible} bounds={menu.OverflowButton.Bounds}");
            foreach(var item in menu.Items.OfType<ToolStripMenuItem>())Console.WriteLine($"MENU24 {item.Text}: {item.Placement} {item.Bounds}");
            Check(menu.Items.OfType<ToolStripMenuItem>().All(i => i.Placement == ToolStripItemPlacement.Main && i.Bounds.Right <= menu.Width || i.Placement == ToolStripItemPlacement.Overflow && menu.OverflowButton.Visible),"twenty-four point menu keeps every command reachable at minimum width");
            fontMenu.DropDownItems.OfType<ToolStripMenuItem>().Single(i => (int)i.Tag! == 16).PerformClick(); Application.DoEvents();
            Check(menu.Font.Size == 16 && menu.CanOverflow, "super-large menu mode remains accessible through overflow");
            Check(menu.Items.OfType<ToolStripMenuItem>().All(i => i.Placement == ToolStripItemPlacement.Main && i.Bounds.Right <= menu.Width || i.Placement == ToolStripItemPlacement.Overflow && menu.OverflowButton.Visible), "super-large mode places every command visibly or in available overflow");
            Preview(form, "Playground_Menu16_1180x720", new(1180, 720));
            fontMenu.DropDownItems.OfType<ToolStripMenuItem>().Single(i => (int)i.Tag! == 12).PerformClick();
            form.Close();
        }
        var reseal = new ClimaxMarket(new Random(5)) { AllowReseal = true, Settings = new(TriggerProbability: 1, OneWordProbability: 1, BreakProbabilityPerSecond: .1, ExtremeSeconds: 3) };
        for (int i = 0; i < 100; i++) reseal.Observe(.8, .03); reseal.BeginBar(0);
        for (int i = 0; i < 3000; i++) reseal.Observe(.1, .03);
        Check(reseal.Broken, "prolonged low energy breaks board before reseal");
        for (int i = 0; i < 6000; i++) reseal.Observe(.8, .03);
        Check(reseal.Resealed && !reseal.Broken && reseal.Status.Contains("回封"), "energy recovery can reseal same board and exposes explicit event");
        Console.WriteLine($"Completed {checks} playground checks.");
    }
}
