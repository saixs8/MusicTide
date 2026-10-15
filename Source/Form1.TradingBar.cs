using System.IO;

namespace SpectrumKlinePlayer;

public sealed partial class Form1
{
    private const int TradingBarHeight = 76;
    private readonly List<(Rectangle Bounds, string Action)> tradingHits = new();
    private double tradeFraction = .25;
    private string tradingFeedback = "按可用保证金开仓；反向交易需先平仓。";
    private PaperTradingForm? tradingDetails;
    private bool CanTrade => trackIsPlaying && hasAudioSignal && timelineCandleCount > 0
        && currentTrackTitle is not ("等待歌曲" or "未知歌曲");
    private TradeRound CurrentTradeRound() => new(currentTrackTitle, tradeStartedAt, paperTrading.Equity(CurrentMusicPrice),
        paperTrading.Fills.ToArray(), paperTrading.RealizedProfit, paperTrading.UnrealizedProfit(CurrentMusicPrice), paperTrading.Shares, paperTrading.Margin);
    private void PersistActiveTrade() => SavePlayJson(Path.Combine(PlayDataDirectory, "active-trade.json"), CurrentTradeRound());
    private bool SubmitTrade(string action)
    {
        if (!CanTrade) { tradingFeedback = "等待歌曲播放和有效音频后才能成交。"; stage.Invalidate(); return false; }
        bool ok = action switch
        {
            "long" => paperTrading.Buy(CurrentMusicPrice, tradeFraction, GetPlaybackPositionSeconds(), currentLyric),
            "short" => paperTrading.Short(CurrentMusicPrice, tradeFraction, GetPlaybackPositionSeconds(), currentLyric),
            "close" => paperTrading.Close(CurrentMusicPrice, tradeFraction, GetPlaybackPositionSeconds(), currentLyric),
            "flat" => paperTrading.Sell(CurrentMusicPrice, GetPlaybackPositionSeconds(), currentLyric),
            _ => false
        };
        if (ok) { PersistActiveTrade(); tradingFeedback = $"{paperTrading.Fills[^1].Side}成功 · {paperTrading.Fills[^1].Quantity} 股 @ {CurrentMusicPrice:0.00}"; }
        else tradingFeedback = paperTrading.Shares != 0 && (action == "short" && paperTrading.Shares > 0 || action == "long" && paperTrading.Shares < 0)
            ? "当前持有反向仓位，请先平仓。" : "未成交：保证金不足、没有持仓或数量不足。";
        stage.Invalidate(); return ok;
    }
    private void UpdateTradingRisk()
    {
        if (!paperTrading.CheckLiquidation(CurrentMusicPrice, GetPlaybackPositionSeconds(), currentLyric)) return;
        tradingFeedback = "已模拟强平：权益触及占用保证金的 20%。";
        PersistActiveTrade();
    }
    private void DrawTradingBar(Graphics g, Rectangle bounds)
    {
        TerminalFill(g, bounds, TerminalTheme.Panel);
        using var border = new Pen(TerminalTheme.Border); g.DrawLine(border, bounds.Left, bounds.Top, bounds.Right, bounds.Top);
        tradingHits.Clear(); int x = bounds.Left + 8;
        Add("模拟交易", "", 66, TerminalTheme.Cyan);
        Add($"杠杆 {paperTrading.Leverage}× ▾", "leverage", 88, TerminalTheme.MenuText);
        Add($"比例 {tradeFraction:P0} ▾", "fraction", 94, TerminalTheme.MenuText);
        Add("开多", "long", 62, TerminalTheme.Rise);
        Add("开空", "short", 62, TerminalTheme.Fall);
        Add("按比例平仓", "close", 90, TerminalTheme.MenuText);
        Add("全部平仓", "flat", 80, TerminalTheme.MenuText);
        Add("新一局", "reset", 68, TerminalTheme.MenuText);
        Add("盈亏明细", "details", 80, TerminalTheme.Cyan);
        TerminalText(g, tradingFeedback, new Rectangle(x + 4, bounds.Top + 5, Math.Max(0, bounds.Right - x - 8), 29), TerminalTheme.Muted, 8);
        double unrealized = paperTrading.UnrealizedProfit(CurrentMusicPrice), equity = paperTrading.Equity(CurrentMusicPrice);
        string position = paperTrading.Shares == 0 ? "空仓" : $"{(paperTrading.Shares > 0 ? "多" : "空")} {Math.Abs(paperTrading.Shares):N0}股  成本 {paperTrading.Cost:0.00}";
        string summary = $"交易价 {CurrentMusicPrice:0.00}   可用 {paperTrading.Cash:N2}   保证金 {paperTrading.Margin:N2}   权益 {equity:N2}   {position}   已实现 {paperTrading.RealizedProfit:+#,##0.00;-#,##0.00;0.00}   浮盈 {unrealized:+#,##0.00;-#,##0.00;0.00}   本局 {(equity / PaperTrading.StartingCash - 1):+0.00%;-0.00%;0.00%}";
        TerminalText(g, summary, new Rectangle(bounds.Left + 10, bounds.Top + 40, bounds.Width - 20, 27), equity >= PaperTrading.StartingCash ? TerminalTheme.Rise : TerminalTheme.Fall, 8.5f);
        void Add(string text, string action, int width, Color color)
        {
            var rect = new Rectangle(x, bounds.Top + 7, width, 27);
            bool enabled = action is not ("long" or "short" or "close" or "flat") || CanTrade;
            if (action.Length > 0) { TerminalFill(g, rect, TerminalTheme.Toolbar); tradingHits.Add((rect, action)); }
            TerminalText(g, text, rect, enabled ? color : TerminalTheme.Muted, 8.5f, action.Length == 0, StringAlignment.Center);
            x += width + 5;
        }
    }
    private bool HandleTradingClick(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return false;
        foreach (var hit in tradingHits)
        {
            if (!hit.Bounds.Contains(e.Location)) continue;
            if (hit.Action is "leverage" or "fraction")
            {
                if (hit.Action == "leverage" && paperTrading.Shares != 0) { tradingFeedback = "持仓期间杠杆锁定，全部平仓后可修改。"; stage.Invalidate(); return true; }
                var menu = new ContextMenuStrip { BackColor = TerminalTheme.Panel, ForeColor = TerminalTheme.Text };
                if (hit.Action == "leverage") foreach (int multiplier in PaperTrading.LeverageChoices)
                {
                    var item = new ToolStripMenuItem(multiplier + "×") { Checked = multiplier == paperTrading.Leverage };
                    item.Click += (_, _) => { paperTrading.SetLeverage(multiplier); stage.Invalidate(); }; menu.Items.Add(item);
                }
                else foreach (double fraction in new[] { .25, .5, 1 })
                {
                    var item = new ToolStripMenuItem(fraction.ToString("P0")) { Checked = fraction == tradeFraction };
                    item.Click += (_, _) => { tradeFraction = fraction; stage.Invalidate(); }; menu.Items.Add(item);
                }
                menu.Closed += (_, _) => menu.Dispose(); menu.Show(stage, new Point(hit.Bounds.Left, hit.Bounds.Top));
            }
            else if (hit.Action == "details") OpenPaperTrading();
            else if (hit.Action == "reset")
            {
                SaveTradeRound(); paperTrading.Reset(); tradeStartedAt = DateTime.Now;
                PersistActiveTrade(); tradingFeedback = "新一局 · 初始虚拟资金 100,000。"; stage.Invalidate();
            }
            else SubmitTrade(hit.Action);
            return true;
        }
        return false;
    }
}
