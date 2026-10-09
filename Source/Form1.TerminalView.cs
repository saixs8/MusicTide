using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SpectrumKlinePlayer;

public sealed partial class Form1
{
    private ComboBox? chartModeControl;
    private TrackBar? minuteFrequencyControl;
    private TrackBar? frequencyMidpointControl;
    private Point? terminalPointer;
    private Rectangle terminalPlot;
    private Rectangle quoteBounds;
    private Rectangle dayTabBounds;
    private Rectangle minuteTabBounds;
    private readonly List<(Rectangle Bounds, int Band)> quoteRowHits = new();
    private readonly List<(Rectangle Bounds, int Filter)> quoteFilterHits = new();
    private readonly List<(Rectangle Bounds, int Column)> quoteHeaderHits = new();
    private int quoteSortColumn = -1, quoteSortDirection;
    private int quoteFilter;
    private int quoteScroll;
    private int visibleQuoteRows;
    private bool terminalSettingsVisible = false;
    private Rectangle lyricFeedBounds;
    private Rectangle lyricFollowBounds;
    private int lyricFeedScroll;
    private int lyricFeedRows;
    private bool lyricFeedFollow = true;
    private Rectangle lyricCurrentBounds;

    private void SelectTerminalChart(ChartMode mode)
    {
        chartMode = mode;
        if (chartModeControl is not null) chartModeControl.SelectedIndex = mode == ChartMode.DayK ? 0 : 1;
        stage.Invalidate();
    }

    private void OnTerminalMouseDown(object? sender, MouseEventArgs e)
    {
        stage.Focus();
        if (HandlePlaybackClick(e)) return;
        if (indicatorPaneBounds.Any(p => p.Contains(e.Location) && e.Y < p.Top + 20) && HandleIndicatorClick(e)) return;
        if (HandleDayNavigationClick(e)) return;
        if (HandleIndicatorClick(e)) return;
        if (lyricsVisible && lyricFollowBounds.Contains(e.Location))
        {
            lyricFeedFollow = true;
            stage.Invalidate();
            return;
        }
        if (dayTabBounds.Contains(e.Location)) SelectTerminalChart(ChartMode.DayK);
        if (minuteTabBounds.Contains(e.Location)) SelectTerminalChart(ChartMode.MinuteLine);
        foreach (var hit in quoteFilterHits)
        {
            if (!hit.Bounds.Contains(e.Location)) continue;
            quoteFilter = hit.Filter;
            quoteScroll = 0;
            stage.Invalidate();
            return;
        }
        foreach (var hit in quoteHeaderHits)
        {
            if (e.Button != MouseButtons.Left || !hit.Bounds.Contains(e.Location)) continue;
            if (quoteSortColumn != hit.Column) { quoteSortColumn = hit.Column; quoteSortDirection = 1; }
            else { quoteSortDirection = quoteSortDirection == 1 ? -1 : quoteSortDirection == -1 ? 0 : 1; }
            if (quoteSortDirection == 0) quoteSortColumn = -1;
            quoteScroll = 0; stage.Invalidate(); return;
        }
        foreach (var hit in quoteRowHits)
        {
            if (!hit.Bounds.Contains(e.Location)) continue;
            minuteBandIndex = hit.Band;
            if (minuteFrequencyControl is not null) minuteFrequencyControl.Value = hit.Band;
            if (minuteFrequencyLabel is not null)
                minuteFrequencyLabel.Text = $"分时采样频率：{FormatFrequency(BandCenterFrequency(hit.Band))}";
            ResetMinuteLine();
            stage.Invalidate();
            return;
        }
    }

    private static int BandRegion(int band) => BandCenterFrequency(band) < 250 ? 1 : BandCenterFrequency(band) < 2500 ? 2 : 3;

    private static readonly (string Code, string Board)[] BandSecurities = Enumerable.Range(0, BandCount).Select(CreateBandSecurity).ToArray();
    private static (string Code, string Board) BandSecurity(int band) => BandSecurities[band];

    private static (string Code, string Board) CreateBandSecurity(int band)
    {
        int region = BandRegion(band);
        int[] members = Enumerable.Range(0, BandCount).Where(i => BandRegion(i) == region).ToArray();
        int index = Array.IndexOf(members, band);
        int half = (members.Length + 1) / 2;
        return region switch
        {
            1 when index < half => ((600001 + index).ToString("D6"), "沪主板"),
            1 => ((1 + index - half).ToString("D6"), "深主板"),
            2 when index < half => ((300001 + index).ToString("D6"), "创业板"),
            2 => ((688001 + index - half).ToString("D6"), "科创板"),
            _ => ((920001 + index).ToString("D6"), "北交所")
        };
    }

