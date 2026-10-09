using System.Drawing.Drawing2D;
using System.Text.Json;
using System.IO;

namespace SpectrumKlinePlayer;

public sealed partial class Form1
{
    private TechnicalIndicator mainIndicator = TechnicalIndicator.MA;
    private readonly List<TechnicalIndicator> subIndicators = [TechnicalIndicator.MACD];
    private readonly Dictionary<TechnicalIndicator, IndicatorParameters> indicatorParameters = new();
    private readonly List<IndicatorBar> indicatorHistory = new();
    private readonly List<Rectangle> indicatorPaneBounds = new();
    private readonly List<(Rectangle Bounds, TechnicalIndicator Kind)> indicatorTabHits = new();
    private Rectangle indicatorCountBounds, indicatorMoreBounds, indicatorMainHeader, terminalEnergyBounds;
    private int activeIndicatorPane = -1;
    private string indicatorCode = string.Empty;
    private DateTime indicatorCodeAt;
    private bool saveIndicatorSettings = true;
    private static readonly Color[] IndicatorColors = [Color.FromArgb(235,235,240),TerminalTheme.Yellow,Color.FromArgb(225,84,200)];
    private string IndicatorSettingsPath => Path.Combine(AppContext.BaseDirectory,"Settings","indicators.json");
    private sealed record IndicatorPreferences(TechnicalIndicator Main, TechnicalIndicator[] Sub, Dictionary<TechnicalIndicator,IndicatorParameters> Parameters);

    private IndicatorParameters Parameters(TechnicalIndicator kind) => indicatorParameters.GetValueOrDefault(kind) ?? TechnicalIndicators.Defaults(kind);

