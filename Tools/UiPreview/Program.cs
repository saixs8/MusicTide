using SpectrumKlinePlayer;
using System.IO;
using System.Reflection;
using System.Drawing.Imaging;

class Preview
{
    static readonly BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static FieldInfo Field(string name) => typeof(Form1).GetField(name, Flags)!;
    static void Set(Form1 form, string name, object value) => Field(name).SetValue(form, value);
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var form = new Form1();
        Set(form,"saveIndicatorSettings",false);
        typeof(Form1).GetMethods(Flags).Single(m=>m.Name=="OnFormClosing" && m.GetParameters().Length==2).Invoke(form,new object?[]{null,new FormClosingEventArgs(CloseReason.None,false)});
        Set(form,"currentTrackTitle","音乐律动 · 音潮行情演示");
        Set(form,"playbackAvailable",true); Set(form,"playbackPlaying",true);
        Set(form,"volumeAvailable",true); Set(form,"playerVolume",.65f);
        Set(form,"currentLyric","让音乐的每一次起伏，都成为眼前的行情");
        Set(form,"simulationMode",true);
        Set(form,"songPositionSeconds",93d); Set(form,"lastTrackPositionSeconds",93d);
        Set(form,"songDurationSeconds",243d); Set(form,"displayRange",.2f);
        Set(form,"lyricTimeline",new TimedLyricLine[]{new(0,"音乐律动 · 歌词资讯演示"),new(12,"让音乐的每一次起伏"),new(24,"都成为眼前的行情"),new(42,"低音在夜色中回响"),new(66,"旋律沿着时间向前"),new(89,"此刻的歌声，正在播放"),new(105,"下一句，将随进度自动点亮"),new(132,"所有歌词，依次排列")});
        Set(form,"activeLyricIndex",5);
        Set(form,"lyricTimingStatus","播放器进度 · 实时同步（演示）");
        Set(form,"timelineCandleCount",50);
        var levels=(float[])Field("bandLevels").GetValue(form)!;
        var targets=(float[])Field("bandTargets").GetValue(form)!;
        for(int i=0;i<levels.Length;i++) levels[i]=targets[i]=(float)(.18+.7*Math.Abs(Math.Sin(i*.14))*Math.Exp(-i*.009));
        foreach(string name in new[]{"candles","timelineCandles"})
        {
            var array=(Array)Field(name).GetValue(form)!; var type=array.GetType().GetElementType()!;
            for(int i=0;i<array.Length;i++)
            {
                var candle=Activator.CreateInstance(type)!;
                float open=(float)(Math.Sin(i*.18)*.11+(i/50d-.5)*.04);
                float close=open+(float)Math.Sin(i*1.29)*.026f;
                type.GetField("Open")!.SetValue(candle,open); type.GetField("Close")!.SetValue(candle,close);
                type.GetField("High")!.SetValue(candle,Math.Max(open,close)+.015f);
                type.GetField("Low")!.SetValue(candle,Math.Min(open,close)-.011f);
                array.SetValue(candle,i);
            }
        }
        var minute=(float[])Field("minuteValues").GetValue(form)!;
        for(int i=0;i<minute.Length;i++)minute[i]=(float)(.07*Math.Sin(i*.022)+.012*Math.Sin(i*.75));
        Set(form,"minutePointTotal",220);
        string output=Path.GetFullPath("Docs/UiPreview");
        form.Show();
        Application.DoEvents();
        foreach(var size in new[]{new Size(1560,900),new Size(1180,720)})
        {
            form.Size=size; form.CreateControl(); form.PerformLayout();
            using var bmp=new Bitmap(form.Width,form.Height);
            form.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));
            bmp.Save(Path.Combine(output,$"Terminal_{size.Width}x{size.Height}.png"),ImageFormat.Png);
        }
        typeof(Form1).GetMethod("SetIndicatorPaneCount",Flags)!.Invoke(form,new object[]{4});
        Set(form,"mainIndicator",TechnicalIndicator.BOLL);
        foreach(var size in new[]{new Size(1560,900),new Size(1180,720)})
        {
            form.Size=size; form.PerformLayout();
            using var bmp=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));
            bmp.Save(Path.Combine(output,$"Terminal_Indicators4_{size.Width}x{size.Height}.png"),ImageFormat.Png);
        }
        var chartEnum=Field("chartMode").FieldType;Set(form,"chartMode",Enum.ToObject(chartEnum,1));
        form.Size=new Size(1560,900);form.PerformLayout();
        using(var bmp=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(Path.Combine(output,"Terminal_Minute.png"),ImageFormat.Png);}
        var stage=(Control)Field("stage").GetValue(form)!;
        stage.Size=new Size(1920,1080);
        using(var bmp=new Bitmap(1920,1080))using(var graphics=Graphics.FromImage(bmp))
        {
            typeof(Form1).GetMethod("DrawTerminalStage",Flags)!.Invoke(form,new object[]{graphics,new Rectangle(0,0,1920,1080)});
            bmp.Save(Path.Combine(output,"Terminal_Fullscreen.png"),ImageFormat.Png);
        }
        var rowHits=(List<(Rectangle Bounds,int Band)>)Field("quoteRowHits").GetValue(form)!;
        var row=rowHits[3];
        typeof(Form1).GetMethod("OnTerminalMouseDown",Flags)!.Invoke(form,new object?[]{stage,new MouseEventArgs(MouseButtons.Left,1,row.Bounds.Left+5,row.Bounds.Top+5,0)});
        if((int)Field("minuteBandIndex").GetValue(form)! != row.Band)throw new Exception("Quote selection failed");
        var slider=(TrackBar)Field("minuteFrequencyControl").GetValue(form)!;
        if(slider.Value!=row.Band)throw new Exception("Frequency control linkage failed");
        var tab=(Rectangle)Field("dayTabBounds").GetValue(form)!;
        typeof(Form1).GetMethod("OnTerminalMouseDown",Flags)!.Invoke(form,new object?[]{stage,new MouseEventArgs(MouseButtons.Left,1,tab.Left+5,tab.Top+5,0)});
        if((int)Field("chartMode").GetValue(form)! != 0)throw new Exception("Chart tab switching failed");
        Set(form,"quoteScroll",0);
        var table=(Rectangle)Field("quoteBounds").GetValue(form)!;
        typeof(Form1).GetMethod("OnTerminalMouseWheel",Flags)!.Invoke(form,new object?[]{stage,new MouseEventArgs(MouseButtons.None,0,table.Left+5,table.Top+70,-120)});
        if((int)Field("quoteScroll").GetValue(form)! != 3)throw new Exception("Quote scrolling failed");
        int[] Bands() => (int[])typeof(Form1).GetMethod("QuoteBands",Flags)!.Invoke(form,null)!;
        void Click(Rectangle r) => typeof(Form1).GetMethod("OnTerminalMouseDown",Flags)!.Invoke(form,new object?[]{stage,new MouseEventArgs(MouseButtons.Left,1,r.Left+5,r.Top+5,0)});
        var headers=(List<(Rectangle Bounds,int Column)>)Field("quoteHeaderHits").GetValue(form)!;
        for(int col=0;col<5;col++)
        {
            Click(headers[col].Bounds);var asc=Bands();
            if((int)Field("quoteSortDirection").GetValue(form)!!=1)throw new Exception("Header ascending click failed");
            Click(headers[col].Bounds);var desc=Bands();
            if((int)Field("quoteSortDirection").GetValue(form)!!=-1)throw new Exception("Header descending click failed");
            if(!asc.SequenceEqual(desc.Reverse()))throw new Exception("Numeric ascending/descending mismatch");
            Click(headers[col].Bounds);if(!Bands().SequenceEqual(Enumerable.Range(0,100)))throw new Exception("Third click must restore default order");
        }
        var filters=(List<(Rectangle Bounds,int Filter)>)Field("quoteFilterHits").GetValue(form)!;
        var sets=new List<int[]>();
        for(int i=1;i<=4;i++) { Click(filters[i].Bounds);sets.Add(Bands()); }
        if(sets.Any(a=>a.Length==0)||sets.SelectMany(a=>a).Distinct().Count()!=100||sets.Sum(a=>a.Length)!=100)throw new Exception("Board filters must partition all hundred bands");
        Click(headers[1].Bounds);Click(headers[1].Bounds);
        if(!Bands().SequenceEqual(sets[^1].Reverse()))throw new Exception("Board filter must retain descending numeric sort");
        Click(filters[0].Bounds);
        using(var bmp=new Bitmap(stage.Width,stage.Height))stage.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));
        row=rowHits[0];Click(row.Bounds);
        if(slider.Value!=row.Band)throw new Exception("Sorted row must select original band");
        string[] MarketCells() => (string[])typeof(Form1).GetMethod("MarketOverviewCells",Flags)!.Invoke(form,null)!;
        void PaintStage() { using var bmp=new Bitmap(stage.Width,stage.Height);stage.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size)); }
        var savedIndicators=((List<TechnicalIndicator>)Field("subIndicators").GetValue(form)!).ToArray();
        var savedMain=(TechnicalIndicator)Field("mainIndicator").GetValue(form)!;
        var dayCells=MarketCells();
        if(dayCells.Any(c=>c.Contains("换手")||c.Contains("成交")))throw new Exception("Day K contains intraday fields");
        PaintStage();var fixedEnergy=(Rectangle)Field("terminalEnergyBounds").GetValue(form)!;
        Click((Rectangle)Field("minuteTabBounds").GetValue(form)!);PaintStage();
        var minuteCells=MarketCells();
        if(!minuteCells.Any(c=>c.Contains("换手率"))||!minuteCells.Any(c=>c.Contains("成交量")))throw new Exception("Minute fields missing");
        if(((List<Rectangle>)Field("indicatorPaneBounds").GetValue(form)!).Count!=0)throw new Exception("Daily technical panes must be hidden in minute mode");
        if(fixedEnergy!=(Rectangle)Field("terminalEnergyBounds").GetValue(form)!)throw new Exception("Energy panel moved on chart switch");
        Click((Rectangle)Field("dayTabBounds").GetValue(form)!);PaintStage();
        if(!savedIndicators.SequenceEqual((List<TechnicalIndicator>)Field("subIndicators").GetValue(form)!)||savedMain!=(TechnicalIndicator)Field("mainIndicator").GetValue(form)!)throw new Exception("Daily indicator selections lost during chart switches");
        Console.WriteLine("Minute/day fields, technical pane separation, preserved indicator choices and fixed energy switching passed");
        Console.WriteLine("Board partition, five-column three-state sorting, filtered sort and sorted row identity passed");
        Console.WriteLine("Rendered 4 previews; quote selection, slider linkage, chart tabs and scrolling passed");
    }
}