    private int[] QuoteBands()
    {
        var bands = Enumerable.Range(0, BandCount).Where(i => quoteFilter switch
        {
            1 => BandRegion(i) == 1,
            2 => BandSecurity(i).Board == "创业板",
            3 => BandSecurity(i).Board == "科创板",
            4 => BandSecurity(i).Board == "北交所",
            _ => true
        });
        if (quoteSortDirection == 0 || quoteSortColumn < 0) return bands.ToArray();
        double Value(int i) => quoteSortColumn switch
        {
            0 => int.Parse(BandSecurity(i).Code), 1 => BandCenterFrequency(i),
            2 => MusicPrice(candles[i].Close), 3 => Math.Exp(candles[i].Close - candles[i].Open) - 1,
            4 => bandTargets[i], _ => i
        };
        return (quoteSortDirection > 0 ? bands.OrderBy(Value) : bands.OrderByDescending(Value)).ThenBy(i => i).ToArray();
    }

    private void OnTerminalMouseWheel(object? sender, MouseEventArgs e)
    {
        if (HandleDayNavigationWheel(e, sender is StagePanel canvas ? canvas.WheelModifiers : ModifierKeys)) return;
        if (lyricsVisible && lyricFeedBounds.Contains(e.Location))
        {
            int total = lyricTimeline.Length > 0 ? lyricTimeline.Length : liveLyricHistory.Count;
            lyricFeedFollow = false;
            lyricFeedScroll = Math.Clamp(lyricFeedScroll - Math.Sign(e.Delta) * 3, 0, Math.Max(0, total - lyricFeedRows));
            stage.Invalidate();
            return;
        }
        if (!quoteBounds.Contains(e.Location)) return;
        quoteScroll = Math.Clamp(quoteScroll - Math.Sign(e.Delta) * 3,
            0, Math.Max(0, QuoteBands().Length - visibleQuoteRows));
        stage.Invalidate();
    }

