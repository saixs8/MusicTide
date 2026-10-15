using SpectrumKlinePlayer;
using System.IO;
using System.Reflection;
using System.Text.Json;

class Program
{
    static int checks;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
    static bool Near(double a, double b) => Math.Abs(a - b) < .00001;
    [STAThread] static void Main()
    {
        var account = new PaperTrading();
        Check(account.Buy(100,.5,1,"long") && account.Shares==500 && account.Cash==50000,"legacy 1x long collateral and quantity");
        Check(account.Sell(120,2,"") && Near(account.Cash,110000) && account.RealizedProfit==10000,"legacy long realized profit");
        account.Reset(); Check(account.SetLeverage(5)&&account.Short(100,.5,1,"short"),"5x short entry");
        Check(account.Shares==-2500&&account.Cash==50000&&account.Margin==50000&&account.Equity(100)==100000,"short proceeds cannot create spendable cash");
        Check(account.UnrealizedProfit(90)==25000&&account.Equity(90)==125000,"falling price profits short");
        Check(!account.SetLeverage(10)&&!account.Buy(100,.25,2,""),"open position locks leverage and blocks opposite entry");
        Check(account.Close(90,.5,2,"")&&account.Shares==-1250&&account.Margin==25000&&account.RealizedProfit==12500,"partial short close releases proportional collateral");
        Check(Near(account.Equity(90),PaperTrading.StartingCash+account.RealizedProfit+account.UnrealizedProfit(90)),"equity conservation after partial close");
        Check(account.Sell(110,3,"")&&Near(account.Cash,100000)&&account.Margin==0&&account.RealizedProfit==0,"short remaining loss offsets earlier realized gain");
        account.Reset();account.SetLeverage(10);account.Buy(100,1,1,"");
        Check(!account.CheckLiquidation(93,2,"")&&account.Shares!=0,"above maintenance keeps position");
        Check(account.CheckLiquidation(92,3,"")&&account.Shares==0&&Near(account.Cash,20000)&&account.Fills[^1].Side=="强制平仓","long liquidation at 20 percent maintenance");
        Check(!account.CheckLiquidation(91,4,"")&&account.Fills.Count==2,"liquidation is not duplicated");
        account.Reset();account.Short(100,1,1,"");
        Check(account.CheckLiquidation(108,2,"")&&Near(account.Cash,20000),"short adverse move liquidates with retained leverage");
        account.Reset();account.Buy(100,1,1,"");account.CheckLiquidation(80,2,"");
        Check(account.Cash<0&&!account.Buy(80,1,3,""),"gap deficit is recorded and cannot reopen without funds");
        account.Reset();
        foreach(double value in new[]{double.NaN,double.PositiveInfinity,-1,0}) Check(!account.Buy(value,1,0,"")&&!account.Short(value,1,0,""),"invalid price rejected "+value);
        Check(!account.Buy(100,double.NaN,0,"")&&!account.Short(100,double.PositiveInfinity,0,""),"invalid fraction rejected");
        account.SetLeverage(3);account.Buy(100,.25,1,"");account.Buy(110,.5,2,"");
        Check(Near(account.Equity(110),100000+account.UnrealizedProfit(110)),"weighted entry and account conservation");
        account.Sell(105,3,"");
        Check(Near(account.Cash,100000+account.RealizedProfit),"all collateral released on full close");
        var legacy=JsonSerializer.Deserialize<TradeRound>("{\"Song\":\"old\",\"FinalEquity\":110000,\"Fills\":[{\"Seconds\":1,\"Side\":\"卖出\",\"Quantity\":1,\"Price\":100,\"Equity\":110000,\"Lyric\":\"\"}]}")!;
        Check(legacy.Fills[0].Leverage==1&&legacy.Fills[0].RealizedProfit==null,"legacy journal has unknown per-fill PnL, not invented zero");
        Check(JsonSerializer.Deserialize<TradeFill>(JsonSerializer.Serialize(account.Fills[^1]))!.RealizedProfit==account.Fills[^1].RealizedProfit,"new journal preserves realized profit");
        VerifyWorkbar();
        Console.WriteLine($"All {checks} trading checks passed");
    }
    static void VerifyWorkbar()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var form=new Form1();
        object Get(string n)=>typeof(Form1).GetField(n,flags)!.GetValue(form)!;
        void Set(string n,object v)=>typeof(Form1).GetField(n,flags)!.SetValue(form,v);
        object Call(string n,params object[] a)=>typeof(Form1).GetMethod(n,flags)!.Invoke(form,a)!;
        ((System.Windows.Forms.Timer)Get("frameTimer")).Stop();
        ((CancellationTokenSource)Get("lyricCaptureCts")).Cancel();
        ((AudioLoopbackCapture)Get("capture"))?.Dispose();Set("capture",null!);
        Set("currentTrackTitle","交易回归演示");Set("trackIsPlaying",true);Set("hasAudioSignal",true);Set("timelineCandleCount",1);Set("timelineClose",0f);Set("songOpeningPrice",100d);
        form.Show();Application.DoEvents();
        var stage=(Control)Get("stage");var wallet=(PaperTrading)Get("paperTrading");
        void Paint(){ using var bitmap=new Bitmap(stage.Width,stage.Height);stage.DrawToBitmap(bitmap,stage.ClientRectangle); }
        Paint();
        var hits=(System.Collections.IList)Get("tradingHits");
        Check(hits.Count==8,"main workbar contains eight inline actions");
        Check(form.OwnedForms.Length==0,"orders do not require a popup");
        wallet.SetLeverage(5);Set("tradeFraction",.5);
        var hit=hits.Cast<object>().Single(x=>(string)x.GetType().GetField("Item2")!.GetValue(x)! == "short");
        var rect=(Rectangle)hit.GetType().GetField("Item1")!.GetValue(hit)!;
        Call("OnTerminalMouseDown",null!,new MouseEventArgs(MouseButtons.Left,1,rect.Left+5,rect.Top+5,0));
        Check(wallet.Shares==-2500&&form.OwnedForms.Length==0,"clicking inline short opens leveraged position without popup");
        Set("hasAudioSignal",false);Check(!(bool)Call("SubmitTrade","long"),"silent or paused playback cannot trade");Set("hasAudioSignal",true);
        Call("OpenPaperTrading");Application.DoEvents();var details=form.OwnedForms.OfType<PaperTradingForm>().Single();
        Check(details.Controls.OfType<FlowLayoutPanel>().SelectMany(p=>p.Controls.OfType<Button>()).All(b=>b.Text=="历史战绩"),"details window is view-only");
        Call("OpenPaperTrading");Check(form.OwnedForms.OfType<PaperTradingForm>().Count()==1,"details reuses existing window");
        Set("timelineClose",(float)Math.Log(1.4));Call("UpdateTradingRisk");Check(wallet.Shares==0&&wallet.Fills[^1].Side=="强制平仓","risk management runs without a trade popup");
        details.Close();
        string output=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../Docs/UiPreview"));Directory.CreateDirectory(output);
        foreach(var size in new[]{new Size(1560,900),new Size(1180,720)}){
            form.Size=size;Application.DoEvents();Paint();
            var energy=(Rectangle)Get("terminalEnergyBounds");var first=(Rectangle)((System.Collections.IList)Get("tradingHits"))[0]!.GetType().GetField("Item1")!.GetValue(((System.Collections.IList)Get("tradingHits"))[0])!;
            Check(energy.Bottom<first.Top,"energy and toolbar do not overlap at "+size.Width);
            using var image=new Bitmap(form.Width,form.Height);form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size));image.Save(Path.Combine(output,"TradingBar_"+size.Width+".png"));
        }
        Call("SetIndicatorPaneCount",4);Paint();
        var panes=(System.Collections.IList)Get("indicatorPaneBounds");var energyBounds=(Rectangle)Get("terminalEnergyBounds");
        Check(panes.Count==4&&panes.Cast<Rectangle>().All(p=>p.Bottom<=energyBounds.Top-20),"four subcharts and fixed energy remain separate at minimum size");
        Set("hasAudioSignal",true);wallet.Reset();wallet.SetLeverage(3);Set("timelineClose",0f);Call("SubmitTrade","short");
        double final=wallet.Equity(100);Call("ResetSongChart",180d,"next track");
        var history=(System.Collections.IList)Get("tradeRounds");var round=(TradeRound)history[history.Count-1]!;
        Check(Near(round.FinalEquity,final)&&round.EndingPosition<0&&round.EndingMargin>0&&wallet.Shares==0&&wallet.Cash==100000,"song change records short PnL then resets margin and position");
        form.Close();
    }
}
