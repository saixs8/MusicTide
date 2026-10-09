namespace SpectrumKlinePlayer;

public sealed partial class Form1
{
    private const int DayHistoryLimit = 20000;
    private readonly List<Candle> dayHistory = new();
    private readonly DayKViewport dayViewport = new();
    private Rectangle dayZoomInBounds, dayZoomOutBounds, dayLatestBounds;
    private bool dayDragging;
    private int dayDragX, dayDragStart, dayDragLength;
    private Candle[] GetDayCandles() => dayHistory.Count == 0 ? timelineCandles.Take(timelineCandleCount).ToArray()
        : dayHistory.Append(timelineCandles[Math.Max(0,timelineCandleCount-1)]).ToArray();
    private bool DayChartArea(Point point) => terminalPlot.Contains(point) || indicatorPaneBounds.Any(p=>p.Contains(point));
    private bool HandleDayNavigationClick(MouseEventArgs e)
    {
        if(chartMode!=ChartMode.DayK || e.Button!=MouseButtons.Left)return false;
        int total=GetIndicatorBars().Length;
        if(dayZoomInBounds.Contains(e.Location)) { dayViewport.Zoom(1,total); stage.Invalidate(); return true; }
        if(dayZoomOutBounds.Contains(e.Location)) { dayViewport.Zoom(-1,total); stage.Invalidate(); return true; }
        if (e.Clicks > 1 && terminalPlot.Contains(e.Location))
        {
            EndDayNavigation();
            int index = dayViewport.Start + (int)((e.X - terminalPlot.Left) * dayViewport.Capacity / (double)Math.Max(1, terminalPlot.Width));
            var bars = GetDayCandles();
            if (index >= 0 && index < bars.Length) OpenCandleDetail(index);
            return true;
        }
        if(dayLatestBounds.Contains(e.Location)) { dayViewport.Latest(total); stage.Invalidate(); return true; }
        if(DayChartArea(e.Location))
        {
            dayViewport.Range(total);dayDragX=e.X;dayDragStart=dayViewport.Start;dayDragLength=dayViewport.Capacity;
            dayDragging=true;stage.Capture=true;stage.Cursor=Cursors.SizeWE;
            return true;
        }
        return false;
    }
    private void OnDayNavigationMove(object? sender,MouseEventArgs e)
    {
        if(!dayDragging)return;
        int shift=(int)Math.Round((dayDragX-e.X)*dayDragLength/(double)Math.Max(1,terminalPlot.Width));
        dayViewport.MoveTo(dayDragStart+shift,GetIndicatorBars().Length);stage.Invalidate();
    }
    private void EndDayNavigation()
    { dayDragging=false;stage.Cursor=Cursors.Default; if(stage.Capture)stage.Capture=false; }
    private bool HandleDayNavigationWheel(MouseEventArgs e, Keys modifiers)
    {
        if(chartMode!=ChartMode.DayK || !DayChartArea(e.Location))return false;
        if(e.Delta==0)return true;
        int total=GetIndicatorBars().Length;
        if((modifiers&Keys.Control)!=0)
            dayViewport.Zoom(Math.Sign(e.Delta),total,(e.X-terminalPlot.Left)/(double)Math.Max(1,terminalPlot.Width));
        else if((modifiers&Keys.Shift)!=0)
            dayViewport.Pan(-Math.Sign(e.Delta)*Math.Max(1,dayViewport.Capacity/5),total);
        else return true;
        stage.Invalidate();return true;
    }
    private bool HandleDayNavigationKey(KeyEventArgs e)
    {
        if(!stage.ContainsFocus||chartMode!=ChartMode.DayK)return false;
        int total=GetIndicatorBars().Length;
        if(e.KeyCode==Keys.Left)dayViewport.Pan(-Math.Max(1,dayViewport.Capacity/5),total);
        else if(e.KeyCode==Keys.Right)dayViewport.Pan(Math.Max(1,dayViewport.Capacity/5),total);
        else if(e.KeyCode==Keys.Home)dayViewport.MoveTo(0,total);
        else if(e.KeyCode==Keys.End)dayViewport.Latest(total);
        else if(e.KeyCode is Keys.Add or Keys.Oemplus)dayViewport.Zoom(1,total);
        else if(e.KeyCode is Keys.Subtract or Keys.OemMinus)dayViewport.Zoom(-1,total);
        else return false;
        e.Handled=true;e.SuppressKeyPress=true;stage.Invalidate();return true;
    }
    private void DrawDayNavigation(Graphics g,Rectangle bounds,int total)
    {
        dayZoomOutBounds=dayZoomInBounds=dayLatestBounds=Rectangle.Empty;
        if(chartMode!=ChartMode.DayK)return;
        var range=dayViewport.Range(total);
        dayLatestBounds=new(bounds.Right-82,bounds.Top,82,bounds.Height);
        dayZoomInBounds=new(bounds.Right-119,bounds.Top,32,bounds.Height);
        dayZoomOutBounds=new(bounds.Right-156,bounds.Top,32,bounds.Height);
        TerminalFill(g,bounds,TerminalTheme.Panel);
        if(bounds.Width>170) TerminalText(g,$"{(dayViewport.Following?"跟随":"历史")} {(total==0?0:range.Start+1)}–{range.End}/{total} · Ctrl+滚轮缩放 / 左右拖动",new Rectangle(bounds.Left,bounds.Top,bounds.Width-160,bounds.Height),TerminalTheme.Muted,7.5f);
        DrawChartTab(g,dayZoomOutBounds,"−",false);DrawChartTab(g,dayZoomInBounds,"＋",false);
        DrawChartTab(g,dayLatestBounds,"回到最新",dayViewport.Following);
    }
}