    private static void TerminalText(Graphics g, string text, RectangleF bounds, Color color,
        float size = 8.5f, bool bold = false, StringAlignment alignment = StringAlignment.Near)
    {
        using var font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Alignment = alignment, LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap
        };
        g.DrawString(text, font, brush, bounds, format);
    }

    private static void TerminalFill(Graphics g, Rectangle bounds, Color color)
    {
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, bounds);
    }

    private void DrawTerminalStage(Graphics g, Rectangle bounds)
    {
        if (bounds.Width < 600 || bounds.Height < 300) return;
        g.Clear(TerminalTheme.Background);
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var border = new Pen(TerminalTheme.Border);
        int musicWidth = Math.Clamp((int)(bounds.Width * .48), 550, 820);
        DrawPlaybackHeader(g, musicWidth);
        DrawMarketOverview(g, new Rectangle(musicWidth + 5, 4, bounds.Width - musicWidth - 10, 84));
        int lyricHeight = lyricsVisible ? (Math.Clamp((int)(bounds.Height * 0.18), 116, 150)) : 0;
        int mainBottom = bounds.Height - 27 - lyricHeight - 8;
        int tableWidth = Math.Clamp((int)(bounds.Width * 0.30), 300, 390);
        quoteBounds = new Rectangle(5, 94, tableWidth, Math.Max(140, mainBottom - 94));
        var chart = new Rectangle(quoteBounds.Right + 5, 94, bounds.Width - quoteBounds.Right - 10, quoteBounds.Height);
        DrawQuoteTable(g, quoteBounds);
        DrawTerminalChart(g, chart);
        if (lyricsVisible) DrawTerminalLyrics(g, new Rectangle(7, mainBottom + 4, bounds.Width - 14, lyricHeight - 4));
        DrawTerminalTicker(g, new Rectangle(0, bounds.Height - 27, bounds.Width, 27));
    }

    private void DrawQuoteTable(Graphics g, Rectangle bounds)
    {
        TerminalFill(g, bounds, TerminalTheme.Background);
        using var border = new Pen(TerminalTheme.Border);
        using var rowPen = new Pen(TerminalTheme.Grid);
        g.DrawRectangle(border, bounds);
        quoteRowHits.Clear();
        quoteFilterHits.Clear();
        quoteHeaderHits.Clear();
        string[] tabs = ["全部", "主板", "创业板", "科创板", "北交所"];
        int tabX = bounds.Left + 7;
        for (int i = 0; i < tabs.Length; i++)
        {
            var tab = new Rectangle(tabX, bounds.Top + 3, Math.Max(1, (bounds.Width - 26) / tabs.Length), 22);
            if (i == quoteFilter)
            {
                TerminalFill(g, tab, TerminalTheme.Selected);
                using var accent = new Pen(TerminalTheme.Cyan, 2);
                g.DrawLine(accent, tab.Left, tab.Bottom, tab.Right, tab.Bottom);
            }
            TerminalText(g, tabs[i], tab, i == quoteFilter ? TerminalTheme.Cyan : TerminalTheme.Muted,
                8.5f, false, StringAlignment.Center);
            quoteFilterHits.Add((tab, i));
            tabX += tab.Width + 3;
        }
        int top = bounds.Top + 28;
        TerminalFill(g, new Rectangle(bounds.Left + 1, top, bounds.Width - 2, 25), TerminalTheme.Toolbar);
        float[] positions = [0.00f, 0.23f, 0.46f, 0.64f, 0.83f, 1.00f];
        string[] names = ["代码", "频率", "现价", "涨跌幅", "能量"];
        for (int col = 0; col < names.Length; col++)
        {
            var cell = new RectangleF(bounds.Left + positions[col] * bounds.Width + 5, top,
                (positions[col + 1] - positions[col]) * bounds.Width - 10, 25);
            quoteHeaderHits.Add((new Rectangle(bounds.Left + (int)(positions[col] * bounds.Width), top, (int)((positions[col + 1] - positions[col]) * bounds.Width), 25), col));
            string caption = names[col] + (quoteSortColumn == col ? quoteSortDirection > 0 ? "↑" : "↓" : "");
            TerminalText(g, caption, cell, quoteSortColumn == col ? TerminalTheme.Cyan : TerminalTheme.Muted, 8, false,
                col == 0 ? StringAlignment.Near : StringAlignment.Far);
        }
        int[] bands = QuoteBands();
        int rowHeight = 20;
        visibleQuoteRows = Math.Max(1, (bounds.Height - 80) / rowHeight);
        quoteScroll = Math.Clamp(quoteScroll, 0, Math.Max(0, bands.Length - visibleQuoteRows));
        for (int row = 0; row < visibleQuoteRows && row + quoteScroll < bands.Length; row++)
        {
            int band = bands[row + quoteScroll];
            var rowBounds = new Rectangle(bounds.Left + 1, top + 25 + row * rowHeight, bounds.Width - 2, rowHeight);
            if (band == minuteBandIndex) TerminalFill(g, rowBounds, TerminalTheme.Selected);
            else if (row % 2 == 1) TerminalFill(g, rowBounds, Color.FromArgb(29, 31, 36));
            var candle = candles[band];
            float change = candle.Close - candle.Open;
            Color movementColor = Math.Abs(change) < 0.0001 ? TerminalTheme.Text : change > 0 ? TerminalTheme.Rise : TerminalTheme.Fall;
            string[] values = [BandSecurity(band).Code, FormatFrequency(BandCenterFrequency(band)),
                $"{MusicPrice(candle.Close):0.00}", $"{(Math.Exp(candle.Close - candle.Open) - 1) * 100:+0.0;-0.0;0.0}%", $"{bandTargets[band] * 100:0.0}"];
            for (int col = 0; col < values.Length; col++)
            {
                var cell = new RectangleF(bounds.Left + positions[col] * bounds.Width + 5, rowBounds.Top,
                    (positions[col + 1] - positions[col]) * bounds.Width - 10, rowHeight);
                TerminalText(g, values[col], cell, col switch { 0 => TerminalTheme.Muted, 1 => TerminalTheme.Text, 4 => TerminalTheme.Cyan, _ => movementColor },
                    8, false, col == 0 ? StringAlignment.Near : StringAlignment.Far);
            }
            g.DrawLine(rowPen, rowBounds.Left, rowBounds.Bottom, rowBounds.Right, rowBounds.Bottom);
            quoteRowHits.Add((rowBounds, band));
        }
        for (int col = 1; col < positions.Length - 1; col++)
        {
            int x = bounds.Left + (int)(positions[col] * bounds.Width);
            g.DrawLine(rowPen, x, top, x, bounds.Bottom - 28);
        }
        int rises = candles.Count(c => c.Close > c.Open + 0.0001f);
        int falls = candles.Count(c => c.Close < c.Open - 0.0001f);
        int footer = bounds.Bottom - 27;
        g.DrawLine(border, bounds.Left, footer, bounds.Right, footer);
        TerminalText(g, $"涨 {rises}", new Rectangle(bounds.Left + 9, footer, 55, 27), TerminalTheme.Rise, 8);
        TerminalText(g, $"跌 {falls}", new Rectangle(bounds.Left + 64, footer, 55, 27), TerminalTheme.Fall, 8);
        TerminalText(g, $"{quoteScroll + 1}–{Math.Min(quoteScroll + visibleQuoteRows, bands.Length)} / {bands.Length}   滚轮翻页",
            new Rectangle(bounds.Left + 119, footer, bounds.Width - 129, 27), TerminalTheme.Muted, 7.5f, false, StringAlignment.Far);
    }

    private void DrawTerminalChart(Graphics g, Rectangle bounds)
    {
        using var border = new Pen(TerminalTheme.Border);
        TerminalFill(g, bounds, TerminalTheme.Background);
        g.DrawRectangle(border, bounds);
        TerminalFill(g, new Rectangle(bounds.Left + 1, bounds.Top + 1, bounds.Width - 2, 29), TerminalTheme.Panel);
        TerminalText(g, climaxMarket.Board is not null ? climaxMarket.Status : $"{BandSecurity(minuteBandIndex).Code} {BandSecurity(minuteBandIndex).Board} · 采样 {FormatFrequency(BandCenterFrequency(minuteBandIndex))} / 对比 {FormatFrequency(BandCenterFrequency(frequencyMidpointBand))}",
            new Rectangle(bounds.Left + 9, bounds.Top, Math.Max(120, bounds.Width - 220), 30), TerminalTheme.Text, 9, true);
        indicatorCountBounds = chartMode == ChartMode.DayK ? new Rectangle(bounds.Right - 213,bounds.Top+4,72,22) : Rectangle.Empty;
        if (chartMode == ChartMode.DayK) DrawChartTab(g,indicatorCountBounds,$"{subIndicators.Count}副图 ▾",false);
        minuteTabBounds = new Rectangle(bounds.Right - 136, bounds.Top + 4, 54, 22);
        dayTabBounds = new Rectangle(bounds.Right - 77, bounds.Top + 4, 62, 22);
        DrawChartTab(g, minuteTabBounds, "分时", chartMode == ChartMode.MinuteLine);
        DrawChartTab(g, dayTabBounds, "日K", chartMode == ChartMode.DayK);
        var dayBars = GetDayCandles();
        var dayRange = dayViewport.Range(dayBars.Length);
        Candle latest = chartMode == ChartMode.DayK && dayRange.End > 0 ? dayBars[dayRange.End-1] : timelineCandles[Math.Clamp(timelineCandleCount - 1, 0, VisibleTimelineCandleCount - 1)];
        var minutePrices = GetMinuteValuesInOrder().Select(v => MusicPrice(v)).ToArray();
        string[] metrics = chartMode == ChartMode.DayK
            ? [$"开 {MusicPrice(latest.Open):0.00}", $"高 {MusicPrice(latest.High):0.00}", $"低 {MusicPrice(latest.Low):0.00}", $"收 {MusicPrice(latest.Close):0.00}"]
            : [$"开 {songOpeningPrice:0.00}", $"高 {(minutePrices.Length == 0 ? songOpeningPrice : minutePrices.Max()):0.00}", $"低 {(minutePrices.Length == 0 ? songOpeningPrice : minutePrices.Min()):0.00}", $"额 {MarketNumber(virtualAmount)}"];
        bool compactNavigation = chartMode == ChartMode.DayK && bounds.Height < 420;
        float metricWidth = (bounds.Width - (compactNavigation ? 178 : 18)) / 4f;
        for (int i = 0; i < metrics.Length; i++)
            TerminalText(g, metrics[i], new RectangleF(bounds.Left + 9 + i * metricWidth, bounds.Top + 31, metricWidth, 23),
                i == 2 ? TerminalTheme.Fall : TerminalTheme.Rise, 8);
        var bars=GetIndicatorBars();int count=bars.Length;
        var overlay=chartMode == ChartMode.DayK ? TechnicalIndicators.Calculate(mainIndicator,bars,Parameters(mainIndicator)) : new IndicatorResult([]);
        indicatorMainHeader=new Rectangle(bounds.Left+9,bounds.Top+50,bounds.Width-18,22);
        if (chartMode == ChartMode.DayK) DrawIndicatorLegend(g,indicatorMainHeader,mainIndicator,overlay,IndicatorEnd(count)-1);
        else TerminalText(g, "分时 · 白色现价 / 黄色均价 / 模拟成交量", indicatorMainHeader, TerminalTheme.Cyan, 8);
        // Energy is anchored independently of the optional indicator pane count.
        int energyHeight=Math.Clamp(bounds.Height/9,36,80);
        terminalEnergyBounds=new Rectangle(bounds.Left+48,bounds.Bottom-52-energyHeight,bounds.Width-102,energyHeight);
        DrawDayNavigation(g,compactNavigation ? new Rectangle(bounds.Right-164,bounds.Top+31,155,23)
            : new Rectangle(bounds.Left+48,bounds.Top+74,bounds.Width-102,23),count);
        int plotTop=bounds.Top+(chartMode==ChartMode.DayK&&!compactNavigation?101:76);
        int available=terminalEnergyBounds.Top-23-plotTop;
        int paneCount = chartMode == ChartMode.DayK ? subIndicators.Count : 1;
        int panelHeight=paneCount==0?0:Math.Min(86,Math.Max(20,(available-44-22)/paneCount));
        int plotHeight=Math.Max(24,available-panelHeight*paneCount-22);
        terminalPlot = new Rectangle(bounds.Left + 48, plotTop, bounds.Width - 102, plotHeight);
        indicatorPaneBounds.Clear();int panelTop=terminalPlot.Bottom+22;
        for(int i=0; chartMode == ChartMode.DayK && i<subIndicators.Count;i++)
            indicatorPaneBounds.Add(new Rectangle(bounds.Left+1,panelTop+i*panelHeight,bounds.Width-2,panelHeight));
        if(chartMode == ChartMode.DayK && terminalPointer is { } cursor&&(terminalPlot.Contains(cursor)||indicatorPaneBounds.Any(p=>p.Contains(cursor))))
        {
            TerminalFill(g,indicatorMainHeader,TerminalTheme.Background);
            DrawIndicatorLegend(g,indicatorMainHeader,mainIndicator,overlay,IndicatorSample(cursor.X,terminalPlot,count));
        }
        float originalRange=displayRange;
        double reference = chartMode == ChartMode.DayK && dayRange.End > dayRange.Start ? MusicPrice(dayBars[dayRange.Start].Open) : songOpeningPrice;
        var visiblePrices = chartMode == ChartMode.DayK
            ? dayBars.Skip(dayRange.Start).Take(dayRange.End - dayRange.Start).SelectMany(c => new[] { MusicPrice(c.High), MusicPrice(c.Low) })
            : minutePrices.AsEnumerable();
        var overlayPrices = overlay.Lines.SelectMany(l => l.Values.Skip(IndicatorStart(count)).Take(IndicatorEnd(count) - IndicatorStart(count))).Where(p => p > 0 && double.IsFinite(p));
        terminalAxis = chartMode == ChartMode.MinuteLine ? MarketAxis.Centered(visiblePrices.Concat(overlayPrices), reference) : MarketAxis.Fit(visiblePrices.Concat(overlayPrices), reference);
        DrawTerminalGrid(g, terminalPlot);
        var state = g.Save();
        g.SetClip(terminalPlot);
        if (chartMode == ChartMode.DayK)
        {
            DrawDayKCandles(g, terminalPlot);
        }
        else DrawTerminalMinuteLine(g, terminalPlot);
        g.Restore(state);
        DrawIndicatorLines(g,terminalPlot,overlay,count,price=>ValueY(terminalPlot,(float)Math.Log(Math.Max(.0001,price)/songOpeningPrice)));
        if(activeIndicatorPane<0){using var focus=new Pen(TerminalTheme.Cyan);g.DrawLine(focus,terminalPlot.Left,terminalPlot.Top,terminalPlot.Right,terminalPlot.Top);}
        DrawTerminalCrosshair(g, terminalPlot);
        displayRange=originalRange; terminalAxis = null;
        for(int i=0; chartMode == ChartMode.DayK && i<subIndicators.Count;i++)
        {
            DrawTechnicalPane(g,indicatorPaneBounds[i],subIndicators[i],TechnicalIndicators.Calculate(subIndicators[i],bars,Parameters(subIndicators[i])),count,i);
        }
        if (chartMode == ChartMode.MinuteLine) DrawMinuteVolume(g, new Rectangle(terminalPlot.Left, panelTop + 18, terminalPlot.Width, Math.Max(2, panelHeight - 21)));
        DrawTerminalEnergy(g, terminalEnergyBounds);
        if (chartMode == ChartMode.DayK) DrawIndicatorTabs(g,new Rectangle(bounds.Left+1,bounds.Bottom-24,bounds.Width-2,23));
        else { indicatorTabHits.Clear(); indicatorMoreBounds = Rectangle.Empty; TerminalText(g, "分时：现价 · 均价 · 成交量    |    日 K：趋势指标", new Rectangle(bounds.Left+9,bounds.Bottom-24,bounds.Width-18,23), TerminalTheme.Muted, 8); }
    }

    private static void DrawChartTab(Graphics g, Rectangle bounds, string label, bool active)
    {
        if (active) TerminalFill(g, bounds, TerminalTheme.Selected);
        TerminalText(g, label, bounds, active ? TerminalTheme.Cyan : TerminalTheme.Muted, 8.5f, false, StringAlignment.Center);
    }

    private void DrawTerminalGrid(Graphics g, Rectangle plot)
    {
        using var grid = new Pen(TerminalTheme.Grid);
        using var zero = new Pen(Color.FromArgb(94, 102, 114)) { DashStyle = DashStyle.Dash };
        int divisions=plot.Height<100?2:8;
        for (int i = 0; i <= divisions; i++)
        {
            float y = plot.Top + plot.Height * i / divisions;
            var axis = terminalAxis ?? MarketAxis.Fit([songOpeningPrice], songOpeningPrice);
            double price = axis.PriceAt(plot, y);
            g.DrawLine(grid, plot.Left, y, plot.Right, y);
            Color color = price > axis.Reference ? TerminalTheme.Rise : price < axis.Reference ? TerminalTheme.Fall : TerminalTheme.Muted;
            TerminalText(g, price.ToString("0.00"), new RectangleF(plot.Left - 49, y - 10, 44, 20), color, 7.5f, false, StringAlignment.Far);
            TerminalText(g, $"{axis.Percent(price):+0.0;-0.0;0.0}%", new RectangleF(plot.Right + 4, y - 10, 48, 20), color, 7.5f);
        }
        if (chartMode == ChartMode.MinuteLine && terminalAxis is { } minuteAxis)
        {
            float zeroY = minuteAxis.Y(plot, minuteAxis.Reference);
            g.DrawLine(zero, plot.Left, zeroY, plot.Right, zeroY);
        }
        var range = chartMode == ChartMode.DayK ? dayViewport.Range(GetIndicatorBars().Length) : (Start: 0, End: 0);
        for (int i = 0; i <= 5; i++)
        {
            float x = plot.Left + plot.Width * i / 5f;
            g.DrawLine(grid, x, plot.Top, x, plot.Bottom);
            int bar = dayViewport.Start + (int)Math.Round(Math.Max(0,dayViewport.Capacity-1)*i/5d);
            string label = chartMode == ChartMode.DayK ? bar<range.Start||bar>=range.End ? "--" : $"第{bar+1}根"
                : FormatSongTime(songDurationSeconds * i / 5);
            TerminalText(g, label, new RectangleF(x - 26, plot.Bottom + 4, 52, 22), TerminalTheme.Muted, 7.5f, false, StringAlignment.Center);
        }
        g.DrawRectangle(grid, plot);
    }

    private void DrawTerminalMinuteLine(Graphics g, Rectangle plot)
    {
        float[] values = GetMinuteValuesInOrder().ToArray();
        if (values.Length < 2)
        {
            TerminalText(g, "等待音频采样", new Rectangle(plot.Left + 9, plot.Top + 6, 150, 24), TerminalTheme.Muted);
            return;
        }
        using var line = new Pen(Color.White, 1.4f) { LineJoin = LineJoin.Round };
        using var averagePen = new Pen(TerminalTheme.Yellow, 1);
        var points = new PointF[values.Length];
        var averages = new PointF[values.Length];
        double weighted = 0, weightSum = 0, priceSum = 0;
        var volumes = MinuteVolumes();
        for (int i = 0; i < values.Length; i++)
        {
            float x = plot.Left + plot.Width * i / Math.Max(1, minutePointCapacity - 1f);
            points[i] = new PointF(x, ValueY(plot, values[i]));
            double price = MusicPrice(values[i]); priceSum += price;
            double weight = volumes[i]; weighted += price * weight; weightSum += weight;
            double mean = weightSum > 0 ? weighted / weightSum : priceSum / (i + 1);
            averages[i] = new PointF(x, ValueY(plot, (float)Math.Log(mean / songOpeningPrice)));
        }
        g.DrawLines(line, points);
        g.DrawLines(averagePen, averages);
    }

    private void DrawTerminalEnergy(Graphics g, Rectangle plot)
    {
        using var grid = new Pen(TerminalTheme.Grid);
        TerminalText(g, "频段能量", new Rectangle(plot.Left, plot.Top - 20, 92, 20), TerminalTheme.Cyan, 8);
        TerminalText(g, $"{BandCount} 个对数频段  ·  幅度 %", new Rectangle(plot.Left + 95, plot.Top - 20, plot.Width - 95, 20), TerminalTheme.Muted, 7.5f);
        for (int i = 0; i <= 4; i++)
        {
            float y = plot.Top + plot.Height * i / 4f;
            g.DrawLine(grid, plot.Left, y, plot.Right, y);
        }
        float lane = plot.Width / (float)BandCount;
        using var brush = new SolidBrush(TerminalTheme.Cyan);
        for (int i = 0; i < BandCount; i++)
        {
            brush.Color = i == minuteBandIndex ? TerminalTheme.Yellow
                : candles[i].Close >= candles[i].Open ? TerminalTheme.Rise : TerminalTheme.Cyan;
            float height = Math.Max(1, Math.Clamp(energyLevels[i], 0, 1) * plot.Height);
            g.FillRectangle(brush, plot.Left + lane * i + 0.5f, plot.Bottom - height, Math.Max(1, lane - 1.5f), height);
        }
        g.DrawRectangle(grid, plot);
        string[] labels = ["40Hz", "100Hz", "250Hz", "630Hz", "1.6k", "4k", "10k", "16k"];
        for (int i = 0; i < labels.Length; i++)
        {
            float x = plot.Left + plot.Width * i / (labels.Length - 1f);
            TerminalText(g, labels[i], new RectangleF(x - 24, plot.Bottom + 3, 48, 20), TerminalTheme.Muted, 7.5f, false, StringAlignment.Center);
        }
    }

    private void DrawTerminalCrosshair(Graphics g, Rectangle plot)
    {
        if (terminalPointer is not { } pointer || pointer.X<plot.Left || pointer.X>plot.Right
            || !plot.Contains(pointer)&&!indicatorPaneBounds.Any(p=>p.Contains(pointer))) return;
        using var pen = new Pen(Color.FromArgb(150, TerminalTheme.Muted)) { DashStyle = DashStyle.Dash };
        g.DrawLine(pen, pointer.X, plot.Top, pointer.X, plot.Bottom);
        if(!plot.Contains(pointer))return;
        g.DrawLine(pen, plot.Left, pointer.Y, plot.Right, pointer.Y);
        var axis = terminalAxis ?? MarketAxis.Fit([songOpeningPrice], songOpeningPrice);
        double price = axis.PriceAt(plot, pointer.Y);
        var label = new Rectangle(plot.Right + 2, pointer.Y - 11, 51, 22);
        TerminalFill(g, label, TerminalTheme.Selected);
        TerminalText(g, $"{axis.Percent(price):+0.0;-0.0;0.0}%", label, TerminalTheme.Text, 7.5f, false, StringAlignment.Center);
        var priceLabel = new Rectangle(plot.Left - 49, pointer.Y - 11, 47, 22);
        TerminalFill(g, priceLabel, TerminalTheme.Selected);
        TerminalText(g, price.ToString("0.00"), priceLabel, TerminalTheme.Text, 7.5f, false, StringAlignment.Center);
    }

    private void DrawTerminalLyrics(Graphics g, Rectangle bounds)
    {
        lyricFeedBounds = bounds;
        TerminalFill(g, bounds, TerminalTheme.Panel);
        using var border = new Pen(TerminalTheme.Border);
        g.DrawRectangle(border, bounds);
        TerminalText(g, "歌词资讯", new Rectangle(bounds.Left + 12, bounds.Top + 2, 85, 25), TerminalTheme.Cyan, 8.5f, true);
        string sync = !autoCaptureLyrics ? "已暂停捕捉" : lyricTimingStatus;
        TerminalText(g, sync, new Rectangle(bounds.Left + 105, bounds.Top + 2, bounds.Width - 290, 25), TerminalTheme.Muted, 8);
        lyricFollowBounds = new Rectangle(bounds.Right - 160, bounds.Top + 3, 150, 23);
        TerminalFill(g, lyricFollowBounds, lyricFeedFollow ? TerminalTheme.Selected : TerminalTheme.Toolbar);
        TerminalText(g, lyricFeedFollow ? "自动跟随 · 滚轮查看" : "点击恢复自动跟随", lyricFollowBounds, TerminalTheme.Cyan, 8, false, StringAlignment.Center);
        IReadOnlyList<TimedLyricLine> lines = lyricTimeline.Length > 0 ? lyricTimeline : liveLyricHistory;
        int active = lyricTimeline.Length > 0 ? activeLyricIndex : liveLyricHistory.Count - 1;
        lyricCurrentBounds = new Rectangle(bounds.Left + 1, bounds.Top + 27, bounds.Width - 2, 44);
        TerminalFill(g, lyricCurrentBounds, TerminalTheme.Selected);
        TerminalFill(g, new Rectangle(lyricCurrentBounds.Left, lyricCurrentBounds.Top, 4, lyricCurrentBounds.Height), TerminalTheme.Yellow);
        TerminalText(g, "▶ 正在唱", new Rectangle(bounds.Left + 12, lyricCurrentBounds.Top + 1, 110, 20), TerminalTheme.Yellow, 9, true);
        string currentTime = active >= 0 && active < lines.Count ? FormatSongTime(lines[active].StartSeconds) : "等待首句";
        TerminalText(g, currentTime, new Rectangle(bounds.Left + 12, lyricCurrentBounds.Top + 22, 110, 22), TerminalTheme.Cyan, 10);
        string pinnedText = active >= 0 && active < lines.Count ? lines[active].Text : currentLyric;
        TerminalText(g, pinnedText, new Rectangle(bounds.Left + 136, lyricCurrentBounds.Top + 1, bounds.Width - 154, 42),
            Color.FromArgb(242, 252, 255), bounds.Width >= 1000 ? 18 : 16, true);
        int top = bounds.Top + 74;
        TerminalFill(g, new Rectangle(bounds.Left + 1, top, bounds.Width - 2, 22), TerminalTheme.Toolbar);
        TerminalText(g, "时间", new Rectangle(bounds.Left + 12, top, 68, 22), TerminalTheme.Muted, 8);
        TerminalText(g, "状态", new Rectangle(bounds.Left + 85, top, 68, 22), TerminalTheme.Muted, 8);
        TerminalText(g, "歌词内容", new Rectangle(bounds.Left + 157, top, bounds.Width - 180, 22), TerminalTheme.Muted, 8);
        var orderedFeed = lines.Select((line, index) => (Line: line, Index: index)).Where(item => item.Index != active).ToArray();
        lyricFeedRows = Math.Max(1, (bounds.Height - 96) / 20);
        if (lyricFeedFollow) lyricFeedScroll = Math.Max(0, active);
        lyricFeedScroll = Math.Clamp(lyricFeedScroll, 0, Math.Max(0, orderedFeed.Length - lyricFeedRows));
        for (int row = 0; row < lyricFeedRows && row + lyricFeedScroll < orderedFeed.Length; row++)
        {
            var entry = orderedFeed[row + lyricFeedScroll];
            int index = entry.Index;
            var line = entry.Line;
            var rowBounds = new Rectangle(bounds.Left + 1, top + 22 + row * 20, bounds.Width - 2, 20);
            if (row % 2 == 1) TerminalFill(g, rowBounds, TerminalTheme.Background);
            TerminalText(g, FormatSongTime(line.StartSeconds), new Rectangle(bounds.Left + 12, rowBounds.Top, 68, 20), TerminalTheme.Muted, 8);
            TerminalText(g, index < active ? "已播" : "待播", new Rectangle(bounds.Left + 85, rowBounds.Top, 68, 20), TerminalTheme.Muted, 8);
            TerminalText(g, line.Text, new Rectangle(bounds.Left + 157, rowBounds.Top, bounds.Width - 175, 20), TerminalTheme.Muted, 9);
        }
    }

    private void DrawTerminalTicker(Graphics g, Rectangle bounds)
    {
        TerminalFill(g, bounds, TerminalTheme.Toolbar);
        TerminalText(g, $"低频 {AverageBandLevel(0, BassBandEnd) * 100:0.0}%", new Rectangle(10, bounds.Top, 125, 27), TerminalTheme.Rise, 8);
        TerminalText(g, $"中频 {AverageBandLevel(BassBandEnd, VoiceBandEnd) * 100:0.0}%", new Rectangle(137, bounds.Top, 125, 27), TerminalTheme.Cyan, 8);
        TerminalText(g, $"高频 {AverageBandLevel(VoiceBandEnd, BandCount) * 100:0.0}%", new Rectangle(264, bounds.Top, 125, 27), TerminalTheme.Fall, 8);
        TerminalText(g, $"K线周期 {candleDurationSeconds:0.00}s", new Rectangle(391, bounds.Top, 130, 27), TerminalTheme.Yellow, 8);
        string notice = playground.CurrentNotification(GetPlaybackPositionSeconds());
        if (clock.Elapsed.TotalSeconds < achievementNoticeUntil) notice = achievementNotice;
        string hint = notice.Length > 0 ? notice : playground.Settings.BlindBox ? playground.BoxStatus
            : playground.Settings.BullBear ? "多空拉锯 · 低频多 / 高频空" : "点击频段切换采样  ·  F11 全屏";
        TerminalText(g, hint, new Rectangle(525, bounds.Top, Math.Max(30, bounds.Width - 670), 27), notice.Length > 0 ? TerminalTheme.Yellow : TerminalTheme.Muted, 9);
        TerminalText(g, DateTime.Now.ToString("HH:mm:ss"), new Rectangle(bounds.Right - 107, bounds.Top, 94, 27), TerminalTheme.Text, 8, false, StringAlignment.Far);
    }
}