    private void LoadIndicatorSettings()
    {
        try
        {
            if (!File.Exists(IndicatorSettingsPath)) return;
            var preferences=JsonSerializer.Deserialize<IndicatorPreferences>(File.ReadAllText(IndicatorSettingsPath));
            if(preferences is null)return;
            if(Enum.IsDefined(preferences.Main)&&TechnicalIndicators.IsOverlay(preferences.Main))mainIndicator=preferences.Main;
            subIndicators.Clear();subIndicators.AddRange((preferences.Sub??[]).Where(k=>Enum.IsDefined(k)&&!TechnicalIndicators.IsOverlay(k)).Take(4));
            foreach(var pair in preferences.Parameters??new())
            {
                var defaults=TechnicalIndicators.Defaults(pair.Key);
                if(Enum.IsDefined(pair.Key)&&pair.Value.N1 is >=1 and <=250&&pair.Value.N2 is >=0 and <=250&&pair.Value.N3 is >=0 and <=250&&pair.Value.Factor is >0 and <=10
                    &&(defaults.N2==0||pair.Value.N2>0)&&(defaults.N3==0||pair.Value.N3>0)
                    &&(pair.Key!=TechnicalIndicator.MACD||pair.Value.N1<pair.Value.N2))indicatorParameters[pair.Key]=pair.Value;
            }
        }
        catch { }
    }
    private void SaveIndicatorSettings()
    {
        if(!saveIndicatorSettings)return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(IndicatorSettingsPath)!); File.WriteAllText(IndicatorSettingsPath,JsonSerializer.Serialize(new IndicatorPreferences(mainIndicator,subIndicators.ToArray(),indicatorParameters),new JsonSerializerOptions{WriteIndented=true})); }
        catch { }
    }
    private void SetIndicatorPaneCount(int count)
    {
        if (chartMode == ChartMode.MinuteLine) SelectTerminalChart(ChartMode.DayK);
        count=Math.Clamp(count,0,4);
        TechnicalIndicator[] defaults=[TechnicalIndicator.MACD,TechnicalIndicator.KDJ,TechnicalIndicator.RSI,TechnicalIndicator.WR];
        while(subIndicators.Count<count)subIndicators.Add(defaults[subIndicators.Count]);
        if(subIndicators.Count>count)subIndicators.RemoveRange(count,subIndicators.Count-count);
        activeIndicatorPane=Math.Clamp(activeIndicatorPane,-1,subIndicators.Count-1);
        SaveIndicatorSettings();stage.Invalidate();
    }
    private void SelectIndicator(TechnicalIndicator kind)
    {
        if (chartMode == ChartMode.MinuteLine) SelectTerminalChart(ChartMode.DayK);
        if(TechnicalIndicators.IsOverlay(kind)){ mainIndicator=kind;activeIndicatorPane=-1; }
        else
        {
            if(subIndicators.Count==0)subIndicators.Add(kind);
            activeIndicatorPane=Math.Clamp(activeIndicatorPane,0,subIndicators.Count-1);
            subIndicators[activeIndicatorPane]=kind;
        }
        indicatorCode=string.Empty;SaveIndicatorSettings();stage.Invalidate();
    }
    private bool HandleIndicatorClick(MouseEventArgs e)
    {
        if (chartMode == ChartMode.MinuteLine) return terminalPlot.Contains(e.Location) || terminalEnergyBounds.Contains(e.Location);
        if(indicatorCountBounds.Contains(e.Location)){ShowIndicatorMenu(e.Location,true);return true;}
        if(indicatorMoreBounds.Contains(e.Location)){ShowIndicatorMenu(e.Location);return true;}
        foreach(var hit in indicatorTabHits)
            if(hit.Bounds.Contains(e.Location)){SelectIndicator(hit.Kind);return true;}
        for(int i=0;i<indicatorPaneBounds.Count;i++)
        {
            var pane=indicatorPaneBounds[i];if(!pane.Contains(e.Location))continue;
            activeIndicatorPane=i;stage.Invalidate();
            if(e.Clicks>1&&e.X<pane.Left+135&&e.Y<pane.Top+19)EditIndicatorParameters(subIndicators[i]);
            else if(e.Button==MouseButtons.Right||e.X>=pane.Left+118&&e.X<pane.Left+137&&e.Y<pane.Top+19)
            {
                ShowIndicatorMenu(e.Location);
            }
            return true;
        }
        if(terminalPlot.Contains(e.Location)||indicatorMainHeader.Contains(e.Location))
        {
            activeIndicatorPane=-1;stage.Invalidate();
            if(e.Button==MouseButtons.Right)ShowIndicatorMenu(e.Location);
            else if(indicatorMainHeader.Contains(e.Location))
            {
                if(e.Clicks>1)EditIndicatorParameters(mainIndicator);
                else if(e.X>=indicatorMainHeader.Left+110&&e.X<indicatorMainHeader.Left+127)ShowIndicatorMenu(e.Location);
            }
            return true;
        }
        // Spectrum is a permanent panel, not a replaceable technical indicator.
        return terminalEnergyBounds.Contains(e.Location);
    }
    private ContextMenuStrip MakeIndicatorContext()
    {
        return new ContextMenuStrip{Renderer=new TerminalMenuRenderer(),BackColor=TerminalTheme.Panel,ForeColor=TerminalTheme.MenuText,Font=new Font("Microsoft YaHei UI",9)};
    }
    private void ShowIndicatorMenu(Point position,bool countOnly=false)
    {
        var menu=MakeIndicatorContext();
        if(!countOnly)
        {
            var overlay=new ToolStripMenuItem("主图指标");var common=new ToolStripMenuItem("常用指标");
            foreach(var kind in Enum.GetValues<TechnicalIndicator>())
            {
                var item=new ToolStripMenuItem(IndicatorLabel(kind)){Checked=TechnicalIndicators.IsOverlay(kind)?mainIndicator==kind:activeIndicatorPane>=0&&subIndicators[activeIndicatorPane]==kind};
                item.Click+=(_,_)=>SelectIndicator(kind);
                (TechnicalIndicators.IsOverlay(kind)?overlay:common).DropDownItems.Add(item);
            }
            menu.Items.Add(overlay);menu.Items.Add(common);
            var edit=new ToolStripMenuItem("修改指标参数");edit.Click+=(_,_)=>EditIndicatorParameters(activeIndicatorPane<0?mainIndicator:subIndicators[activeIndicatorPane]);menu.Items.Add(edit);
            if(activeIndicatorPane>=0)
            {
                int pane=activeIndicatorPane;
                var remove=new ToolStripMenuItem("删除当前指标图");remove.Click+=(_,_)=>{subIndicators.RemoveAt(pane);activeIndicatorPane=Math.Min(pane,subIndicators.Count-1);SaveIndicatorSettings();stage.Invalidate();};menu.Items.Add(remove);
            }
            menu.Items.Add(new ToolStripSeparator());
        }
        var windows=new ToolStripMenuItem("指标窗口数量");
        for(int i=0;i<=4;i++){int count=i;var item=new ToolStripMenuItem(i==0?"不显示指标副图":$"{i} 个指标副图"){Checked=subIndicators.Count==i};item.Click+=(_,_)=>SetIndicatorPaneCount(count);windows.DropDownItems.Add(item);}
        menu.Items.Add(windows);
        var add=new ToolStripMenuItem("增加指标图"){Enabled=subIndicators.Count<4};add.Click+=(_,_)=>{SetIndicatorPaneCount(subIndicators.Count+1);activeIndicatorPane=subIndicators.Count-1;};menu.Items.Add(add);
        menu.Items.Add(new ToolStripMenuItem("能量柱 · 固定保留"){Enabled=false});
        TerminalTheme.SizeMenu(menu,playground.Settings.MenuSize);
        menu.Closed+=(_,_)=>{if(!IsDisposed&&IsHandleCreated)BeginInvoke(new Action(menu.Dispose));};menu.Show(stage,position);
    }
    private ToolStripMenuItem BuildIndicatorMenu()
    {
        var item=new ToolStripMenuItem("技术指标");
        var select=new ToolStripMenuItem("选择指标");select.Click+=(_,_)=>ShowIndicatorMenu(new Point(Math.Max(0,terminalPlot.Left),100));item.DropDownItems.Add(select);
        for(int i=0;i<=4;i++){int count=i;var child=new ToolStripMenuItem($"{i} 个指标副图");child.Click+=(_,_)=>SetIndicatorPaneCount(count);child.Tag=count;item.DropDownItems.Add(child);}
        item.DropDownOpening+=(_,_)=>{foreach(var child in item.DropDownItems.OfType<ToolStripMenuItem>())if(child.Tag is int count)child.Checked=subIndicators.Count==count;};
        return item;
    }
    private static string IndicatorLabel(TechnicalIndicator kind) => kind switch
    {
        TechnicalIndicator.MA=>"MA  移动平均线",TechnicalIndicator.BOLL=>"BOLL  布林线",TechnicalIndicator.EXPMA=>"EXPMA  指数均线",TechnicalIndicator.MACD=>"MACD  平滑异同平均",
        TechnicalIndicator.KDJ=>"KDJ  随机指标",TechnicalIndicator.RSI=>"RSI  相对强弱",TechnicalIndicator.WR=>"WR  威廉指标",TechnicalIndicator.BIAS=>"BIAS  乖离率",TechnicalIndicator.CCI=>"CCI  顺势指标",TechnicalIndicator.ATR=>"ATR  平均真实波幅",_=>"ROC  变动率"
    };
    private void EditIndicatorParameters(TechnicalIndicator kind)
    {
        var p=Parameters(kind);int[] periods=[p.N1,p.N2,p.N3];
        using var dialog=new Form{Text=$"{IndicatorLabel(kind)} · 参数",Size=new Size(360,310),FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,StartPosition=FormStartPosition.CenterParent,BackColor=TerminalTheme.Panel,ForeColor=TerminalTheme.MenuText,Font=Font};
        var numbers=new List<NumericUpDown>();int y=18;
        for(int i=0;i<3;i++)
        {
            if(i>0&&periods[i]==0)break;
            var label=new Label{Text=$"周期 {i+1}",Bounds=new Rectangle(20,y,140,28)};
            var number=new NumericUpDown{Minimum=1,Maximum=250,Value=periods[i],Bounds=new Rectangle(180,y,130,28),BackColor=TerminalTheme.Input,ForeColor=TerminalTheme.MenuText};numbers.Add(number);dialog.Controls.Add(label);dialog.Controls.Add(number);y+=36;
        }
        NumericUpDown? factor=null;
        if(kind==TechnicalIndicator.BOLL){dialog.Controls.Add(new Label{Text="标准差倍数",Bounds=new Rectangle(20,y,140,28)});factor=new NumericUpDown{Minimum=.1m,Maximum=10,DecimalPlaces=1,Increment=.1m,Value=(decimal)p.Factor,Bounds=new Rectangle(180,y,130,28),BackColor=TerminalTheme.Input,ForeColor=TerminalTheme.MenuText};dialog.Controls.Add(factor);y+=36;}
        var error=new Label{Bounds=new Rectangle(20,y,300,32),ForeColor=TerminalTheme.Rise};dialog.Controls.Add(error);
        var reset=new Button{Text="恢复默认",Bounds=new Rectangle(20,dialog.ClientSize.Height-45,90,28)};
        reset.Click+=(_,_)=>{var defaults=TechnicalIndicators.Defaults(kind);int[] values=[defaults.N1,defaults.N2,defaults.N3];for(int i=0;i<numbers.Count;i++)numbers[i].Value=values[i];if(factor is not null)factor.Value=(decimal)defaults.Factor;};dialog.Controls.Add(reset);
        var ok=new Button{Text="确定",Bounds=new Rectangle(220,dialog.ClientSize.Height-45,90,28)};
        ok.Click+=(_,_)=>{if(kind==TechnicalIndicator.MACD&&numbers[0].Value>=numbers[1].Value){error.Text="短周期必须小于长周期";return;}indicatorParameters[kind]=new((int)numbers[0].Value,numbers.Count>1?(int)numbers[1].Value:0,numbers.Count>2?(int)numbers[2].Value:0,factor is null?p.Factor:(double)factor.Value);SaveIndicatorSettings();stage.Invalidate();dialog.DialogResult=DialogResult.OK;};dialog.Controls.Add(ok);dialog.AcceptButton=ok;dialog.ShowDialog(this);
    }
    private void OnIndicatorKeyPress(object? sender,KeyPressEventArgs e)
    {
        if(!stage.ContainsFocus)return;
        if(DateTime.Now-indicatorCodeAt>TimeSpan.FromSeconds(4))indicatorCode=string.Empty;
        if(char.IsAsciiLetter(e.KeyChar)){indicatorCode=(indicatorCode+char.ToUpperInvariant(e.KeyChar));if(indicatorCode.Length>6)indicatorCode=indicatorCode[^6..];indicatorCodeAt=DateTime.Now;e.Handled=true;stage.Invalidate();}
        else if(e.KeyChar=='\b'){if(indicatorCode.Length>0)indicatorCode=indicatorCode[..^1];e.Handled=true;stage.Invalidate();}
        else if(e.KeyChar=='\r'&&indicatorCode.Length>0){if(Enum.TryParse<TechnicalIndicator>(indicatorCode,out var kind))SelectIndicator(kind);e.Handled=true;}
    }
    private IndicatorBar PriceBar(Candle candle) => new(MusicPrice(candle.Open),MusicPrice(candle.High),MusicPrice(candle.Low),MusicPrice(candle.Close));
    private void RecordIndicatorBar()
    {
        if(timelineCandleCount<1)return;
        indicatorHistory.Add(PriceBar(timelineCandles[timelineCandleCount-1]));
        dayHistory.Add(timelineCandles[timelineCandleCount-1]);
        if(indicatorHistory.Count>DayHistoryLimit)indicatorHistory.RemoveAt(0);
        if(dayHistory.Count>DayHistoryLimit){dayHistory.RemoveAt(0);dayViewport.RemoveOldest();}
    }
    private IndicatorBar[] GetIndicatorBars()
    {
        if(chartMode==ChartMode.MinuteLine)return GetMinuteValuesInOrder().Select(v=>{double price=MusicPrice(v);return new IndicatorBar(price,price,price,price);}).ToArray();
        if(indicatorHistory.Count==0)return timelineCandles.Take(timelineCandleCount).Select(PriceBar).ToArray();
        return indicatorHistory.Append(PriceBar(timelineCandles[Math.Max(0,timelineCandleCount-1)])).ToArray();
    }
    private void DrawIndicatorTabs(Graphics g,Rectangle bounds)
    {
        TerminalFill(g,bounds,TerminalTheme.Toolbar);indicatorTabHits.Clear();int x=bounds.Left+3;
        var active=activeIndicatorPane<0?mainIndicator:subIndicators[activeIndicatorPane];
        indicatorMoreBounds=new Rectangle(bounds.Right-49,bounds.Top,49,bounds.Height);
        foreach(var kind in Enum.GetValues<TechnicalIndicator>())
        {
            int width=kind.ToString().Length*8+16;if(x+width>indicatorMoreBounds.Left)break;
            var tab=new Rectangle(x,bounds.Top,width,bounds.Height);DrawChartTab(g,tab,kind.ToString(),kind==active);indicatorTabHits.Add((tab,kind));x+=width;
        }
        DrawChartTab(g,indicatorMoreBounds,"更多 ▾",false);
        if(indicatorCode.Length>0&&DateTime.Now-indicatorCodeAt<TimeSpan.FromSeconds(4))TerminalText(g,$"键盘精灵  {indicatorCode} ↵",new Rectangle(bounds.Left,bounds.Top,bounds.Width-50,bounds.Height),TerminalTheme.Yellow,9,true,StringAlignment.Far);
    }
    private int IndicatorStart(int count) => chartMode==ChartMode.DayK?dayViewport.Range(count).Start:0;
    private int IndicatorEnd(int count) => chartMode==ChartMode.DayK?dayViewport.Range(count).End:count;
    private float IndicatorX(Rectangle plot,int index,int count)
    {
        int start=IndicatorStart(count);
        return chartMode==ChartMode.DayK?plot.Left+plot.Width*(index-dayViewport.Start+.5f)/dayViewport.Capacity:plot.Left+plot.Width*index/Math.Max(1,minutePointCapacity-1f);
    }
    private void DrawIndicatorLines(Graphics g,Rectangle plot,IndicatorResult result,int count,Func<double,float> y)
    {
        var state=g.Save();g.SetClip(plot);
        for(int line=0;line<result.Lines.Length;line++)
        {
            using var pen=new Pen(IndicatorColors[line%3],1.1f);PointF? previous=null;
            for(int i=IndicatorStart(count);i<IndicatorEnd(count);i++)
            {
                double value=result.Lines[line].Values[i];if(!double.IsFinite(value)){previous=null;continue;}
                var point=new PointF(IndicatorX(plot,i,count),y(value));if(previous is { } p)g.DrawLine(pen,p,point);previous=point;
            }
        }
        g.Restore(state);
    }
    private void DrawIndicatorLegend(Graphics g,Rectangle bounds,TechnicalIndicator kind,IndicatorResult result,int sample)
    {
        var p=Parameters(kind);string periods=string.Join(",",new[]{p.N1,p.N2,p.N3}.Where(n=>n>0));
        if(kind==TechnicalIndicator.BOLL)periods+= $",{p.Factor:0.#}";
        TerminalText(g,$"{kind}({periods})",new Rectangle(bounds.Left,bounds.Top,110,bounds.Height),TerminalTheme.MenuText,7.5f,true);
        TerminalText(g,"▾",new Rectangle(bounds.Left+110,bounds.Top,17,bounds.Height),TerminalTheme.Cyan,8,true,StringAlignment.Center);
        int available=Math.Max(20,bounds.Width-127);int width=available/Math.Max(1,result.Lines.Length+(result.Histogram is null?0:1));
        for(int i=0;i<result.Lines.Length;i++)
        {
            double value=sample>=0?result.Lines[i].Values[sample]:double.NaN;
            TerminalText(g,$"{result.Lines[i].Name}:{(double.IsFinite(value)?value.ToString("0.00"):"--")}",new Rectangle(bounds.Left+127+i*width,bounds.Top,width,bounds.Height),IndicatorColors[i%3],7.2f);
        }
        if(result.Histogram is { } bars)TerminalText(g,$"MACD:{(sample>=0?bars[sample]:0):0.00}",new Rectangle(bounds.Right-width,bounds.Top,width,bounds.Height),TerminalTheme.Cyan,7.2f);
    }
    private void DrawTechnicalPane(Graphics g,Rectangle bounds,TechnicalIndicator kind,IndicatorResult result,int count,int pane)
    {
        using var border=new Pen(pane==activeIndicatorPane?TerminalTheme.Cyan:TerminalTheme.Border);
        g.DrawLine(border,bounds.Left,bounds.Top,bounds.Right,bounds.Top);
        var plot=new Rectangle(bounds.Left+48,bounds.Top+18,bounds.Width-102,Math.Max(2,bounds.Height-21));
        int sample=IndicatorEnd(count)-1;
        bool InIndicatorArea(Point point)=>terminalPlot.Contains(point)||indicatorPaneBounds.Any(p=>p.Contains(point));
        if(terminalPointer is { } pointer&&InIndicatorArea(pointer))sample=IndicatorSample(pointer.X,plot,count);
        DrawIndicatorLegend(g,new Rectangle(bounds.Left+8,bounds.Top,bounds.Width-16,17),kind,result,sample);
        var values=result.Lines.SelectMany(line=>line.Values.Skip(IndicatorStart(count)).Take(IndicatorEnd(count)-IndicatorStart(count))).Where(double.IsFinite).ToList();
        if(result.Histogram is { } histogram)values.AddRange(histogram.Skip(IndicatorStart(count)).Take(IndicatorEnd(count)-IndicatorStart(count)));
        if(values.Count==0){TerminalText(g,"等待足够的采样",plot,TerminalTheme.Muted,7);return;}
        double min=Math.Min(values.Min(),result.Floor??double.MaxValue),max=Math.Max(values.Max(),result.Ceiling??double.MinValue);
        if(kind is TechnicalIndicator.MACD or TechnicalIndicator.BIAS or TechnicalIndicator.ROC){double range=Math.Max(.01,Math.Max(Math.Abs(min),Math.Abs(max)));min=-range;max=range;}
        if(max-min<1e-8){min-=1;max+=1;}
        double padding=(max-min)*.05;min-=padding;max+=padding;
        float Y(double value)=>(float)(plot.Bottom-(value-min)/(max-min)*plot.Height);
        using var grid=new Pen(TerminalTheme.Grid);using var zero=new Pen(TerminalTheme.Muted){DashStyle=DashStyle.Dash};
        g.DrawRectangle(grid,plot);
        for(int i=1;i<5;i++){float x=plot.Left+plot.Width*i/5f;g.DrawLine(grid,x,plot.Top,x,plot.Bottom);}
        if(min<=0&&max>=0)g.DrawLine(zero,plot.Left,Y(0),plot.Right,Y(0));
        if(plot.Height>=25){TerminalText(g,max.ToString("0.0"),new Rectangle(plot.Right+4,plot.Top-3,48,16),TerminalTheme.Muted,6.8f);TerminalText(g,min.ToString("0.0"),new Rectangle(plot.Right+4,plot.Bottom-13,48,16),TerminalTheme.Muted,6.8f);}
        if(result.Histogram is { } hist)
        {
            var clip=g.Save();g.SetClip(plot);float width=Math.Max(1,plot.Width/(float)Math.Max(1,chartMode==ChartMode.DayK?dayViewport.Capacity:minutePointCapacity)*.55f);
            using var positive=new Pen(TerminalTheme.Rise,width);using var negative=new Pen(TerminalTheme.Cyan,width);
            for(int i=IndicatorStart(count);i<IndicatorEnd(count);i++){float x=IndicatorX(plot,i,count);g.DrawLine(hist[i]>=0?positive:negative,x,Y(0),x,Y(hist[i]));}g.Restore(clip);
        }
        DrawIndicatorLines(g,plot,result,count,Y);
        if(terminalPointer is { } cursor&&InIndicatorArea(cursor)&&cursor.X>=plot.Left&&cursor.X<=plot.Right)
            g.DrawLine(zero,cursor.X,plot.Top,cursor.X,plot.Bottom);
    }
    private int IndicatorSample(int x,Rectangle plot,int count)
    {
        if(count==0)return -1;
        int start=IndicatorStart(count);int index=chartMode==ChartMode.DayK?dayViewport.Start+(int)((x-plot.Left)/(float)plot.Width*dayViewport.Capacity):(int)((x-plot.Left)/(float)plot.Width*(minutePointCapacity-1));
        return Math.Clamp(index,start,IndicatorEnd(count)-1);
    }
}
