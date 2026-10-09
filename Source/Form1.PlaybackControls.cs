namespace SpectrumKlinePlayer;

public sealed partial class Form1
{
    private readonly List<(Rectangle Bounds, PlayerCommand Command)> playbackHits = new();
    private Rectangle volumeTrackBounds, muteBounds;
    private bool volumeDragging, playbackBusy, playbackAvailable, playbackPlaying;
    private bool volumeAvailable, playerMuted, volumeReadBusy, volumeWriteBusy;
    private float playerVolume = 0.5f;
    private float? requestedVolume;
    private double nextVolumeRead;
    private string playerFeedback = string.Empty;
    private DateTime feedbackUntil;

    private bool HandlePlaybackClick(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return false;
        foreach (var hit in playbackHits)
        {
            if (!hit.Bounds.Contains(e.Location)) continue;
            if (playbackAvailable && !playbackBusy) RunPlayerCommand(hit.Command);
            return true;
        }
        if (muteBounds.Contains(e.Location))
        {
            if (volumeAvailable && !volumeWriteBusy) TogglePlayerMute();
            return true;
        }
        if (volumeTrackBounds.Contains(e.Location))
        {
            if (volumeAvailable && !volumeWriteBusy)
            {
                volumeDragging = true; stage.Capture = true;
                RequestVolumeAt(e.X);
            }
            return true;
        }
        return false;
    }

    private void OnPlaybackPointerMove(object? sender, MouseEventArgs e)
    {
        if (volumeDragging && e.Button == MouseButtons.Left) RequestVolumeAt(e.X);
    }

    private async void RunPlayerCommand(PlayerCommand command)
    {
        playbackBusy = true;
        try
        {
            bool ok = await WindowsMediaSessionCapture.ControlAsync(command);
            string name = command switch { PlayerCommand.Previous => "上一首", PlayerCommand.Next => "下一首", PlayerCommand.Stop => "停止", _ => "播放 / 暂停" };
            playerFeedback = ok ? $"已发送{name}" : $"播放器未接受{name}";
            feedbackUntil = DateTime.Now.AddSeconds(3);
        }
        finally { playbackBusy = false; if (!stage.IsDisposed) stage.Invalidate(); }
    }

    private async void TogglePlayerMute()
    {
        volumeWriteBusy = true;
        bool muted = !playerMuted;
        string? source = WindowsMediaSessionCapture.ActiveSourceApp;
        try
        {
            bool ok = await Task.Run(() => MusicAudioVolume.Set(source, muted: muted));
            if (ok) playerMuted = muted;
            else { playerFeedback = "未找到播放器音量会话"; feedbackUntil = DateTime.Now.AddSeconds(3); }
        }
        finally { volumeWriteBusy = false; nextVolumeRead = 0; if (!stage.IsDisposed) stage.Invalidate(); }
    }

    private async void RequestVolumeAt(int x)
    {
        float value = Math.Clamp((x - volumeTrackBounds.Left) / (float)Math.Max(1, volumeTrackBounds.Width), 0, 1);
        playerVolume = value; requestedVolume = value;
        stage.Invalidate();
        if (volumeWriteBusy) return;
        volumeWriteBusy = true;
        string? source = WindowsMediaSessionCapture.ActiveSourceApp;
        try
        {
            while (requestedVolume is { } requested && !closing)
            {
                requestedVolume = null;
                bool ok = await Task.Run(() => MusicAudioVolume.Set(source, requested, requested > 0 ? false : null));
                if (ok && requested > 0) playerMuted = false;
                if (!ok) { playerFeedback = "未找到播放器音量会话"; feedbackUntil = DateTime.Now.AddSeconds(3); break; }
            }
        }
        finally { volumeWriteBusy = false; nextVolumeRead = 0; }
    }

