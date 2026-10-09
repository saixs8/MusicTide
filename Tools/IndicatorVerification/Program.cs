using SpectrumKlinePlayer;
using System.Reflection;
using System.IO;
class Check
{
    static int passed;
    static void Assert(bool ok,string name){if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
    static bool Near(double a,double b)=>Math.Abs(a-b)<1e-8;
    [STAThread] static void Main()
    {
        ApplicationConfiguration.Initialize();
        IndicatorBar Bar(double close)=>new(close,close,close,close);
        var rising=Enumerable.Range(1,40).Select(i=>Bar(i)).ToArray();
        var flat=Enumerable.Repeat(new IndicatorBar(100,110,90,100),40).ToArray();
        var ma=TechnicalIndicators.Calculate(TechnicalIndicator.MA,rising);
        Assert(double.IsNaN(ma.Lines[0].Values[3])&&Near(ma.Lines[0].Values[4],3),"MA waits for period and matches known mean");
        var boll=TechnicalIndicators.Calculate(TechnicalIndicator.BOLL,rising.Take(20).ToArray());
        Assert(Near(boll.Lines[0].Values[19],10.5)&&Near(boll.Lines[1].Values[19],10.5+2*Math.Sqrt(33.25))&&Near(boll.Lines[2].Values[19],10.5-2*Math.Sqrt(33.25)),"BOLL population deviation and bands match closed-form result");
        var macd=TechnicalIndicators.Calculate(TechnicalIndicator.MACD,new[]{Bar(100),Bar(101)});double dif=2d/13-2d/27;
        Assert(Near(macd.Lines[0].Values[1],dif)&&Near(macd.Lines[1].Values[1],dif*.2)&&Near(macd.Histogram![1],dif*1.6),"MACD EMA recurrence and doubled histogram");
        var kdj=TechnicalIndicators.Calculate(TechnicalIndicator.KDJ,flat);
        Assert(kdj.Lines.All(l=>l.Values.All(v=>Near(v,50))),"KDJ symmetric range stays neutral");
        var rsi=TechnicalIndicators.Calculate(TechnicalIndicator.RSI,rising);
        Assert(rsi.Lines.All(l=>Near(l.Values[^1],100)),"RSI rising input reaches 100");
        Assert(TechnicalIndicators.Calculate(TechnicalIndicator.RSI,rising.Reverse().ToArray()).Lines.All(l=>Near(l.Values[^1],0)),"RSI falling input reaches 0");
        Assert(TechnicalIndicators.Calculate(TechnicalIndicator.WR,flat).Lines.All(l=>Near(l.Values[^1],50)),"WR uses Chinese terminal 0 to 100 range");
        var bias=TechnicalIndicators.Calculate(TechnicalIndicator.BIAS,rising.Take(30).ToArray());
        Assert(Near(bias.Lines[0].Values[^1],100*(30d/27.5-1)),"BIAS ratio uses correct rolling mean");
        Assert(Near(TechnicalIndicators.Calculate(TechnicalIndicator.CCI,rising.Take(30).ToArray()).Lines[0].Values[^1],6.5/(.015*3.5)),"CCI typical price and mean absolute deviation");
        var atr=TechnicalIndicators.Calculate(TechnicalIndicator.ATR,new[]{new IndicatorBar(10,11,9,10),new IndicatorBar(15,16,14,15),new IndicatorBar(15,17,13,16)},new(2));
        Assert(Near(atr.Lines[0].Values[1],4)&&Near(atr.Lines[0].Values[2],5),"ATR includes gaps and follows terminal simple average");
        Assert(Near(TechnicalIndicators.Calculate(TechnicalIndicator.ROC,rising.Take(30).ToArray()).Lines[0].Values[^1],100*(30d/18-1)),"ROC uses N-period reference");
        foreach(var kind in Enum.GetValues<TechnicalIndicator>())
        {
            var result=TechnicalIndicators.Calculate(kind,Enumerable.Repeat(Bar(100),80).ToArray());
            Assert(result.Lines.All(l=>l.Values.Length==80&&!l.Values.Any(double.IsInfinity)),kind+" handles flat input without infinity");
            Assert(TechnicalIndicators.Calculate(kind,Array.Empty<IndicatorBar>()).Lines.All(l=>l.Values.Length==0),kind+" handles empty input");
        }
        using var form=new Form1();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        FieldInfo Field(string name)=>typeof(Form1).GetField(name,flags)!;
        void Set(string name,object value)=>Field(name).SetValue(form,value);
        object? Call(string name,params object[] args)=>typeof(Form1).GetMethod(name,flags)!.Invoke(form,args);
        Set("saveIndicatorSettings",false);
        typeof(Form1).GetMethods(flags).Single(m=>m.Name=="OnFormClosing"&&m.GetParameters().Length==2).Invoke(form,new object?[]{null,new FormClosingEventArgs(CloseReason.None,false)});
        Set("closing",false);form.Show();Application.DoEvents();
        var stage=(Control)Field("stage").GetValue(form)!;
        Rectangle Render(){using var bmp=new Bitmap(stage.Width,stage.Height);stage.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));return (Rectangle)Field("terminalEnergyBounds").GetValue(form)!;}
        Call("SetIndicatorPaneCount",0);var energy=Render();
        Call("SetIndicatorPaneCount",5);Assert(((List<TechnicalIndicator>)Field("subIndicators").GetValue(form)!).Count==4,"pane count is capped at four");
        var fourEnergy=Render();Assert(energy==fourEnergy,"energy rectangle stays fixed from zero to four panes");
        var panes=(List<Rectangle>)Field("indicatorPaneBounds").GetValue(form)!;
        Assert(panes.Count==4&&panes.Zip(panes.Skip(1)).All(pair=>pair.First.Bottom<=pair.Second.Top)&&panes[^1].Bottom<energy.Top-19,"four panes remain ordered above fixed energy");
        var pane=panes[2];Call("OnTerminalMouseDown",stage,new MouseEventArgs(MouseButtons.Left,1,pane.Left+150,pane.Top+5,0));
        Call("SelectIndicator",TechnicalIndicator.CCI);
        Assert(((List<TechnicalIndicator>)Field("subIndicators").GetValue(form)!)[2]==TechnicalIndicator.CCI,"selection replaces the clicked pane");
        Call("SelectIndicator",TechnicalIndicator.BOLL);Assert((TechnicalIndicator)Field("mainIndicator").GetValue(form)! ==TechnicalIndicator.BOLL&&((List<TechnicalIndicator>)Field("subIndicators").GetValue(form)!).Count==4,"BOLL switches main overlay without consuming pane slots");
        stage.Focus();foreach(char c in "RSI\r")Call("OnIndicatorKeyPress",form,new KeyPressEventArgs(c));
        Assert(((List<TechnicalIndicator>)Field("subIndicators").GetValue(form)!)[0]==TechnicalIndicator.RSI,"keyboard code and Enter switch indicator");
        var history=(List<IndicatorBar>)Field("indicatorHistory").GetValue(form)!;history.AddRange(rising);
        Set("timelineCandleCount",1);var bars=(IndicatorBar[])Call("GetIndicatorBars")!;
        Assert(bars.Length==41,"indicator computation retains completed history beyond displayed window");
        Call("ResetSongChart",180d,"next song");Assert(history.Count==0,"track reset clears indicator history");
        Assert((TechnicalIndicator)Field("mainIndicator").GetValue(form)! ==TechnicalIndicator.BOLL,"track reset preserves indicator choice");
        form.BeginInvoke(new Action(()=>
        {
            var dialog=Application.OpenForms.Cast<Form>().Single(f=>f.Text.Contains("布林线")&&f!=form);
            var numbers=dialog.Controls.OfType<NumericUpDown>().ToArray();numbers[0].Value=26;numbers[1].Value=2.5m;
            dialog.Controls.OfType<Button>().Single(b=>b.Text=="确定").PerformClick();
        }));
        Call("EditIndicatorParameters",TechnicalIndicator.BOLL);
        var parameters=(Dictionary<TechnicalIndicator,IndicatorParameters>)Field("indicatorParameters").GetValue(form)!;
        Assert(parameters[TechnicalIndicator.BOLL] is {N1:26,Factor:2.5},"parameter dialog updates period and standard deviation factor");
        string settings=Path.Combine(AppContext.BaseDirectory,"Settings","indicators.json");string? original=File.Exists(settings)?File.ReadAllText(settings):null;
        try
        {
            Set("saveIndicatorSettings",true);Call("SaveIndicatorSettings");Set("saveIndicatorSettings",false);
            Call("SelectIndicator",TechnicalIndicator.MA);Call("SetIndicatorPaneCount",0);parameters.Clear();Call("LoadIndicatorSettings");
            Assert((TechnicalIndicator)Field("mainIndicator").GetValue(form)! ==TechnicalIndicator.BOLL&&((List<TechnicalIndicator>)Field("subIndicators").GetValue(form)!).Count==4&&parameters[TechnicalIndicator.BOLL].N1==26,"indicator choices and parameters reload from settings");
        }
        finally{if(original is null)File.Delete(settings);else File.WriteAllText(settings,original);}
        form.Close();Console.WriteLine($"All {passed} indicator checks passed");
    }
}
