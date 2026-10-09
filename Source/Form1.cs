namespace SpectrumKlinePlayer;

using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

public sealed partial class Form1 : Form
{
    private const int BandCount = 100;
    private const int BassBandEnd = BandCount * 25 / 80;
    private const int VoiceBandEnd = BandCount * 55 / 80;
    private const float MaxMove = 0.20f;
    private const float CloseQuantum = 0.0025f;
    private const float MinDisplayRange = 0.025f;
    private const int MinutePointCount = 360;
    private const int VisibleTimelineCandleCount = 50;

    private readonly StagePanel stage = new();
    private readonly System.Windows.Forms.Timer frameTimer = new();
    private readonly object audioLock = new();
    private readonly float[] audioRing = new float[65536];
    private readonly Complex[] fft = new Complex[4096];
    private readonly Candle[] candles = new Candle[BandCount];
    private readonly Candle[] timelineCandles = new Candle[VisibleTimelineCandleCount];
    private readonly float[] bandLevels = new float[BandCount];
    private readonly float[] bandTargets = new float[BandCount];
    private readonly float[] energyTargets = new float[BandCount];
    private readonly float[] energyLevels = new float[BandCount];
    private long lastAudioPacketAt;
    private double lastSpectrumAt;
    private readonly float[] rawBandEnergy = new float[BandCount];
    private readonly float[] bandEnergyFloor = new float[BandCount];
    private readonly float[] bandEnergyPeak = new float[BandCount];
    private readonly float[] periodOpen = new float[BandCount];
    private readonly float[] periodHigh = new float[BandCount];
    private readonly float[] periodLow = new float[BandCount];
    private readonly float[] periodStartLevel = new float[BandCount];
    private readonly float[] periodMinLevel = new float[BandCount];
    private readonly float[] periodMaxLevel = new float[BandCount];
    private readonly float[] periodDeltaSum = new float[BandCount];
    private readonly float[] periodLastLevel = new float[BandCount];
    private readonly int[] periodSamples = new int[BandCount];
    private readonly CandlePattern[] periodPatterns = new CandlePattern[BandCount];
    private readonly float[] minuteValues = new float[MinutePointCount];
    private readonly Random random = new(9307);
    private readonly List<LyricParticle> particles = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource lyricCaptureCts = new();

    private AudioLoopbackCapture? capture;
    private Task? lyricCaptureTask;
    private Panel? controlPanel;
    private MenuStrip? menuBar;
    private ToolStripMenuItem? trendModeMenu;
    private Label? statusLabel;
    private Label? trackStatusLabel;
    private Label? speedValueLabel;
    private Label? lyricSourceLabel;
    private Label? minuteFrequencyLabel;
    private Label? frequencyMidpointLabel;
    private int ringWrite;
    private int capturedSampleRate = 48000;
    private long audioBufferCount;
    private long nonSilentBufferCount;
    private float latestAudioRms;
    private string captureStatus = "正在连接系统音频";
    private double nextAudioStatusAt;
    private int candleCycleSeed;
    private int minuteBandIndex = 24;
    private int frequencyMidpointBand = BandCount / 2;
    private int minutePointTotal;
    private int minutePointCapacity = MinutePointCount;
    private double candlePeriodStart;
    private float candleDurationSeconds = 1.2f;
    private float displayRange = MaxMove;
    private int timelineCandleCount;
    private double dayKCandleStart;
    private double songDurationSeconds = 180;
    private double songPositionSeconds;
    private double lastTrackPositionSeconds = -1;
    private float timelineClose;
    private float timelineLastSourceLevel = 0.5f;
    private float timelineLastSelectedLevel = 0.5f;
    private int timelineSourceBand = -1;
    private bool timelineSelectedLevelInitialized;
    private bool timelineInitialized;
    private string currentTrackKey = string.Empty;
    private string currentTrackTitle = "等待歌曲";
    private string trackStatus = "歌曲时长未识别";
    private float minuteLineValue;
    private float minuteLastBandLevel;
    private double nextMinuteSampleAt;
    private double nextMinuteShockAt;
    private ChartMode chartMode = ChartMode.DayK;
    private TrendMode trendMode = TrendMode.Normal;
    private bool liveAudio;
    private bool simulationMode = false;
    private bool hasAudioSignal;
    private double lastMarketFrameAt;
    private bool lyricsVisible = true;
    private bool autoCaptureLyrics = true;
    private bool candlePeriodInitialized;
    private bool fullscreen;
    private bool closing;
    private Rectangle oldBounds;
    private FormBorderStyle oldBorderStyle;
    private string currentLyric = "等待自动捕捉歌词";
    private readonly LyricPlaybackClock lyricClock = new();
    private TimedLyricLine[] lyricTimeline = [];
    private int activeLyricIndex = -1;
    private string lyricTimingStatus = "等待播放器";
    private readonly List<TimedLyricLine> liveLyricHistory = new();

    public Form1()
    {
        Text = $"音潮行情 · 音乐 K 线终端 v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";
        using (var iconStream = typeof(Form1).Assembly.GetManifestResourceStream("SpectrumKlinePlayer.AppIcon.ico"))
        {
            if (iconStream is not null)
            {
                using var appIcon = new Icon(iconStream);
                Icon = (Icon)appIcon.Clone();
            }
        }
        MinimumSize = new Size(1180, 720);
        Size = new Size(1560, 900);
        BackColor = TerminalTheme.Background;
        ForeColor = TerminalTheme.Text;
        Font = new Font("Microsoft YaHei UI", 9);
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterScreen;

        for (int i = 0; i < candles.Length; i++)
        {
            candles[i] = new Candle { Open = 0, Close = 0, High = 0, Low = 0, Pattern = CandlePattern.Flat };
        }

        LoadSongHeats();
        ResetSongChart(180, "等待歌曲");
        LoadIndicatorSettings();
        LoadClimaxSettings();
        LoadPlaygroundData();

        controlPanel = BuildControls();
        controlPanel.Visible = terminalSettingsVisible;
        SetTrackStatus(trackStatus);
        menuBar = BuildMenuBar();
        Controls.Add(stage);
        Controls.Add(controlPanel);
        Controls.Add(menuBar);

        stage.Dock = DockStyle.Fill;
        stage.Render = DrawStage;
        stage.MouseDown += OnTerminalMouseDown;
        stage.MouseMove += (_, e) => { terminalPointer = e.Location; stage.Invalidate(); };
        stage.MouseLeave += (_, _) => { terminalPointer = null; stage.Invalidate(); };
        stage.MouseWheel += OnTerminalMouseWheel;
        stage.MouseMove += OnDayNavigationMove;
        stage.MouseUp += (_, _) => EndDayNavigation();
        stage.MouseCaptureChanged += (_, _) => { if (!stage.Capture) EndDayNavigation(); };
        stage.PreviewKeyDown += (_, e) => { if (e.KeyCode is Keys.Left or Keys.Right or Keys.Home or Keys.End) e.IsInputKey = true; };
        stage.TabStop = true;
        stage.MouseMove += OnPlaybackPointerMove;
        stage.MouseUp += (_, _) => { volumeDragging = false; stage.Capture = false; };

        frameTimer.Interval = 30;
        frameTimer.Tick += (_, _) => OnFrame();
        frameTimer.Start();

        KeyDown += OnKeyDown;
        KeyPress += OnIndicatorKeyPress;
        FormClosing += OnFormClosing;
        TryStartLoopbackCapture();
        StartLyricCaptureLoop();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (closing)
        {
            return;
        }

        closing = true;
        SaveCurrentRecording();
        SaveTradeRound();
        frameTimer.Stop();
        lyricCaptureCts.Cancel();
        capture?.Dispose();
        capture = null;

        if (lyricCaptureTask is { } task && !task.IsCompleted)
        {
            try
            {
                task.Wait(500);
            }
            catch (AggregateException)
            {
            }
        }
    }