    private async void PollPlayerVolume()
    {
        playbackAvailable = WindowsMediaSessionCapture.ActiveSession is not null;
        playbackPlaying = WindowsMediaSessionCapture.IsPlaying;
        if (volumeReadBusy || volumeWriteBusy || volumeDragging || clock.Elapsed.TotalSeconds < nextVolumeRead) return;
        nextVolumeRead = clock.Elapsed.TotalSeconds + 1;
        volumeReadBusy = true;
        string? source = WindowsMediaSessionCapture.ActiveSourceApp;
        try
        {
            var state = await Task.Run(() => MusicAudioVolume.Read(source));
            if (closing || IsDisposed || volumeDragging || volumeWriteBusy || source != WindowsMediaSessionCapture.ActiveSourceApp) return;
            volumeAvailable = state is not null;
            if (state is { } volume) { playerVolume = volume.Level; playerMuted = volume.Muted; }
        }
        finally { volumeReadBusy = false; }
    }

    private void DrawPlaybackHeader(Graphics g, int width)
    {
        TerminalFill(g, new Rectangle(5, 4, width - 5, 84), TerminalTheme.Panel);
        TerminalText(g, "音乐行情", new Rectangle(12, 5, 74, 29), TerminalTheme.Muted, 9, true);
        TerminalText(g, currentTrackTitle, new Rectangle(94, 5, width - 110, 29), TerminalTheme.MenuText, 14, true);
        playbackHits.Clear();
        int x = 12;
        foreach (var entry in new[] { (PlayerCommand.Previous, "上一首", 58), (PlayerCommand.Toggle, playbackPlaying ? "❚❚ 暂停" : "▶ 播放", 72), (PlayerCommand.Stop, "■ 停止", 58), (PlayerCommand.Next, "下一首", 58) })
        {
            var button = new Rectangle(x, 34, entry.Item3, 25);
            DrawPlaybackButton(g, button, entry.Item2, playbackAvailable && !playbackBusy, entry.Item1 == PlayerCommand.Toggle);
            playbackHits.Add((button, entry.Item1)); x += entry.Item3 + 6;
        }
        muteBounds = new Rectangle(x + 4, 34, 52, 25);
        DrawPlaybackButton(g, muteBounds, playerMuted ? "已静音" : "静音", volumeAvailable, playerMuted);
        volumeTrackBounds = new Rectangle(muteBounds.Right + 12, 34, Math.Clamp(width - muteBounds.Right - 74, 75, 180), 25);
        int center = volumeTrackBounds.Top + 12;
        using var background = new Pen(TerminalTheme.Border, 4);
        using var fill = new Pen(volumeAvailable ? TerminalTheme.Cyan : TerminalTheme.Muted, 4);
        g.DrawLine(background, volumeTrackBounds.Left, center, volumeTrackBounds.Right, center);
        int thumbX = volumeTrackBounds.Left + (int)(volumeTrackBounds.Width * playerVolume);
        g.DrawLine(fill, volumeTrackBounds.Left, center, thumbX, center);
        TerminalFill(g, new Rectangle(thumbX - 4, center - 7, 8, 14), volumeAvailable ? TerminalTheme.MenuText : TerminalTheme.Muted);
        TerminalText(g, volumeAvailable ? $"{playerVolume * 100:0}%" : "音量", new Rectangle(volumeTrackBounds.Right + 7, 34, 50, 25), TerminalTheme.MenuText, 9);
        TerminalText(g, DateTime.Now < feedbackUntil ? playerFeedback : !playbackAvailable ? "等待播放器" : "音乐控制 · 上下首 / 播放暂停 / 停止 / 音量",
            new Rectangle(12, 63, width - 28, 20), TerminalTheme.Muted, 8);
        using var border = new Pen(TerminalTheme.Border);
        g.DrawRectangle(border, new Rectangle(5, 4, width - 5, 84));
    }

    private static void DrawPlaybackButton(Graphics g, Rectangle bounds, string text, bool enabled, bool active)
    {
        TerminalFill(g, bounds, enabled && active ? TerminalTheme.Selected : TerminalTheme.Input);
        using var border = new Pen(enabled ? TerminalTheme.Border : TerminalTheme.Grid);
        g.DrawRectangle(border, bounds);
        TerminalText(g, text, bounds, !enabled ? TerminalTheme.Muted : active ? TerminalTheme.Cyan : TerminalTheme.MenuText, 9, true, StringAlignment.Center);
    }
}
