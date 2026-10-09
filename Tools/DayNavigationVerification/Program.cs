using SpectrumKlinePlayer;
using System.Reflection;
using System.IO;
using System.Drawing.Imaging;
using System.Collections;
using System.Runtime.InteropServices;
class Verify
{
    static int checks;
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    static IntPtr Location(Point p) => (IntPtr)((p.Y & 65535) << 16 | (p.X & 65535));
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    [STAThread]static void Main()
    {
        ApplicationConfiguration.Initialize();var v=new DayKViewport();
        Check(v.Range(150)==(100,150),"initial viewport follows newest 50 bars");
        v.Pan(-60,150);Check(v.Range(150)==(40,90)&&!v.Following,"pan selects older bars");
        Check(v.Range(151)==(40,90),"incoming bar does not shift historical view");
        v.Zoom(1,151,.4);Check(v.Capacity==40 && Math.Abs(v.Start+16-60)<=1,"historical zoom retains cursor anchor");
        v.Pan(-1000,151);Check(v.Start==0,"oldest edge is bounded");v.Pan(1000,151);Check(v.Following&&v.Range(151).End==151,"latest edge resumes follow");
        for(int i=0;i<50;i++)v.Zoom(1,151);Check(v.Capacity==12,"maximum zoom bounded");
        for(int i=0;i<50;i++)v.Zoom(-1,151);Check(v.Capacity==500&&v.Range(151)==(0,151),"zoom out shows all available bars with bounded capacity");
        Check(v.Range(0)==(0,0),"empty chart has valid range");v.Reset();v.MoveTo(50,200);v.RemoveOldest();Check(v.Start==49,"history eviction preserves same bars when available");
        v.Latest(201);Check(v.Following&&v.Range(201)==(151,201),"latest action restores realtime follow");
        using var form=new Form1();var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        FieldInfo F(string n)=>typeof(Form1).GetField(n,flags)!;
        void Set(string n,object x)=>F(n).SetValue(form,x);
        object? Call(string n,params object?[] args)=>typeof(Form1).GetMethods(flags).Single(m=>m.Name==n&&m.DeclaringType==typeof(Form1)).Invoke(form,args);
        Call("OnFormClosing",null,new FormClosingEventArgs(CloseReason.None,false));Set("saveIndicatorSettings",false);
        Set("currentTrackTitle","K线历史浏览 · 验证数据");Set("timelineCandleCount",1);
        var array=(Array)F("timelineCandles").GetValue(form)!;var type=array.GetType().GetElementType()!;
        object Candle(int i)
        {
            object c=Activator.CreateInstance(type)!;float open=(float)(Math.Sin(i*.12)*.12+i*.0003),close=open+(float)Math.Sin(i*1.3)*.018f;
            foreach(var p in new[]{("Open",open),("Close",close),("High",Math.Max(open,close)+.014f),("Low",Math.Min(open,close)-.012f)})type.GetField(p.Item1)!.SetValue(c,p.Item2);
            return c;
        }
        for(int i=0;i<180;i++){array.SetValue(Candle(i),0);Call("RecordIndicatorBar");}array.SetValue(Candle(180),0);
        Check(((Array)Call("GetDayCandles")!).Length==181,"completed OHLC history survives original fifty-bar window");
        Check(((IndicatorBar[])Call("GetIndicatorBars")!).Length==181,"indicators retain matching full history");
        form.Size=new(1560,900);form.Show();Application.DoEvents();
        var viewport=(DayKViewport)F("dayViewport").GetValue(form)!;
        Rectangle Plot()=>(Rectangle)F("terminalPlot").GetValue(form)!;
        void Render(string name)
        {
            form.PerformLayout();using var bmp=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bmp,new(Point.Empty,bmp.Size));
            Directory.CreateDirectory("Docs/UiPreview");bmp.Save("Docs/UiPreview/"+name+".png",ImageFormat.Png);
        }
        Render("DayK_Latest");var energy=(Rectangle)F("terminalEnergyBounds").GetValue(form)!;
        var plot=Plot();var point=new Point(plot.Left+plot.Width/2,plot.Top+plot.Height/2);
        Call("HandleDayNavigationWheel",new MouseEventArgs(MouseButtons.None,0,point.X,point.Y,120),Keys.None);
        Check(viewport.Capacity==50,"plain chart wheel does not change zoom");
        Call("HandleDayNavigationWheel",new MouseEventArgs(MouseButtons.None,0,point.X,point.Y,120),Keys.Control);
        Check(viewport.Capacity==40,"Ctrl chart wheel zooms in");
        Call("HandleDayNavigationWheel",new MouseEventArgs(MouseButtons.None,0,point.X,point.Y,-120),Keys.Control);Check(viewport.Capacity==50,"Ctrl chart wheel zooms out");
        int beforeWheel=viewport.Capacity;
        Call("HandleDayNavigationWheel",new MouseEventArgs(MouseButtons.None,0,point.X,point.Y,0),Keys.Control);
        Check(viewport.Capacity==beforeWheel,"zero wheel delta leaves viewport unchanged");
        var targets=(float[])F("energyTargets").GetValue(form)!;var levels=(float[])F("energyLevels").GetValue(form)!;
        Array.Fill(targets,.7f);Array.Fill(levels,.2f);var savedTargets=targets.ToArray();var savedLevels=levels.ToArray();
        Call("OnTerminalMouseDown",form,new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0));
        Call("OnDayNavigationMove",form,new MouseEventArgs(MouseButtons.Left,0,point.X+plot.Width/2,point.Y,0));Call("EndDayNavigation");
        Check(!viewport.Following&&viewport.Range(181).Start<131,"actual rightward drag reveals older bars");
        int olderStart=viewport.Range(181).Start;
        Call("OnTerminalMouseDown",form,new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0));
        Call("OnDayNavigationMove",form,new MouseEventArgs(MouseButtons.Left,0,point.X-plot.Width/4,point.Y,0));Call("EndDayNavigation");
        Check(viewport.Range(181).Start>olderStart,"actual leftward drag reveals newer bars");
        Check(targets.SequenceEqual(savedTargets)&&levels.SequenceEqual(savedLevels),"zoom and drag never modify live spectrum values");
        var r=viewport.Range(181);
        Check((int)Call("IndicatorSample",plot.Left,plot,181)! ==r.Start && (int)Call("IndicatorSample",plot.Right,plot,181)! ==r.End-1,"crosshair samples visible historical range");
        Render("DayK_History");Check((Rectangle)F("terminalEnergyBounds").GetValue(form)! ==energy,"energy remains anchored during zoom and pan");
        var latest=(Rectangle)F("dayLatestBounds").GetValue(form)!;Call("OnTerminalMouseDown",form,new MouseEventArgs(MouseButtons.Left,1,latest.Left+5,latest.Top+5,0));
        Check(viewport.Following&&viewport.Range(181).End==181,"actual latest button restores current bars");
        viewport.MoveTo(30,181);Render("DayK_History");
        Call("SetIndicatorPaneCount",4);Set("mainIndicator",TechnicalIndicator.BOLL);form.Size=new(1180,720);Render("DayK_HistoryFour_Minimum");
        Check((int)Call("IndicatorStart",181)! ==30 && (int)Call("IndicatorEnd",181)! ==80,"four technical panes share viewport range");
        var e4=(Rectangle)F("terminalEnergyBounds").GetValue(form)!;Call("SetIndicatorPaneCount",0);Render("DayK_HistoryZero_Minimum");
        Check((Rectangle)F("terminalEnergyBounds").GetValue(form)! ==e4,"energy position independent of optional pane count");
        var stage=(Control)F("stage").GetValue(form)!;stage.Focus();var key=new KeyEventArgs(Keys.End);Call("OnKeyDown",form,key);
        Check(viewport.Following&&key.Handled,"End shortcut returns latest");
        Check(targets.SequenceEqual(savedTargets)&&levels.SequenceEqual(savedLevels),"navigation keyboard does not change live spectrum values");
        Set("closing",false);Set("simulationMode",true);Set("trackIsPlaying",false);
        var frameClock=(System.Diagnostics.Stopwatch)F("clock").GetValue(form)!;
        Set("lastSpectrumAt",frameClock.Elapsed.TotalSeconds-.1);Call("OnFrame");
        Check(!levels.SequenceEqual(savedLevels),"live spectrum continues updating while price candles are paused");
        Set("closing",true);
        Call("SelectTerminalChart",Enum.Parse(F("chartMode").FieldType,"MinuteLine"));Render("DayK_MinuteUnchanged");
        Check((Rectangle)F("dayLatestBounds").GetValue(form)! ==Rectangle.Empty,"minute chart hides day navigation controls");
        Call("ResetSongChart",180d,"下一曲");Check(((IList)F("dayHistory").GetValue(form)!).Count==0&&viewport.Capacity==50&&viewport.Following,"track reset clears historical view and restores default zoom");
        Set("trackIsPlaying",true);Call("UpdateDayKRolling");
        var clock=(System.Diagnostics.Stopwatch)F("clock").GetValue(form)!;
        Set("dayKCandleStart",clock.Elapsed.TotalSeconds-80*(float)F("candleDurationSeconds").GetValue(form)!);
        Call("UpdateDayKRolling");
        Check((int)F("timelineCandleCount").GetValue(form)! ==50&&((Array)Call("GetDayCandles")!).Length>=81,"actual rolling generator retains bars after original buffer shifts");
        Call("ResetSongChart",180d,"少量K线 · 原生窗口消息验证");
        Call("SelectTerminalChart",Enum.Parse(F("chartMode").FieldType,"DayK"));
        Set("timelineCandleCount",6);for(int i=0;i<6;i++)array.SetValue(Candle(i),i);
        Render("DayK_ShortBefore");plot=Plot();point=new(plot.Left+plot.Width/2,plot.Top+plot.Height/2);
        Check(stage.CanFocus&&stage.Focus(),"chart canvas is genuinely focusable for wheel routing");
        Check(!(bool)F("terminalSettingsVisible").GetValue(form)!&&!((Control)F("controlPanel").GetValue(form)!).Visible,"terminal settings panel starts collapsed");
        float beforeX=(float)Call("IndicatorX",plot,2,6)!;
        SendMessage(stage.Handle,0x20A,(IntPtr)((120<<16)|8),Location(stage.PointToScreen(point)));
        Check(viewport.Capacity==40,"native WM_MOUSEWHEEL reaches chart with Ctrl modifier");
        Check((float)Call("IndicatorX",plot,2,6)!>beforeX,"six-bar chart visibly changes horizontal spacing when zoomed");
        SendMessage(stage.Handle,0x201,(IntPtr)1,Location(point));
        Check((bool)F("dayDragging").GetValue(form)!&&stage.Capture,"native left button starts captured chart drag");
        SendMessage(stage.Handle,0x200,(IntPtr)1,Location(new(point.X+plot.Width/4,point.Y)));
        SendMessage(stage.Handle,0x202,IntPtr.Zero,Location(new(point.X+plot.Width/4,point.Y)));
        Check(viewport.Start<0&&!stage.Capture,"native short-history drag visibly moves bars into available blank space");
        Render("DayK_ShortAfter");
        int shiftedStart=viewport.Start;
        SendMessage(stage.Handle,0x201,(IntPtr)1,Location(point));
        SendMessage(stage.Handle,0x200,(IntPtr)1,Location(new(point.X-plot.Width/4,point.Y)));
        SendMessage(stage.Handle,0x202,IntPtr.Zero,Location(new(point.X-plot.Width/4,point.Y)));
        Check(viewport.Start>shiftedStart,"native leftward drag returns short-history bars toward latest");
        Console.WriteLine($"Passed {checks} day navigation checks");form.Close();
    }
}