    private Panel BuildControls()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Right,
            Width = 256,
            Padding = new Padding(12, 10, 12, 10),
            BackColor = TerminalTheme.Panel,
            AutoScroll = true
        };

        var title = MakeLabel("  终端设置", 10, true);
        title.ForeColor = TerminalTheme.Cyan;
        statusLabel = MakeLabel("音频：等待系统音频", 9, false);
        statusLabel.Name = "status";
        trackStatusLabel = MakeLabel("曲目：等待识别", 8, false);
        trackStatusLabel.Name = "trackStatus";
        trackStatusLabel.ForeColor = TerminalTheme.Muted;
        trackStatusLabel.Height = 44;

        var audioButton = MakeButton("采集电脑音频");
        audioButton.Click += (_, _) =>
        {
            simulationMode = false;
            TryStartLoopbackCapture();
            statusLabel.Text = liveAudio ? "音频：系统回放采集中" : "音频：等待或不可用";
        };

        var simButton = MakeButton("使用模拟频谱");
        simButton.Click += (_, _) =>
        {
            simulationMode = true;
            statusLabel.Text = "音频：模拟数据";
        };

        var chartModeLabel = MakeLabel("K线类型", 9, true);
        var chartModeBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Height = 34,
            Dock = DockStyle.Top,
            BackColor = TerminalTheme.Input,
            ForeColor = TerminalTheme.Text,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold)
        };
        chartModeBox.Items.AddRange(["日K", "分时图"]);
        chartModeBox.SelectedIndex = 0;
        chartModeControl = chartModeBox;
        chartModeBox.SelectedIndexChanged += (_, _) =>
        {
            chartMode = chartModeBox.SelectedIndex == 1 ? ChartMode.MinuteLine : ChartMode.DayK;
            displayRange = MinDisplayRange;
            stage.Invalidate();
        };

        minuteFrequencyLabel = MakeLabel($"分时采样频率：{FormatFrequency(BandCenterFrequency(minuteBandIndex))}", 9, false);
        var minuteFrequencySlider = new TrackBar
        {
            Minimum = 0,
            Maximum = BandCount - 1,
            TickFrequency = 8,
            Value = minuteBandIndex,
            Height = 42,
            Dock = DockStyle.Top
        };
        minuteFrequencyControl = minuteFrequencySlider;
        minuteFrequencySlider.Scroll += (_, _) =>
        {
            minuteBandIndex = minuteFrequencySlider.Value;
            minuteFrequencyLabel.Text = $"分时采样频率：{FormatFrequency(BandCenterFrequency(minuteBandIndex))}";
            ResetMinuteLine();
        };

        frequencyMidpointLabel = MakeLabel($"对比频率：{FormatFrequency(BandCenterFrequency(frequencyMidpointBand))}", 9, false);
        var frequencyMidpointSlider = new TrackBar
        {
            Minimum = 0,
            Maximum = BandCount - 1,
            TickFrequency = 8,
            Value = frequencyMidpointBand,
            Height = 42,
            Dock = DockStyle.Top
        };
        frequencyMidpointControl = frequencyMidpointSlider;
        frequencyMidpointSlider.Scroll += (_, _) =>
        {
            frequencyMidpointBand = frequencyMidpointSlider.Value;
            frequencyMidpointLabel.Text = $"对比频率：{FormatFrequency(BandCenterFrequency(frequencyMidpointBand))}";
            ResetMinuteLine();
        };

        speedValueLabel = MakeLabel($"K线变化周期：{candleDurationSeconds:0.00} 秒", 9, false);
        var speedSlider = new TrackBar
        {
            Minimum = 30,
            Maximum = 400,
            TickFrequency = 50,
            Value = (int)(candleDurationSeconds * 100),
            Height = 42,
            Dock = DockStyle.Top
        };
        speedSlider.Scroll += (_, _) =>
        {
            candleDurationSeconds = Math.Clamp(speedSlider.Value / 100f, 0.3f, 4.0f);
            speedValueLabel.Text = $"K线变化周期：{candleDurationSeconds:0.00} 秒";
        };

        var lyricToggle = MakeButton("显示/隐藏歌词");
        lyricToggle.Click += (_, _) => lyricsVisible = !lyricsVisible;

        var autoLyricToggle = MakeButton("自动捕捉电脑歌词：开");
        autoLyricToggle.Click += (_, _) =>
        {
            autoCaptureLyrics = !autoCaptureLyrics;
            autoLyricToggle.Text = autoCaptureLyrics ? "自动捕捉电脑歌词：开" : "自动捕捉电脑歌词：关";
            lyricSourceLabel!.Text = autoCaptureLyrics ? "歌词来源：正在同步播放器与本地歌词" : "歌词捕捉已暂停";
        };

        lyricSourceLabel = MakeLabel("歌词来源：正在同步播放器与本地歌词", 8, false);
        lyricSourceLabel.ForeColor = TerminalTheme.Muted;
        lyricSourceLabel.Height = 58;

        var fullscreenButton = MakeButton("全屏到副屏");
        fullscreenButton.Click += (_, _) => ToggleFullscreen();

        var notes = MakeLabel("F11 全屏 | Esc 退出全屏\n把窗口拖到 USB/HDMI 副屏后再全屏测试。", 8, false);
        notes.ForeColor = TerminalTheme.Muted;

        Control[] orderedControls =
        {
            title, statusLabel, trackStatusLabel, audioButton, simButton, chartModeLabel, chartModeBox,
            minuteFrequencyLabel, minuteFrequencySlider, frequencyMidpointLabel, frequencyMidpointSlider,
            speedValueLabel, speedSlider,
            lyricToggle, autoLyricToggle, lyricSourceLabel, fullscreenButton, notes
        };

        for (int i = orderedControls.Length - 1; i >= 0; i--)
        {
            var control = orderedControls[i];
            control.Dock = DockStyle.Top;
            control.Margin = new Padding(0, 0, 0, 10);
            panel.Controls.Add(control);
        }

        return panel;
    }

    private MenuStrip BuildMenuBar()
    {
        var menu = new MenuStrip
        {
            Dock = DockStyle.Top,
            BackColor = TerminalTheme.Toolbar,
            ForeColor = TerminalTheme.Text,
            Padding = new Padding(4, 1, 4, 1),
            Renderer = new TerminalMenuRenderer(),
            Font = new Font("Microsoft YaHei UI", playground.Settings.MenuSize, FontStyle.Bold),
            CanOverflow = true
        };

        trendModeMenu = new ToolStripMenuItem("涨跌模式");
        foreach ((TrendMode mode, string text) in new[]
        {
            (TrendMode.Down, "下跌"),
            (TrendMode.Normal, "正常"),
            (TrendMode.Up, "上涨")
        })
        {
            var item = new ToolStripMenuItem(text)
            {
                Tag = mode,
                CheckOnClick = true,
                Checked = mode == trendMode
            };
            item.Click += (_, _) =>
            {
                trendMode = (TrendMode)item.Tag!;
                foreach (ToolStripMenuItem sibling in trendModeMenu.DropDownItems.OfType<ToolStripMenuItem>())
                {
                    sibling.Checked = sibling.Tag is TrendMode siblingMode && siblingMode == trendMode;
                }

                stage.Invalidate();
            };
            trendModeMenu.DropDownItems.Add(item);
        }

        var brand = new ToolStripMenuItem("▥  音潮行情") { ForeColor = TerminalTheme.Cyan };
        brand.Click += (_, _) => { quoteFilter = 0; quoteScroll = 0; stage.Invalidate(); };
        menu.Items.Add(brand);
        var dayItem = new ToolStripMenuItem("实时K线");
        dayItem.Click += (_, _) => SelectTerminalChart(ChartMode.DayK);
        var minuteItem = new ToolStripMenuItem("分时走势");
        minuteItem.Click += (_, _) => SelectTerminalChart(ChartMode.MinuteLine);
        menu.Items.Add(dayItem);
        menu.Items.Add(minuteItem);
        menu.Items.Add(BuildIndicatorMenu());
        menu.Items.Add(trendModeMenu);
        menu.Items.Add(BuildMusicFeaturesMenu());
        var lyricItem = new ToolStripMenuItem("歌词");
        lyricItem.Click += (_, _) => { lyricsVisible = !lyricsVisible; stage.Invalidate(); };
        menu.Items.Add(lyricItem);
        var settingsItem = new ToolStripMenuItem("终端设置") { CheckOnClick = true, Checked = terminalSettingsVisible };
        settingsItem.Click += (_, _) =>
        {
            terminalSettingsVisible = settingsItem.Checked;
            if (controlPanel is not null) controlPanel.Visible = terminalSettingsVisible && !fullscreen;
        };
        menu.Items.Add(settingsItem);
        var screenItem = new ToolStripMenuItem("副屏全屏  F11");
        screenItem.Click += (_, _) => ToggleFullscreen();
        menu.Items.Add(screenItem);
        TerminalTheme.SizeMenu(menu, playground.Settings.MenuSize, true);
        return menu;
    }

    private static Label MakeLabel(string text, int size, bool strong)
    {
        return new Label
        {
            AutoSize = false,
            Height = text.Contains('\n') ? 42 : 24,
            Text = text,
            ForeColor = TerminalTheme.Text,
            Font = new Font("Microsoft YaHei UI", size, strong ? FontStyle.Bold : FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static Button MakeButton(string text)
    {
        return new Button
        {
            Height = 29,
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = TerminalTheme.Input,
            ForeColor = TerminalTheme.Text,
            FlatAppearance = { BorderColor = TerminalTheme.Border, MouseOverBackColor = TerminalTheme.Selected },
            Font = new Font("Microsoft YaHei UI", 9, FontStyle.Regular)
        };
    }

    private void TryStartLoopbackCapture()
    {
        if (capture != null)
        {
            simulationMode = false;
            return;
        }

        try
        {
            capture = new AudioLoopbackCapture(ProcessCapturedBuffer, ReportCaptureStatus);
            capture.Start();
            liveAudio = false;
            simulationMode = false;
            captureStatus = "已启动，等待系统音频包";
        }
        catch (Exception ex)
        {
            capture?.Dispose();
            capture = null;
            liveAudio = false;
            simulationMode = false;
            captureStatus = $"采集失败：{ex.Message}";
        }
    }

    private void ReportCaptureStatus(string message)
    {
        if (message.StartsWith("WASAPI_ERROR:", StringComparison.Ordinal))
        {
            liveAudio = false;
            Volatile.Write(ref latestAudioRms, 0);
        }
        captureStatus = message switch
        {
            _ when message.StartsWith("WASAPI_CONNECTED:", StringComparison.Ordinal) =>
                "系统音频设备已连接 " + message["WASAPI_CONNECTED:".Length..],
            _ when message.StartsWith("WASAPI_ERROR:", StringComparison.Ordinal) =>
                "系统音频错误：" + message["WASAPI_ERROR:".Length..],
            "WASAPI_STARTED" => "系统音频采集已启动，等待数据包",
            _ => message
        };
    }

    private void ProcessCapturedBuffer(IntPtr data, int frames, WaveFormatInfo format, bool silent)
    {
        if (frames <= 0)
        {
            return;
        }

        capturedSampleRate = format.SampleRate;
        Interlocked.Increment(ref audioBufferCount);
        int bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
        int frameSize = Math.Max(1, bytesPerSample * format.Channels);
        int bytesRecorded = frames * frameSize;
        byte[] rented = ArrayPool<byte>.Shared.Rent(bytesRecorded);

        try
        {
            if (!silent)
            {
                Marshal.Copy(data, rented, 0, bytesRecorded);
            }
            else
            {
                Array.Clear(rented, 0, bytesRecorded);
            }

            float squareSum = 0;
            lock (audioLock)
            {
                for (int frame = 0; frame < frames; frame++)
                {
                    float sum = 0;
                    int offset = frame * frameSize;
                    for (int channel = 0; channel < format.Channels; channel++)
                    {
                        int sampleOffset = offset + channel * bytesPerSample;
                        sum += SampleToFloat(rented, sampleOffset, bytesPerSample, format.IsFloat);
                    }

                    float sample = silent ? 0 : sum / format.Channels;
                    audioRing[ringWrite] = sample;
                    squareSum += sample * sample;
                    ringWrite = (ringWrite + 1) % audioRing.Length;
                }
            }

            float rms = MathF.Sqrt(squareSum / Math.Max(1, frames));
            Volatile.Write(ref latestAudioRms, rms);
            Interlocked.Exchange(ref lastAudioPacketAt, Environment.TickCount64);
            if (!silent && rms >= 0.0001f)
            {
                Interlocked.Increment(ref nonSilentBufferCount);
            }

            liveAudio = true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static float SampleToFloat(byte[] buffer, int offset, int bytesPerSample, bool isFloat)
    {
        if (bytesPerSample == 1)
        {
            return (buffer[offset] - 128) / 128f;
        }

        if (isFloat && bytesPerSample == 4)
        {
            return BitConverter.ToSingle(buffer, offset);
        }

        if (bytesPerSample == 3)
        {
            int value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
            if ((value & 0x00800000) != 0)
            {
                value |= unchecked((int)0xFF000000);
            }

            return value / 8388608f;
        }

        if (bytesPerSample == 2)
        {
            return BitConverter.ToInt16(buffer, offset) / 32768f;
        }

        if (bytesPerSample == 4)
        {
            return BitConverter.ToInt32(buffer, offset) / 2147483648f;
        }

        return 0;
    }

    private void OnFrame()
    {
        if (closing || IsDisposed || stage.IsDisposed)
        {
            return;
        }

        try
        {
            if (simulationMode)
            {
                GenerateSimulation();
                Array.Copy(bandTargets, energyTargets, BandCount);
            }
            else if (liveAudio)
            {
                AnalyzeAudio();
            }
            else
            {
                Array.Clear(bandTargets);
                Array.Clear(energyTargets);
            }

            UpdateAudioStatus();
            hasAudioSignal = simulationMode || (liveAudio
                && Environment.TickCount64 - Interlocked.Read(ref lastAudioPacketAt) <= 350
                && Volatile.Read(ref latestAudioRms) >= .0001f);
            if (hasAudioSignal) UpdateSpectrumLevels();
            else
            {
                // Every live energy display must agree with the silent input.
                Array.Clear(bandTargets);
                Array.Clear(energyTargets);
                Array.Clear(energyLevels);
                lastSpectrumAt = clock.Elapsed.TotalSeconds;
            }
            double marketNow = clock.Elapsed.TotalSeconds;
            double marketDt = lastMarketFrameAt > 0 ? marketNow - lastMarketFrameAt : 0;
            lastMarketFrameAt = marketNow;
            if (!hasAudioSignal)
            {
                // Preserve the current OHLC and phase; silence is not a falling price.
                candlePeriodStart += marketDt;
                dayKCandleStart += marketDt;
            }
            else UpdateCandles();
            ObserveMusicEnergy();
            if (hasAudioSignal)
            {
                UpdateDayKRolling();
                UpdateMinuteLine();
                SampleSongRecording();
            }
            if (hasAudioSignal) UpdateDisplayRange();
            UpdateLyrics();
            PollPlayerVolume();
            if (!closing && !stage.IsDisposed)
            {
                stage.Invalidate();
            }
        }
        catch (ObjectDisposedException)
        {
            // The timer can receive one final message while the form is closing.
        }
        catch (InvalidOperationException) when (closing || IsDisposed)
        {
        }
    }

    private void UpdateAudioStatus()
    {
        if (statusLabel is null || closing || statusLabel.IsDisposed)
        {
            return;
        }

        double now = clock.Elapsed.TotalSeconds;
        if (now < nextAudioStatusAt)
        {
            return;
        }

        nextAudioStatusAt = now + 0.35;
        if (simulationMode)
        {
            statusLabel.Text = "音频：模拟数据";
            return;
        }

        long buffers = Interlocked.Read(ref audioBufferCount);
        long signalBuffers = Interlocked.Read(ref nonSilentBufferCount);
        float rms = Volatile.Read(ref latestAudioRms);
        if (!liveAudio)
        {
            statusLabel.Text = $"音频：等待系统音频包 | {captureStatus}";
        }
        else
        {
            statusLabel.Text = $"音频：{captureStatus} | 包 {buffers} | 有声 {signalBuffers} | 电平 {rms:0.0000}";
        }
    }

    private void GenerateSimulation()
    {
        float time = (float)clock.Elapsed.TotalSeconds;
        for (int i = 0; i < bandTargets.Length; i++)
        {
            float x = i / (float)(bandTargets.Length - 1);
            float bass = MathF.Exp(-x * 5.0f) * (0.55f + 0.35f * MathF.Sin(time * 3.1f));
            float mid = MathF.Exp(-MathF.Pow((x - 0.42f) * 4.0f, 2)) * (0.35f + 0.35f * MathF.Sin(time * 5.3f + i * 0.13f));
            float high = MathF.Exp(-MathF.Pow((x - 0.78f) * 5.0f, 2)) * (0.25f + 0.25f * MathF.Sin(time * 9.0f + i * 0.37f));
            float pulse = random.NextSingle() * 0.08f;
            bandTargets[i] = Math.Clamp(0.04f + bass + mid + high + pulse, 0.02f, 1.0f);
        }
    }

    private void AnalyzeAudio()
    {
        if (Environment.TickCount64 - Interlocked.Read(ref lastAudioPacketAt) > 350
            || Volatile.Read(ref latestAudioRms) < .0001f)
        {
            Array.Clear(bandTargets);
            Array.Clear(energyTargets);
            return;
        }
        const int fftSize = 4096;
        float rms = 0;
        lock (audioLock)
        {
            int start = (ringWrite - fftSize + audioRing.Length) % audioRing.Length;
            for (int i = 0; i < fftSize; i++)
            {
                float sample = audioRing[(start + i) % audioRing.Length];
                float window = 0.5f - 0.5f * MathF.Cos(2.0f * MathF.PI * i / (fftSize - 1));
                fft[i] = new Complex(sample * window, 0);
                rms += sample * sample;
            }
        }

        rms = MathF.Sqrt(rms / fftSize);
        FastFourierTransform(fft);

        const float minFreq = 38f;
        const float maxFreq = 16000f;
        float nyquist = capturedSampleRate * 0.5f;
        for (int band = 0; band < bandTargets.Length; band++)
        {
            float a = band / (float)bandTargets.Length;
            float b = (band + 1) / (float)bandTargets.Length;
            float f0 = minFreq * MathF.Pow(maxFreq / minFreq, a);
            float f1 = minFreq * MathF.Pow(maxFreq / minFreq, b);
            int bin0 = Math.Clamp((int)(f0 / nyquist * (fftSize / 2)), 1, fftSize / 2 - 1);
            int bin1 = Math.Clamp((int)(f1 / nyquist * (fftSize / 2)), bin0 + 1, fftSize / 2);
            double sum = 0;
            double squareSum = 0;
            for (int bin = bin0; bin < bin1; bin++)
            {
                sum += fft[bin].Magnitude;
                squareSum += fft[bin].Magnitude * fft[bin].Magnitude;
            }

            float energy = (float)(sum / (bin1 - bin0));
            rawBandEnergy[band] = MathF.Log10(1.0f + energy * 80.0f);
            // Absolute FFT amplitude for the live meter; independent of price normalization.
            double amplitude = 4 * Math.Sqrt(squareSum / (bin1 - bin0)) / fftSize;
            energyTargets[band] = (float)Math.Clamp((20 * Math.Log10(Math.Max(1e-9, amplitude)) + 72) / 66, 0, 1);
        }

        for (int band = 0; band < bandTargets.Length; band++)
        {
            float raw = rawBandEnergy[band];
            if (bandEnergyPeak[band] <= 0)
            {
                bandEnergyFloor[band] = raw;
                bandEnergyPeak[band] = raw + 0.08f;
            }

            float floorRate = raw < bandEnergyFloor[band] ? 0.08f : 0.006f;
            bandEnergyFloor[band] += (raw - bandEnergyFloor[band]) * floorRate;

            float peakRate = raw > bandEnergyPeak[band] ? 0.18f : 0.012f;
            bandEnergyPeak[band] += (raw - bandEnergyPeak[band]) * peakRate;
            if (bandEnergyPeak[band] - bandEnergyFloor[band] < 0.06f)
            {
                bandEnergyPeak[band] = bandEnergyFloor[band] + 0.06f;
            }
        }

        for (int band = 0; band < bandTargets.Length; band++)
        {
            float raw = rawBandEnergy[band];
            float normalized = (raw - bandEnergyFloor[band]) / Math.Max(0.04f, bandEnergyPeak[band] - bandEnergyFloor[band]);
            float left = rawBandEnergy[Math.Max(0, band - 1)];
            float right = rawBandEnergy[Math.Min(rawBandEnergy.Length - 1, band + 1)];
            float neighbor = (left + right) * 0.5f;
            float contrast = Math.Clamp((raw - neighbor) * 1.6f, -0.45f, 0.45f);
            float spectralLevel = Math.Clamp(0.50f + (normalized - 0.50f) * 0.72f + contrast, 0.02f, 0.98f);
            bandTargets[band] = spectralLevel;
        }
    }

    private static void FastFourierTransform(Complex[] data)
    {
        int n = data.Length;
        int j = 0;
        for (int i = 1; i < n; i++)
        {
            int bit = n >> 1;
            while ((j & bit) != 0)
            {
                j ^= bit;
                bit >>= 1;
            }

            j ^= bit;
            if (i < j)
            {
                (data[i], data[j]) = (data[j], data[i]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2.0 * Math.PI / len;
            var step = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (int i = 0; i < n; i += len)
            {
                Complex w = Complex.One;
                int half = len >> 1;
                for (int k = 0; k < half; k++)
                {
                    Complex even = data[i + k];
                    Complex odd = data[i + k + half] * w;
                    data[i + k] = even + odd;
                    data[i + k + half] = even - odd;
                    w *= step;
                }
            }
        }
    }

    private void UpdateSpectrumLevels()
    {
        double now = clock.Elapsed.TotalSeconds;
        double dt = lastSpectrumAt == 0 ? .03 : Math.Clamp(now - lastSpectrumAt, 0, .25);
        lastSpectrumAt = now;
        for (int i = 0; i < BandCount; i++)
        {
            double response = energyTargets[i] > energyLevels[i] ? .04 : .16;
            energyLevels[i] += (energyTargets[i] - energyLevels[i]) * (float)(1 - Math.Exp(-dt / response));
            if (energyTargets[i] == 0 && energyLevels[i] < .0001f) energyLevels[i] = 0;
        }
    }

    private void UpdateCandles()
    {
        double now = clock.Elapsed.TotalSeconds;
        if (!candlePeriodInitialized || now - candlePeriodStart >= candleDurationSeconds)
        {
            BeginNewCandlePeriod(now);
        }

        float progress = Math.Clamp((float)((now - candlePeriodStart) / candleDurationSeconds), 0, 1);
        float eased = progress * progress * (3.0f - 2.0f * progress);

        for (int i = 0; i < candles.Length; i++)
        {
            float target = bandTargets[i];
            float previous = bandLevels[i];
            float attack = simulationMode
                ? target > previous ? 0.14f : 0.08f
                : target > previous ? 0.095f : 0.055f;
            bandLevels[i] = previous + (target - previous) * attack;
            float level = bandLevels[i];
            float delta = level - periodLastLevel[i];
            periodLastLevel[i] = level;
            periodDeltaSum[i] += delta;
            periodSamples[i]++;
            periodMinLevel[i] = Math.Min(periodMinLevel[i], level);
            periodMaxLevel[i] = Math.Max(periodMaxLevel[i], level);

            float moveFromStart = level - periodStartLevel[i];
            float liveMove = moveFromStart * 0.34f + periodDeltaSum[i] * 0.18f;
            float close = Math.Clamp(periodOpen[i] + liveMove, -MaxMove, MaxMove);
            close = Math.Clamp(close, periodOpen[i] - MaxMove, periodOpen[i] + MaxMove);
            var candle = candles[i];
            close = QuantizeClose(periodOpen[i], close, candle.Close);

            periodHigh[i] = Math.Min(MaxMove, Math.Max(periodHigh[i], close));
            periodLow[i] = Math.Max(-MaxMove, Math.Min(periodLow[i], close));
            var livePattern = ClassifyPattern(periodOpen[i], close, periodHigh[i], periodLow[i], i);

            candle.Open = Math.Clamp(periodOpen[i], -MaxMove, MaxMove);
            candle.Close = Math.Clamp(close, -MaxMove, MaxMove);
            candle.High = Math.Clamp(Math.Max(periodHigh[i], Math.Max(candle.Open, candle.Close)), -MaxMove, MaxMove);
            candle.Low = Math.Clamp(Math.Min(periodLow[i], Math.Min(candle.Open, candle.Close)), -MaxMove, MaxMove);
            candle.Velocity = candle.Close - candle.Open;
            candle.Pattern = livePattern;
            candles[i] = candle;
        }
    }

    private void BeginNewCandlePeriod(double now)
    {
        candlePeriodInitialized = true;
        candlePeriodStart = now;
        candleCycleSeed++;

        for (int i = 0; i < candles.Length; i++)
        {
            float previousClose = candleCycleSeed == 1 ? 0 : Math.Clamp(candles[i].Close, -MaxMove, MaxMove);
            if (candleCycleSeed == 1)
            {
                bandLevels[i] = bandTargets[i];
            }

            float energy = Math.Clamp(bandLevels[i], 0.03f, 0.97f);
            var pattern = ClassifyPattern(previousClose, previousClose, previousClose, previousClose, i);

            periodOpen[i] = previousClose;
            periodPatterns[i] = pattern;
            periodHigh[i] = periodOpen[i];
            periodLow[i] = periodOpen[i];
            periodStartLevel[i] = energy;
            periodMinLevel[i] = energy;
            periodMaxLevel[i] = energy;
            periodDeltaSum[i] = 0;
            periodLastLevel[i] = energy;
            periodSamples[i] = 0;

            candles[i] = new Candle
            {
                Open = periodOpen[i],
                Close = periodOpen[i],
                High = periodOpen[i],
                Low = periodOpen[i],
                Pattern = pattern
            };
        }
    }

    private void ResetSongChart(double durationSeconds, string title)
    {
        ResetMusicFeatures();
        indicatorHistory.Clear();
        dayHistory.Clear(); dayViewport.Reset(); EndDayNavigation();
        songDurationSeconds = Math.Clamp(durationSeconds, 10, 24 * 60 * 60);
        currentTrackTitle = string.IsNullOrWhiteSpace(title) ? "未知歌曲" : title.Trim();
        currentTrackKey = NormalizeTrackKey(currentTrackTitle);
        RandomizeSongBands();
        songOpeningPrice = Math.Round(5 + Random.Shared.NextDouble() * 95, 2);
        virtualVolume = virtualAmount = 0;
        songPositionSeconds = 0;
        lastTrackPositionSeconds = -1;
        timelineCandleCount = 0;
        dayKCandleStart = clock.Elapsed.TotalSeconds;
        timelineClose = 0;
        timelineLastSourceLevel = 0.5f;
        timelineLastSelectedLevel = 0.5f;
        timelineSourceBand = -1;
        timelineSelectedLevelInitialized = false;
        timelineInitialized = false;
        displayRange = MaxMove;
        SetTrackStatus($"{currentTrackTitle} | 歌曲时长 {FormatSongTime(songDurationSeconds)} | 已重置");
        minutePointCapacity = Math.Clamp(
            (int)Math.Ceiling(songDurationSeconds / 0.08),
            60,
            MinutePointCount);

        Array.Clear(timelineCandles);
        Array.Clear(candles);
        Array.Clear(periodOpen);
        Array.Clear(periodHigh);
        Array.Clear(periodLow);
        Array.Clear(periodStartLevel);
        Array.Clear(periodMinLevel);
        Array.Clear(periodMaxLevel);
        Array.Clear(periodDeltaSum);
        Array.Clear(periodLastLevel);
        Array.Clear(periodSamples);
        Array.Clear(periodPatterns);
        candleCycleSeed = 0;
        candlePeriodInitialized = false;
        ResetMinuteLine();
        ResetLyricState();
        for (int i = 0; i < candles.Length; i++)
        {
            candles[i] = new Candle { Pattern = CandlePattern.Flat };
        }
    }

    private void RandomizeSongBands()
    {
        int previousBand = minuteBandIndex, previousComparison = frequencyMidpointBand;
        var samples = Enumerable.Range(0, BandCount).Where(i => i != previousBand).ToArray();
        minuteBandIndex = samples[Random.Shared.Next(samples.Length)];
        var comparisons = Enumerable.Range(0, BandCount).Where(i => i != minuteBandIndex && i != previousComparison).ToArray();
        frequencyMidpointBand = comparisons[Random.Shared.Next(comparisons.Length)];
        if (minuteFrequencyControl is not null) minuteFrequencyControl.Value = minuteBandIndex;
        if (frequencyMidpointControl is not null) frequencyMidpointControl.Value = frequencyMidpointBand;
        if (minuteFrequencyLabel is not null) minuteFrequencyLabel.Text = $"分时采样频率：{FormatFrequency(BandCenterFrequency(minuteBandIndex))}";
        if (frequencyMidpointLabel is not null) frequencyMidpointLabel.Text = $"对比频率：{FormatFrequency(BandCenterFrequency(frequencyMidpointBand))}";
        quoteFilter = 0; quoteScroll = 0;
        var bands = QuoteBands(); int index = Array.IndexOf(bands, minuteBandIndex);
        if (index >= 0) quoteScroll = Math.Max(0, index - Math.Max(1, visibleQuoteRows) / 2);
    }

    private void ResetLyricState()
    {
        lyricClock.Reset();
        lyricTimeline = [];
        activeLyricIndex = -1;
        liveLyricHistory.Clear();
        lyricTimingStatus = "等待播放器";
        lyricFeedFollow = true;
        lyricFeedScroll = 0;
        currentLyric = "等待自动捕捉歌词";
        particles.Clear();
        if (lyricSourceLabel is not null)
        {
            lyricSourceLabel.Text = autoCaptureLyrics ? "歌词来源：正在同步播放器与本地歌词" : "歌词捕捉已暂停";
        }
    }

    private void UpdateDayKRolling()
    {
        double now = clock.Elapsed.TotalSeconds;
        if (!trackIsPlaying) { dayKCandleStart = now; return; }
        if (!timelineInitialized)
        {
            timelineInitialized = true;
            dayKCandleStart = now;
            timelineCandleCount = 1;
            timelineClose = 0;
            timelineCandles[0] = new Candle
            {
                Open = 0,
                Close = 0,
                High = 0,
                Low = 0,
                Pattern = CandlePattern.Flat
            };
        }

        while (now - dayKCandleStart >= candleDurationSeconds)
        {
            var finished = timelineCandles[timelineCandleCount - 1];
            ApplyClimaxBar(ref finished, true);
            ObserveCompletedBoard(finished);
            timelineCandles[timelineCandleCount - 1] = finished;
            RecordIndicatorBar();
            dayKCandleStart += candleDurationSeconds;
            if (timelineCandleCount < VisibleTimelineCandleCount)
            {
                timelineCandleCount++;
            }
            else
            {
                Array.Copy(timelineCandles, 1, timelineCandles, 0, VisibleTimelineCandleCount - 1);
            }

            int index = timelineCandleCount - 1;
            float open = Math.Clamp(timelineClose + CreateGap(), -4.0f, 4.0f);
            timelineCandles[index] = new Candle
            {
                Open = open,
                Close = open,
                High = open,
                Low = open,
                Pattern = CandlePattern.Flat
            };
            var started = timelineCandles[index];
            BeginClimaxBar(ref started, timelineClose);
            timelineCandles[index] = started;
        }

        int currentIndex = timelineCandleCount - 1;
        Candle candle = timelineCandles[currentIndex];
        Candle originalCandle = candle;
        if (!timelineInitialized)
        {
            timelineInitialized = true;
            timelineLastSourceLevel = 0.5f;
        }

        float source = GetTimelineSourceLevel();
        float sourceDelta = source - timelineLastSourceLevel;
        timelineLastSourceLevel = source;
        float drift = (source - 0.5f) * 0.020f + sourceDelta * 0.24f;
        var playInfluence = playground.Influence(GetPlaybackPositionSeconds(), AverageBandLevel(0, BassBandEnd), AverageBandLevel(VoiceBandEnd, BandCount), AverageBandLevel(BassBandEnd, VoiceBandEnd));
        drift = drift * playInfluence.Volatility + playInfluence.BiasPerSecond * (float)Math.Clamp(clock.Elapsed.TotalSeconds - lastPlayDriftAt, 0, .1);
        lastPlayDriftAt = clock.Elapsed.TotalSeconds;
        float desiredClose = candle.Close + drift;
        float close = Math.Clamp(desiredClose, candle.Open - MaxMove, candle.Open + MaxMove);
        close = Math.Clamp(close, -4.0f, 4.0f);
        candle.Close = close;
        candle.High = Math.Clamp(Math.Max(candle.High, close), candle.Open - MaxMove, candle.Open + MaxMove);
        candle.Low = Math.Clamp(Math.Min(candle.Low, close), candle.Open - MaxMove, candle.Open + MaxMove);
        candle.Velocity = candle.Close - candle.Open;
        if (climaxMarket.Board is not null && climaxMarket.Settings.Enabled) candle = originalCandle;
        ApplyClimaxBar(ref candle);
        candle.Pattern = ClassifyPattern(candle.Open, candle.Close, candle.High, candle.Low, currentIndex);
        timelineCandles[currentIndex] = candle;
        timelineClose = candle.Close;
    }

    private float GetTimelineSourceLevel()
    {
        int band = Math.Clamp(minuteBandIndex, 0, BandCount - 1);
        int comparison = band.CompareTo(Math.Clamp(frequencyMidpointBand, 0, BandCount - 1));
        float selectedLevel = bandLevels[band] * 0.62f + bandTargets[band] * 0.38f;
        if (!timelineSelectedLevelInitialized || timelineSourceBand != band)
        {
            timelineSelectedLevelInitialized = true;
            timelineSourceBand = band;
            timelineLastSelectedLevel = selectedLevel;
            return 0.5f;
        }

        float levelChange = selectedLevel - timelineLastSelectedLevel;
        timelineLastSelectedLevel = selectedLevel;
        if (comparison == 0)
        {
            comparison = 0;
        }

        var profile = GetTrendProfile();
        float musicSignal = levelChange * comparison * 10.0f;
        float modeSignal = profile.ModeBias * 0.08f;
        return Math.Clamp(
            0.5f + musicSignal * profile.MusicInfluence + modeSignal * (1.0f - profile.MusicInfluence),
            0.02f,
            0.98f);
    }

    private float CreateGap()
    {
        var profile = GetTrendProfile();
        float sign = random.NextSingle() < profile.GapUpProbability ? 1.0f : -1.0f;
        float size = 0.004f + random.NextSingle() * 0.016f;
        return sign * size;
    }

    private (float GapUpProbability, float MusicInfluence, float ModeBias) GetTrendProfile()
    {
        return trendMode switch
        {
            TrendMode.Down => (0.18f, 0.35f, -1.0f),
            TrendMode.Up => (0.82f, 0.35f, 1.0f),
            _ => (0.50f, 1.0f, 0.0f)
        };
    }

    private static string FormatTrendMode(TrendMode mode)
    {
        return mode switch
        {
            TrendMode.Down => "下跌",
            TrendMode.Up => "上涨",
            _ => "正常"
        };
    }

    private double GetPlaybackPositionSeconds()
    {
        if (lastTrackPositionSeconds >= 0)
        {
            return Math.Clamp(lyricClock.HasPosition ? lyricClock.Position : songPositionSeconds, 0, songDurationSeconds);
        }

        return Math.Clamp(clock.Elapsed.TotalSeconds, 0, songDurationSeconds);
    }

    private float AverageBandLevel(int start, int end)
    {
        start = Math.Clamp(start, 0, bandTargets.Length);
        end = Math.Clamp(end, start + 1, bandTargets.Length);
        float sum = 0;
        for (int i = start; i < end; i++)
        {
            sum += bandTargets[i];
        }

        return sum / (end - start);
    }

    private void UpdateDisplayRange()
    {
        float maxAbs = 0.006f;
        if (chartMode == ChartMode.MinuteLine)
        {
            foreach (float value in GetMinuteValuesInOrder())
            {
                maxAbs = Math.Max(maxAbs, Math.Abs(value));
            }
        }
        else
        {
            for (int i = 0; i < timelineCandleCount; i++)
            {
                var candle = timelineCandles[i];
                maxAbs = Math.Max(maxAbs, Math.Abs(candle.Open));
                maxAbs = Math.Max(maxAbs, Math.Abs(candle.Close));
                maxAbs = Math.Max(maxAbs, Math.Abs(candle.High));
                maxAbs = Math.Max(maxAbs, Math.Abs(candle.Low));
            }
        }

        float targetRange = chartMode == ChartMode.MinuteLine
            ? Math.Clamp(maxAbs * 1.42f, MinDisplayRange, MaxMove)
            : Math.Max(MaxMove, maxAbs * 1.18f);
        float rate = targetRange > displayRange ? 0.16f : 0.045f;
        displayRange += (targetRange - displayRange) * rate;
        displayRange = chartMode == ChartMode.MinuteLine
            ? Math.Clamp(displayRange, MinDisplayRange, MaxMove)
            : Math.Clamp(displayRange, MaxMove, 4.0f);
    }

    private void UpdateMinuteLine()
    {
        double now = clock.Elapsed.TotalSeconds;
        if (now < nextMinuteSampleAt)
        {
            return;
        }

        nextMinuteSampleAt = now + 0.08;
        double position = GetPlaybackPositionSeconds();
        int targetCount = Math.Clamp(
            (int)Math.Floor(position / Math.Max(0.01, songDurationSeconds) * minutePointCapacity) + 1,
            1,
            minutePointCapacity);
        int band = Math.Clamp(minuteBandIndex, 0, bandLevels.Length - 1);
        float level = bandLevels[band] * 0.62f + bandTargets[band] * 0.38f;
        if (minutePointTotal == 0)
        {
            minuteLastBandLevel = level;
            minuteLineValue = 0;
        }

        float neighbor = (
            bandLevels[Math.Max(0, band - 1)] +
            bandLevels[Math.Min(bandLevels.Length - 1, band + 1)]) * 0.5f;
        float direction = band == frequencyMidpointBand ? 0 : band > frequencyMidpointBand ? 1 : -1;
        float delta = (level - minuteLastBandLevel) * direction;
        float contrast = (level - neighbor) * direction;
        minuteLastBandLevel = level;

        var profile = GetTrendProfile();
        float musicMove = delta * 0.34f + contrast * 0.018f;
        float modeMove = profile.ModeBias * (1.0f - profile.MusicInfluence) * 0.0016f;
        float desired = minuteLineValue + musicMove * profile.MusicInfluence + modeMove;
        if (targetCount > minutePointTotal && minutePointTotal > 0 && direction != 0)
        {
            desired += CreateGap() * 0.35f;
        }
        if (Math.Abs(minuteLineValue) >= MaxMove * 0.78f && now >= nextMinuteShockAt)
        {
            float shockDirection = minuteLineValue == 0
                ? (random.NextSingle() >= 0.5f ? 1 : -1)
                : MathF.Sign(minuteLineValue);
            if (random.NextSingle() < 0.35f)
            {
                shockDirection *= -1;
            }

            desired += shockDirection * (0.006f + random.NextSingle() * 0.020f);
            nextMinuteShockAt = now + 0.55 + random.NextDouble() * 0.9;
        }

        float quantized = MathF.Round(desired / CloseQuantum) * CloseQuantum;
        float maxStep = Math.Abs(minuteLineValue) >= MaxMove * 0.78f
            ? CloseQuantum * (2.0f + random.NextSingle() * 5.0f)
            : CloseQuantum * 1.25f;
        minuteLineValue = Math.Clamp(minuteLineValue + Math.Clamp(quantized - minuteLineValue, -maxStep, maxStep), -MaxMove, MaxMove);

        int currentIndex = Math.Clamp(targetCount - 1, 0, minutePointCapacity - 1);
        while (minutePointTotal < targetCount)
        {
            minuteValues[minutePointTotal] = minuteLineValue;
            minutePointTotal++;
        }

        minuteValues[currentIndex] = minuteLineValue;
    }

    private void ResetMinuteLine()
    {
        Array.Clear(minuteValues);
        minutePointTotal = 0;
        minuteLineValue = 0;
        nextMinuteShockAt = 0;
        int band = Math.Clamp(minuteBandIndex, 0, bandLevels.Length - 1);
        minuteLastBandLevel = bandLevels[band];
        displayRange = MinDisplayRange;
    }

    private IEnumerable<float> GetMinuteValuesInOrder()
    {
        for (int i = 0; i < minutePointTotal; i++)
        {
            yield return minuteValues[i];
        }
    }

    private static float QuantizeClose(float open, float desiredClose, float previousClose)
    {
        float quantized = open + MathF.Round((desiredClose - open) / CloseQuantum) * CloseQuantum;
        float maxStep = CloseQuantum * 1.4f;
        return Math.Clamp(previousClose + Math.Clamp(quantized - previousClose, -maxStep, maxStep), -MaxMove, MaxMove);
    }

    private static CandlePattern ClassifyPattern(float open, float close, float high, float low, int index)
    {
        float body = Math.Abs(close - open);
        float range = Math.Max(0.001f, high - low);
        float upper = high - Math.Max(open, close);
        float lower = Math.Min(open, close) - low;
        float colorThreshold = CloseQuantum * 0.55f;

        if (range < colorThreshold && body < colorThreshold)
        {
            return CandlePattern.Flat;
        }

        if (body <= CloseQuantum && upper > body * 1.4f && lower > body * 1.4f)
        {
            return CandlePattern.Doji;
        }

        bool highOpen = open >= 0 || upper < lower * 0.7f || index < 12;
        bool rising = close >= open;
        return (highOpen, rising) switch
        {
            (true, false) => CandlePattern.HighOpenDown,
            (false, true) => CandlePattern.LowOpenUp,
            (true, true) => CandlePattern.HighOpenUp,
            _ => CandlePattern.LowOpenDown
        };
    }

    private static float InitialUpperWick(CandlePattern pattern, int index)
    {
        float jitter = (index % 5) * 0.006f;
        return pattern switch
        {
            CandlePattern.Flat => 0.004f,
            CandlePattern.Doji => 0.085f + jitter,
            CandlePattern.HighOpenDown => 0.06f + jitter,
            CandlePattern.HighOpenUp => 0.03f + jitter,
            CandlePattern.LowOpenUp => 0.05f + jitter,
            CandlePattern.LowOpenDown => 0.03f + jitter,
            _ => 0.04f
        };
    }

    private static float InitialLowerWick(CandlePattern pattern, int index)
    {
        float jitter = (index % 4) * 0.006f;
        return pattern switch
        {
            CandlePattern.Flat => 0.004f,
            CandlePattern.Doji => 0.085f + jitter,
            CandlePattern.HighOpenDown => 0.055f + jitter,
            CandlePattern.HighOpenUp => 0.025f + jitter,
            CandlePattern.LowOpenUp => 0.035f + jitter,
            CandlePattern.LowOpenDown => 0.055f + jitter,
            _ => 0.04f
        };
    }

    private static float PatternUpperWick(CandlePattern pattern, int index)
    {
        return InitialUpperWick(pattern, index) * 0.45f;
    }

    private static float PatternLowerWick(CandlePattern pattern, int index)
    {
        return InitialLowerWick(pattern, index) * 0.45f;
    }

    private void UpdateLyrics()
    {
        if (autoCaptureLyrics && lyricTimeline.Length > 0 && lyricClock.HasPosition)
        {
            int index = LyricPlaybackClock.FindActive(lyricTimeline, GetPlaybackPositionSeconds());
            if (index != activeLyricIndex)
            {
                activeLyricIndex = index;
            }
            if (index >= 0) currentLyric = lyricTimeline[index].Text;
        }
        if (!hasAudioSignal) return;
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            var p = particles[i];
            p.Age += 0.016f;
            p.X += p.Vx;
            p.Y += p.Vy;
            p.Vy += 0.012f;
            p.Alpha = Math.Max(0, 1.0f - p.Age / p.Life);
            particles[i] = p;
            if (p.Alpha <= 0)
            {
                particles.RemoveAt(i);
            }
        }
    }

    private void StartLyricCaptureLoop()
    {
        if (lyricCaptureTask is not null || closing)
        {
            return;
        }

        IntPtr ownWindow = Handle;
        lyricCaptureTask = Task.Run(() => LyricCaptureLoop(ownWindow, lyricCaptureCts.Token));
    }

    private void LyricCaptureLoop(IntPtr ownWindow, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            LyricFrame? kugouFrame = null;
            MediaTrackInfo? trackInfo = null;
            string? trackTitle = null;
            TimedLyricLine[] capturedTimeline = [];
            string timingStatus = "等待播放器";
            try
            {
                // 标题身份和播放位置分开读取，避免旧标题把新歌词缓存顶住。
                MediaTrackInfo? sessionTrack = WindowsMediaSessionCapture.TryCapture();
                bool kugouSessionActive = WindowsMediaSessionCapture.ActiveSourceApp?.Contains("kugou", StringComparison.OrdinalIgnoreCase) == true;
                var otherPlayer = MusicPlayerProfiles.Find(WindowsMediaSessionCapture.ActiveSourceApp)
                    ?? (sessionTrack is null ? PlayerWindowCapture.FindRunningPlayer() : null);
                if (otherPlayer is { Id: not "kugou" })
                {
                    var other = CaptureOtherPlayer(otherPlayer, sessionTrack, token);
                    trackInfo = other.Track;
                    trackTitle = other.Title;
                    capturedTimeline = other.Lines;
                    kugouFrame = other.Frame;
                    timingStatus = other.Status;
                }
                else
                {
                // Native window metadata stays available when hidden to the tray and
                // can change before the system media metadata catches up.
                string? nativeTitle = kugouSessionActive ? KugouPlaybackTimeCapture.TryCaptureTitle() : null;
                trackTitle = nativeTitle ?? sessionTrack?.Title;
                if (!string.IsNullOrWhiteSpace(trackTitle))
                {
                    string detectedTitle = trackTitle;
                    BeginInvoke(new Action(() => ApplyTrackTitle(detectedTitle)));
                }
                MediaTrackInfo? windowTrack = sessionTrack is { PositionSeconds: >= 0 } && !WindowsMediaSessionCapture.UsesEstimatedTimeline
                    ? null
                    : (kugouSessionActive && trackTitle is not null
                        ? KugouPlaybackTimeCapture.TryCapture(trackTitle, sessionTrack?.IsPlaying ?? true) : null)
                        ?? (kugouSessionActive ? null : MediaTrackCapture.TryCapture());
                trackInfo = sessionTrack is { DurationSeconds: >= 2, PositionSeconds: >= 0 } && !WindowsMediaSessionCapture.UsesEstimatedTimeline
                    ? sessionTrack
                    : windowTrack ?? sessionTrack;
                if (trackInfo is { } playback && sessionTrack is { } sessionPlayback)
                    trackInfo = playback with { IsPlaying = sessionPlayback.IsPlaying };
                trackTitle ??= MediaTrackCapture.TryCaptureTitle() ?? trackInfo?.Title;
                trackInfo = MatchTrackIdentity(trackInfo, trackTitle, sessionTrack?.IsPlaying ?? true);
                if (WindowsMediaSessionCapture.UsesEstimatedTimeline && windowTrack is { PositionSeconds: >= 0 } realWindow
                    && sessionTrack is { } windowSession
                    && NormalizeTrackKey(realWindow.Title) == NormalizeTrackKey(windowSession.Title))
                    WindowsMediaSessionCapture.ObserveLyric(windowSession.Title,
                        new LiveLyricPosition("播放器窗口进度", realWindow.PositionSeconds, realWindow.DurationSeconds, true));

                if ((trackInfo is null || trackInfo.Value.DurationSeconds < 2) && !string.IsNullOrWhiteSpace(trackTitle))
                {
                    double? lyricDuration = KugouLocalLyricCapture.TryGetDurationSeconds(trackTitle);
                    if (lyricDuration is >= 2 and <= 24 * 60 * 60)
                    {
                        double position = trackInfo?.PositionSeconds >= 0 ? trackInfo.Value.PositionSeconds : -1;
                        trackInfo = new MediaTrackInfo(trackTitle, lyricDuration.Value, position, sessionTrack?.IsPlaying ?? true);
                    }
                }

                if (WindowsMediaSessionCapture.UsesEstimatedTimeline
                    && !string.IsNullOrWhiteSpace(trackTitle)
                    && windowTrack is not { PositionSeconds: >= 0 }
                    && !KugouPlaybackTimeCapture.HasRecentClock(trackTitle)
                    && !kugouSessionActive
                    && KugouScreenLyricCapture.TryCapture(trackTitle, WindowsMediaSessionCapture.IsAlignedToObservedLyric
                        ? WindowsMediaSessionCapture.CurrentEstimatedPositionSeconds : null) is { } observed)
                {
                    WindowsMediaSessionCapture.ObserveLyric(trackTitle, observed);
                    if (trackInfo is { } alignedTrack)
                    {
                        trackInfo = alignedTrack with { DurationSeconds = observed.DurationSeconds >= 2 ? observed.DurationSeconds : alignedTrack.DurationSeconds,
                            PositionSeconds = WindowsMediaSessionCapture.CurrentEstimatedPositionSeconds };
                    }
                }

                kugouFrame = KugouLocalLyricCapture.TryCapture(trackInfo, null, trackTitle);
                if (kugouFrame is { } localFrame && WindowsMediaSessionCapture.UsesEstimatedTimeline)
                {
                    string timing = !string.IsNullOrWhiteSpace(trackTitle) && KugouPlaybackTimeCapture.HasRecentClock(trackTitle)
                        ? "酷狗真实播放时间"
                        : WindowsMediaSessionCapture.IsAlignedToObservedLyric
                        ? "桌面歌词已校时"
                        : "等待桌面歌词校时";
                    kugouFrame = new LyricFrame(localFrame.Text, localFrame.TimeTag,
                        $"{localFrame.Source} / {timing}");
                }
                LyricFrame? desktopFrame = null;
                if (kugouFrame is null && !kugouSessionActive)
                {
                    desktopFrame = KugouDesktopLyricCapture.TryCapture();
                    if (desktopFrame is { } liveFrame)
                    {
                        kugouFrame = KugouLocalLyricCapture.TryCapture(trackInfo, liveFrame.Text, trackTitle)
                            ?? liveFrame;
                    }
                }
                if (string.IsNullOrWhiteSpace(trackTitle))
                {
                    trackTitle = KugouLocalLyricCapture.TryGetCurrentTrackTitle();
                }

                if ((trackInfo is null || trackInfo.Value.DurationSeconds < 2) && !string.IsNullOrWhiteSpace(trackTitle))
                {
                    double? lyricDuration = KugouLocalLyricCapture.TryGetDurationSeconds(trackTitle);
                    if (lyricDuration is >= 2 and <= 24 * 60 * 60)
                    {
                        double position = trackInfo?.PositionSeconds >= 0 ? trackInfo.Value.PositionSeconds : -1;
                        trackInfo = new MediaTrackInfo(trackTitle, lyricDuration.Value, position, sessionTrack?.IsPlaying ?? true);
                    }
                }

                if (kugouFrame is { } frame
                    && string.IsNullOrWhiteSpace(frame.TimeTag)
                    && trackInfo is { } timedTrack
                    && timedTrack.PositionSeconds >= 0)
                {
                    string timeTag = TimeSpan.FromSeconds(Math.Max(0, timedTrack.PositionSeconds)).ToString(@"mm\:ss");
                    kugouFrame = new LyricFrame(frame.Text, timeTag, frame.Source);
                }
                if (!string.IsNullOrWhiteSpace(trackTitle)) capturedTimeline = KugouLocalLyricCapture.GetTimeline(trackTitle);
                if (kugouSessionActive && !string.IsNullOrWhiteSpace(trackTitle) && !KugouPlaybackTimeCapture.HasRecentClock(trackTitle))
                {
                    var fallback = KugouLyricFallback.TryCapture(trackTitle,
                        WindowsMediaSessionCapture.IsAlignedToObservedLyric ? WindowsMediaSessionCapture.CurrentEstimatedPositionSeconds : null,
                        capturedTimeline.Length == 0);
                    if (fallback.Position is { } position)
                    {
                        WindowsMediaSessionCapture.ObserveLyric(trackTitle, position);
                        if (trackInfo is { } aligned) trackInfo = aligned with
                        {
                            PositionSeconds = position.HasExactPosition ? position.PositionSeconds : WindowsMediaSessionCapture.CurrentEstimatedPositionSeconds,
                            DurationSeconds = position.DurationSeconds >= 2 ? position.DurationSeconds : aligned.DurationSeconds
                        };
                    }
                    if (capturedTimeline.Length == 0 && fallback.Frame is { } fallbackFrame) kugouFrame = fallbackFrame;
                }
                timingStatus = WindowsMediaSessionCapture.UsesEstimatedTimeline
                    ? !string.IsNullOrWhiteSpace(trackTitle) && KugouPlaybackTimeCapture.HasRecentClock(trackTitle)
                        ? "酷狗真实播放时间 · 实时同步"
                        : WindowsMediaSessionCapture.IsAlignedToObservedLyric ? "桌面校时 · 自动跟随" : "估算进度 · 等待桌面歌词校时"
                    : trackInfo?.PositionSeconds >= 0 ? "播放器进度 · 实时同步" : "等待播放器进度";
                // Reject a batch if the player changed songs during cache/OCR work.
                if (kugouSessionActive && KugouPlaybackTimeCapture.TryCaptureTitle() is { } latestTitle
                    && NormalizeTrackKey(latestTitle) != NormalizeTrackKey(trackTitle)) continue;
                }
            }
            catch
            {
                // External music software may close while UI Automation walks its tree.
            }

            if (!token.IsCancellationRequested)
            {
                LyricFrame? capturedFrame = kugouFrame;
                MediaTrackInfo? capturedTrackInfo = trackInfo;
                string? capturedTrackTitle = trackTitle;
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        ApplyTrackInfo(capturedTrackInfo);
                        ApplyTrackTitle(capturedTrackTitle);
                        if (capturedTimeline.Length > 0) lyricTimeline = capturedTimeline;
                        if (capturedTrackInfo is null && lyricClock.HasPosition)
                            lyricClock.Sample(GetPlaybackPositionSeconds(), false);
                        lyricTimingStatus = timingStatus;
                        ApplyCapturedLyric(capturedFrame);
                    }));
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }
            }

            try
            {
                Task.Delay(150, token).Wait(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static MediaTrackInfo? MatchTrackIdentity(MediaTrackInfo? info, string? title, bool playing)
    {
        if (info is not { } track || string.IsNullOrWhiteSpace(title)
            || NormalizeTrackKey(track.Title) == NormalizeTrackKey(title)) return info;
        // A new title must not inherit the old song's clock or duration while
        // Windows metadata and the player's own controls are updating separately.
        return new MediaTrackInfo(title, -1, -1, playing);
    }

    private void ApplyTrackInfo(MediaTrackInfo? trackInfo)
    {
        if (closing || IsDisposed || trackInfo is not { } info)
        {
            return;
        }

        string infoKey = NormalizeTrackKey(info.Title);
        bool titleChanged = !string.Equals(currentTrackKey, infoKey, StringComparison.Ordinal);
        bool durationChanged = info.DurationSeconds >= 2 && Math.Abs(songDurationSeconds - info.DurationSeconds) > 1.0;
        bool repeated = !titleChanged && info.PositionSeconds is >= 0 and < 5 && lastTrackPositionSeconds > songDurationSeconds * .85;
        if (titleChanged || repeated)
        {
            ResetSongChart(info.DurationSeconds >= 2 ? info.DurationSeconds : songDurationSeconds, info.Title);
        }

        if (info.DurationSeconds >= 2) songDurationSeconds = info.DurationSeconds;
        trackIsPlaying = info.IsPlaying;
        if (durationChanged) minutePointCapacity = Math.Clamp((int)Math.Ceiling(songDurationSeconds / 0.08), 60, MinutePointCount);
        if (info.PositionSeconds >= 0)
        {
            songPositionSeconds = Math.Clamp(info.PositionSeconds, 0, songDurationSeconds);
            lastTrackPositionSeconds = songPositionSeconds;
            lyricClock.Sample(songPositionSeconds, info.IsPlaying);
        }

        SetTrackStatus($"{currentTrackTitle} | 时长 {FormatSongTime(songDurationSeconds)} | 播放 {FormatSongTime(Math.Max(0, songPositionSeconds))}");
    }

    private void ApplyTrackTitle(string? title)
    {
        if (closing || IsDisposed || string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        string cleanTitle = title.Trim();
        if (!string.Equals(currentTrackKey, NormalizeTrackKey(cleanTitle), StringComparison.Ordinal))
        {
            ResetSongChart(songDurationSeconds, cleanTitle);
        }

        if (lastTrackPositionSeconds < 0)
        {
            SetTrackStatus($"{currentTrackTitle} | 歌曲时长 {FormatSongTime(songDurationSeconds)} | 播放位置等待播放器");
        }
    }

    private void SetTrackStatus(string value)
    {
        trackStatus = value;
        if (trackStatusLabel is { IsDisposed: false })
        {
            trackStatusLabel.Text = $"曲目：{value}";
        }
    }

    private void ApplyCapturedLyric(LyricFrame? kugouFrame)
    {
        if (closing || IsDisposed || !autoCaptureLyrics)
        {
            return;
        }

        if (kugouFrame is { } frame)
        {
            string source = string.IsNullOrWhiteSpace(frame.TimeTag)
                ? $"歌词来源：{frame.Source}"
                : $"歌词来源：{frame.Source} @ {frame.TimeTag}";
            if (lyricTimeline.Length > 0 && lyricClock.HasPosition)
            {
                int index = LyricPlaybackClock.FindActive(lyricTimeline, GetPlaybackPositionSeconds());
                activeLyricIndex = index;
                if (index >= 0) currentLyric = lyricTimeline[index].Text;
                if (lyricSourceLabel is not null) lyricSourceLabel.Text = source;
            }
            else if (!string.Equals(frame.Text, currentLyric, StringComparison.Ordinal))
            {
                SetLyric(frame.Text, source);
            }
            else if (lyricSourceLabel is not null)
            {
                lyricSourceLabel.Text = source;
            }
            return;
        }

        if (lyricSourceLabel is not null)
        {
            lyricSourceLabel.Text = "歌词来源：等待匹配时间轴或播放器桌面歌词";
        }
    }

    private void SetLyric(string text, string source)
    {
        string clean = text.Trim();
        if (clean.Length == 0 || string.Equals(clean, currentLyric, StringComparison.Ordinal))
        {
            return;
        }

        SpawnLyricParticles();
        currentLyric = clean;
        if (lyricTimeline.Length == 0)
        {
            liveLyricHistory.Add(new TimedLyricLine(GetPlaybackPositionSeconds(), clean));
            if (liveLyricHistory.Count > 200) liveLyricHistory.RemoveAt(0);
        }
        if (lyricSourceLabel != null)
        {
            lyricSourceLabel.Text = source;
        }
    }

    private static string NormalizeTrackKey(string? text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : Regex.Replace(text.ToLowerInvariant(), @"[^\p{L}\p{Nd}\u4e00-\u9fff]", string.Empty);
    }

    private void SpawnLyricParticles()
    {
        if (string.IsNullOrWhiteSpace(currentLyric))
        {
            return;
        }

        int count = Math.Min(120, Math.Max(28, currentLyric.Length * 3));
        for (int i = 0; i < count; i++)
        {
            float angle = (float)(random.NextDouble() * Math.PI * 2);
            float speed = 0.4f + random.NextSingle() * 2.2f;
            particles.Add(new LyricParticle
            {
                X = 0.50f + (random.NextSingle() - 0.5f) * 0.34f,
                Y = 0.105f + (random.NextSingle() - 0.5f) * 0.035f,
                Vx = MathF.Cos(angle) * speed / 700f,
                Vy = MathF.Sin(angle) * speed / 420f - 0.001f,
                Life = 0.65f + random.NextSingle() * 0.75f,
                Size = 1.2f + random.NextSingle() * 3.2f,
                Alpha = 1
            });
        }
    }

    private void DrawStage(Graphics g, Rectangle bounds)
    {
        DrawTerminalStage(g, bounds);
    }

    private static Rectangle FitAspect(Rectangle bounds, float aspect, int margin)
    {
        int availableWidth = Math.Max(1, bounds.Width - margin * 2);
        int availableHeight = Math.Max(1, bounds.Height - margin * 2);
        int width = availableWidth;
        int height = (int)MathF.Round(width / aspect);
        if (height > availableHeight)
        {
            height = availableHeight;
            width = (int)MathF.Round(height * aspect);
        }

        int x = bounds.Left + (bounds.Width - width) / 2;
        int y = bounds.Top + (bounds.Height - height) / 2;
        return new Rectangle(x, y, width, height);
    }

    private void DrawTimelineHeader(Graphics g, Rectangle bounds)
    {
        using var titleFont = new Font("Microsoft YaHei UI", 15, FontStyle.Bold);
        using var smallFont = new Font("Microsoft YaHei UI", 9, FontStyle.Regular);
        using var titleBrush = new SolidBrush(Color.FromArgb(20, 24, 30));
        using var mutedBrush = new SolidBrush(Color.FromArgb(86, 96, 112));
        string mode = simulationMode ? "模拟频谱" : liveAudio ? "系统音频采集中" : "等待音频";
        string chartText = chartMode == ChartMode.MinuteLine
            ? $"分时图 | 采样 {FormatFrequency(BandCenterFrequency(minuteBandIndex))}"
            : $"日K | 滚动窗口 {Math.Min(timelineCandleCount, VisibleTimelineCandleCount)}/{VisibleTimelineCandleCount} 根";
        g.DrawString("频谱股票盘面", titleFont, titleBrush, 42, 24);
        g.DrawString($"{mode} | {chartText} | 涨跌模式：{FormatTrendMode(trendMode)} | {trackStatus} | 视图 ±{displayRange * 100:0.0}%", smallFont, mutedBrush, 210, 31);
    }

    private void DrawHeader(Graphics g, Rectangle bounds)
    {
        using var titleFont = new Font("Microsoft YaHei UI", 15, FontStyle.Bold);
        using var smallFont = new Font("Microsoft YaHei UI", 9, FontStyle.Regular);
        using var titleBrush = new SolidBrush(Color.FromArgb(20, 24, 30));
        using var mutedBrush = new SolidBrush(Color.FromArgb(86, 96, 112));
        string mode = simulationMode ? "模拟频谱" : liveAudio ? "系统音频采集中" : "等待音频";
        string chartText = chartMode == ChartMode.MinuteLine
            ? $"分时图  |  采样 {FormatFrequency(BandCenterFrequency(minuteBandIndex))}"
            : $"日K  |  {candles.Length} 个对数频段  |  周期 {candleDurationSeconds:0.00} 秒";
        g.DrawString("频谱股票盘面", titleFont, titleBrush, 42, 24);
        g.DrawString($"{mode}  |  {chartText}  |  视图 +/-{displayRange * 100:0.0}% / 限幅 20%", smallFont, mutedBrush, 210, 31);
    }

    private void DrawGrid(Graphics g, Rectangle plot)
    {
        using var gridPen = new Pen(Color.FromArgb(224, 228, 234), 1);
        using var axisPen = new Pen(Color.FromArgb(176, 184, 196), 1);
        using var zeroPen = new Pen(Color.FromArgb(60, 66, 76), 1.7f);
        using var labelBrush = new SolidBrush(Color.FromArgb(86, 96, 112));
        using var font = new Font("Microsoft YaHei UI", 8, FontStyle.Regular);

        float[] yValues = [displayRange, displayRange * 0.5f, 0, -displayRange * 0.5f, -displayRange];
        for (int i = 0; i < yValues.Length; i++)
        {
            float value = yValues[i];
            float y = ValueY(plot, value);
            g.DrawLine(i == 2 ? zeroPen : gridPen, plot.Left, y, plot.Right, y);
            g.DrawString(FormatPercent(value), font, labelBrush, 8, y - 8);
        }

        string[] labels = chartMode == ChartMode.DayK
            ? ["50根前", "40根前", "30根前", "20根前", "10根前", "5根前", "当前", "最新"]
            : ["40Hz", "100", "250", "630", "1.6k", "4k", "10k", "16k"];
        for (int i = 0; i < labels.Length; i++)
        {
            float x = plot.Left + plot.Width * i / (labels.Length - 1f);
            g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
            g.DrawString(labels[i], font, labelBrush, x - 14, plot.Bottom + 10);
        }

        g.DrawRectangle(axisPen, plot);
    }

    private void DrawDayKCandles(Graphics g, Rectangle plot)
    {
        var all = GetDayCandles();
        var range = dayViewport.Range(all.Length);
        int visibleCount = range.End - range.Start;
        int startIndex = range.Start;
        int count = Math.Max(1, dayViewport.Capacity);
        float lane = plot.Width / (float)count;
        float candleWidth = Math.Max(1.5f, lane * 0.68f);
        using var riseBrush = new SolidBrush(Color.FromArgb(226, 40, 52));
        using var fallBrush = new SolidBrush(TerminalTheme.Cyan);
        using var flatBrush = new SolidBrush(TerminalTheme.Text);
        using var risePen = new Pen(TerminalTheme.Rise, Math.Max(1.0f, Math.Min(1.5f, lane * 0.12f)));
        using var fallPen = new Pen(TerminalTheme.Cyan, Math.Max(1.0f, Math.Min(1.5f, lane * 0.12f)));
        using var flatPen = new Pen(TerminalTheme.Text, Math.Max(1.0f, Math.Min(1.5f, lane * 0.12f)));

        for (int visibleIndex = 0; visibleIndex < visibleCount; visibleIndex++)
        {
            int i = startIndex + visibleIndex;
            Candle candle = all[i];
            float bodyMove = candle.Close - candle.Open;
            bool flat = Math.Abs(bodyMove) < CloseQuantum * 0.55f;
            bool rise = bodyMove > 0;
            float x = plot.Left + lane * (i-dayViewport.Start) + lane * 0.5f;
            float openY = ValueY(plot, candle.Open);
            float closeY = ValueY(plot, candle.Close);
            float highY = ValueY(plot, candle.High);
            float lowY = ValueY(plot, candle.Low);
            float bodyTop = Math.Min(openY, closeY);
            float bodyHeight = flat ? 2.0f : Math.Max(2.5f, Math.Abs(closeY - openY));

            var pen = flat && candle.LimitDirection != 0 ? candle.LimitDirection > 0 ? risePen : fallPen : flat ? flatPen : rise ? risePen : fallPen;
            var brush = flat && candle.LimitDirection != 0 ? candle.LimitDirection > 0 ? riseBrush : fallBrush : flat ? flatBrush : rise ? riseBrush : fallBrush;
            g.DrawLine(pen, x, highY, x, lowY);
            var body = new RectangleF(x - candleWidth / 2f, bodyTop, candleWidth, bodyHeight);
            if (!rise || flat) g.FillRectangle(brush, body);
            g.DrawRectangle(pen, body.X, body.Y, body.Width, body.Height);
            if (candle.Pattern == CandlePattern.Doji)
            {
                g.DrawLine(pen, body.Left - 1, (openY + closeY) * 0.5f, body.Right + 1, (openY + closeY) * 0.5f);
            }
        }
    }

    private void DrawCandles(Graphics g, Rectangle plot)
    {
        float lane = plot.Width / (float)candles.Length;
        float candleWidth = Math.Max(4f, lane * 0.58f);
        using var riseBrush = new SolidBrush(Color.FromArgb(226, 40, 52));
        using var fallBrush = new SolidBrush(Color.FromArgb(20, 150, 72));
        using var flatBrush = new SolidBrush(Color.FromArgb(18, 22, 28));
        using var risePen = new Pen(Color.FromArgb(196, 28, 40), 1.2f);
        using var fallPen = new Pen(Color.FromArgb(16, 122, 58), 1.2f);
        using var flatPen = new Pen(Color.FromArgb(18, 22, 28), 1.2f);

        for (int i = 0; i < candles.Length; i++)
        {
            var candle = candles[i];
            float bodyMove = candle.Close - candle.Open;
            bool flat = Math.Abs(bodyMove) < CloseQuantum * 0.55f;
            bool rise = bodyMove > 0;
            float x = plot.Left + lane * i + lane * 0.5f;
            float openY = ValueY(plot, candle.Open);
            float closeY = ValueY(plot, candle.Close);
            float highY = ValueY(plot, candle.High);
            float lowY = ValueY(plot, candle.Low);
            float bodyTop = Math.Min(openY, closeY);
            float bodyHeight = flat ? 2.0f : Math.Max(2.5f, Math.Abs(closeY - openY));

            var pen = flat ? flatPen : rise ? risePen : fallPen;
            var brush = flat ? flatBrush : rise ? riseBrush : fallBrush;
            g.DrawLine(pen, x, highY, x, lowY);
            var body = new RectangleF(x - candleWidth / 2f, bodyTop, candleWidth, bodyHeight);
            g.FillRectangle(brush, body);
            g.DrawRectangle(pen, body.X, body.Y, body.Width, body.Height);

            if (candle.Pattern == CandlePattern.Doji)
            {
                g.DrawLine(pen, body.Left - 1, (openY + closeY) * 0.5f, body.Right + 1, (openY + closeY) * 0.5f);
            }
        }
    }

    private void DrawMinuteLine(Graphics g, Rectangle plot)
    {
        var values = GetMinuteValuesInOrder().ToArray();
        using var linePen = new Pen(Color.FromArgb(10, 12, 16), 2.0f)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        using var pointBrush = new SolidBrush(Color.FromArgb(10, 12, 16));
        using var labelBrush = new SolidBrush(Color.FromArgb(10, 12, 16));
        using var font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold);

        if (values.Length < 2)
        {
            float y = ValueY(plot, 0);
            g.DrawLine(linePen, plot.Left, y, plot.Right, y);
            g.DrawString("等待采样", font, labelBrush, plot.Left + 8, plot.Top + 8);
            return;
        }

        PointF[] points = new PointF[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            float x = plot.Left + plot.Width * i / Math.Max(1, minutePointCapacity - 1f);
            float y = ValueY(plot, values[i]);
            points[i] = new PointF(x, y);
        }

        g.DrawLines(linePen, points);
        PointF last = points[^1];
        g.FillEllipse(pointBrush, last.X - 3.2f, last.Y - 3.2f, 6.4f, 6.4f);

        string label = FormatPercent(values[^1]);
        SizeF labelSize = g.MeasureString(label, font);
        float labelX = Math.Min(plot.Right - labelSize.Width - 4, last.X + 8);
        float labelY = Math.Clamp(last.Y - labelSize.Height * 0.5f, plot.Top + 2, plot.Bottom - labelSize.Height - 2);
        g.DrawString(label, font, labelBrush, labelX, labelY);

        string subtitle = $"分时采样频率：{FormatFrequency(BandCenterFrequency(minuteBandIndex))}";
        using var mutedBrush = new SolidBrush(Color.FromArgb(86, 96, 112));
        using var smallFont = new Font("Microsoft YaHei UI", 8, FontStyle.Regular);
        g.DrawString(subtitle, smallFont, mutedBrush, plot.Left + 8, plot.Top + 8);
    }

    private void DrawLyrics(Graphics g, Rectangle bounds)
    {
        if (!lyricsVisible)
        {
            return;
        }

        var lyricBand = new Rectangle(bounds.Left + 42, bounds.Top + 44, bounds.Width - 84, 32);
        using var bandBrush = new SolidBrush(Color.FromArgb(238, 240, 244));
        using var font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold);
        using var shadow = new SolidBrush(Color.FromArgb(110, 255, 255, 255));
        using var textBrush = new SolidBrush(Color.FromArgb(18, 22, 28));
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        g.FillRectangle(bandBrush, lyricBand);
        var shadowRect = lyricBand;
        shadowRect.Offset(2, 2);
        g.DrawString(currentLyric, font, shadow, shadowRect, format);
        g.DrawString(currentLyric, font, textBrush, lyricBand, format);

        using var particleBrush = new SolidBrush(Color.White);
        foreach (var particle in particles)
        {
            int alpha = (int)(particle.Alpha * 180);
            particleBrush.Color = Color.FromArgb(alpha, 40, 44, 52);
            float px = bounds.Left + particle.X * bounds.Width;
            float py = bounds.Top + particle.Y * bounds.Height;
            g.FillEllipse(particleBrush, px, py, particle.Size, particle.Size);
        }
    }

    private float ValueY(Rectangle plot, float value)
    {
        if (terminalAxis is { } axis && plot == terminalPlot) return axis.Y(plot, MusicPrice(value));
        float range = Math.Clamp(displayRange, MinDisplayRange, 4.0f);
        float normalized = Math.Clamp(value, -range, range) / range;
        return plot.Top + plot.Height * 0.5f - normalized * plot.Height * 0.5f;
    }

    private static string FormatPercent(float value)
    {
        if (Math.Abs(value) < 0.0001f)
        {
            return "0%";
        }

        return $"{value * 100:+0.0;-0.0}%";
    }

    private static string FormatSongTime(double seconds)
    {
        seconds = Math.Max(0, seconds);
        int totalSeconds = (int)Math.Round(seconds);
        return totalSeconds >= 3600
            ? $"{totalSeconds / 3600}:{totalSeconds / 60 % 60:00}:{totalSeconds % 60:00}"
            : $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    private static float BandCenterFrequency(int band)
    {
        const float minFreq = 38f;
        const float maxFreq = 16000f;
        float a = (band + 0.5f) / BandCount;
        return minFreq * MathF.Pow(maxFreq / minFreq, a);
    }

    private static string FormatFrequency(float frequency)
    {
        return frequency >= 1000f ? $"{frequency / 1000f:0.00} kHz" : $"{frequency:0} Hz";
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (HandleDayNavigationKey(e)) return;
        if(DateTime.Now-indicatorCodeAt>TimeSpan.FromSeconds(4))indicatorCode=string.Empty;
        if(stage.ContainsFocus&&e.KeyCode is >=Keys.A and <=Keys.Z&&!e.Control&&!e.Alt)return;
        if(e.KeyCode==Keys.Escape&&indicatorCode.Length>0){indicatorCode=string.Empty;stage.Invalidate();e.Handled=true;return;}
        if (e.KeyCode == Keys.F11)
        {
            ToggleFullscreen();
        }
        else if (e.KeyCode == Keys.Escape && fullscreen)
        {
            ToggleFullscreen();
        }
        else if (e.KeyCode == Keys.L)
        {
            lyricsVisible = !lyricsVisible;
        }
        else if (e.KeyCode == Keys.M)
        {
            simulationMode = !simulationMode;
        }
    }

    private void ToggleFullscreen()
    {
        if (!fullscreen)
        {
            oldBounds = Bounds;
            oldBorderStyle = FormBorderStyle;
            if (controlPanel != null)
            {
                controlPanel.Visible = false;
            }
            if (menuBar != null)
            {
                menuBar.Visible = false;
            }

            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            Bounds = Screen.FromControl(this).Bounds;
            fullscreen = true;
        }
        else
        {
            FormBorderStyle = oldBorderStyle;
            Bounds = oldBounds;
            if (controlPanel != null)
            {
                controlPanel.Visible = terminalSettingsVisible;
            }
            if (menuBar != null)
            {
                menuBar.Visible = true;
            }

            fullscreen = false;
        }
    }

    private struct Candle
    {
        public float Open;
        public float Close;
        public float High;
        public float Low;
        public float Velocity;
        public CandlePattern Pattern;
        public int LimitDirection;
    }

    private enum CandlePattern
    {
        HighOpenDown,
        LowOpenUp,
        HighOpenUp,
        LowOpenDown,
        Flat,
        Doji
    }

    private enum ChartMode
    {
        DayK,
        MinuteLine
    }

    private enum TrendMode
    {
        Down,
        Normal,
        Up
    }

    private struct LyricParticle
    {
        public float X;
        public float Y;
        public float Vx;
        public float Vy;
        public float Life;
        public float Age;
        public float Size;
        public float Alpha;
    }

    private sealed class StagePanel : Panel
    {
        internal Keys WheelModifiers;
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x020A)
            {
                long flags = m.WParam.ToInt64();
                WheelModifiers = ((flags & 8) != 0 ? Keys.Control : Keys.None) | ((flags & 4) != 0 ? Keys.Shift : Keys.None);
                var screen = new Point(unchecked((short)m.LParam.ToInt64()), unchecked((short)(m.LParam.ToInt64() >> 16)));
                var point = PointToClient(screen);
                OnMouseWheel(new MouseEventArgs(MouseButtons.None, 0, point.X, point.Y, unchecked((short)(flags >> 16))));
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Action<Graphics, Rectangle>? Render { get; set; }

        public StagePanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.Selectable, true);
            BackColor = TerminalTheme.Background;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            try
            {
                Render?.Invoke(e.Graphics, ClientRectangle);
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
