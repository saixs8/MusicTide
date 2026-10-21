# MusicTide · 音潮行情

[简体中文](README.md) | **English**

**v0.9.12 · Windows music visualization desktop app · Source available for non-commercial use only**

Turn the music playing on your computer into a live spectrum, virtual stock charts and candlesticks. MusicTide combines synchronized lyrics, local AI emotion analysis, chorus-driven market events and song comparisons. Move the window to a USB/HDMI secondary display and use full-screen mode.

> Reading, studying, modifying and non-commercial redistribution are permitted. **Commercial use requires prior written authorization.** This includes selling the software, paid deployment, commercial product integration, business operations and monetized content. See [LICENSE](LICENSE). The project uses a custom non-commercial source license, not MIT or GPL, and is not presented as OSI-approved open source. The Chinese license text is authoritative; this README is a summary.

All security codes, prices, volumes and financial indicators are virtual values generated from music for visualization and entertainment. The desktop UI and most detailed documentation are currently in Chinese; this file provides English instructions.

## Downloads and installation

| Download | Purpose |
|---|---|
| [MusicTideSetup-v0.9.12.exe](https://github.com/saixs8/MusicTide/releases/download/v0.9.12/MusicTideSetup-v0.9.12.exe) | Installer with private .NET and Ollama CPU runtimes |
| [MusicTide-Source-v0.9.12.zip](https://github.com/saixs8/MusicTide/releases/download/v0.9.12/MusicTide-Source-v0.9.12.zip) | Source snapshot, tools, documentation and licenses; excludes models and personal data |
| `Output-Major/MusicTide.exe` | Local manually published build; requires .NET 9 Desktop Runtime and the adjacent dependencies |
| [Setup-SHA256.json](https://github.com/saixs8/MusicTide/releases/download/v0.9.12/Setup-SHA256.json) | Installer SHA256 checksum |

Find the current published version on [Releases](https://github.com/saixs8/MusicTide/releases). Release archives are snapshots of their respective versions; the READMEs on the main branch may receive later documentation updates.

**Requirements:** Windows 10 version 2004/build 19041 or later, or Windows 11, on Intel/AMD x64. Native ARM64 operation has not been verified. Development requires the .NET 9 SDK.

### Full or offline installation

- **Full installation:** includes the app, private .NET 9.0.20 and Ollama 0.40.1 CPU runtimes. Downloads approximately 986 MB for `qwen2.5:1.5b`, with SHA256 verification and resumable downloads.
- **Offline installation:** skips the model download. Charts, spectrum and player-provided/local lyrics remain available. Online lyric lookup still requires a connection; basic local keyword learning can run without the AI model.
- If the full installation has no network connection or the model download fails, installation can finish in offline mode while retaining download progress.
- While the model is missing, each launch offers to install it, postpone, or stop asking. If reminders are disabled, run `MusicTideSetup.exe` in the installation root and select full installation later. Successful completion removes the reminder.

The default location is `%LOCALAPPDATA%\Programs\MusicTide`. You can type a different directory or choose one with the browse button, including paths with spaces or Chinese characters. AI completion uses the existing installation location. Choose a writable folder; installation does not require administrator privileges or modify the global PATH.

The app is stored in `App/`, private runtimes in `Runtime/`. Existing settings and song history are preserved. The dark installer displays installation stages, model download percentage, size and speed, with expandable logs. Click the launch button when installation finishes. See the [installation guide](Distribution/README.md) for details in Chinese.

## Quick start

1. Start MusicTide and play music in a supported player.
2. Filter the 100 frequency bands by board on the left and select a row. Clicking a column header cycles through ascending, descending and default order.
3. Switch the main chart between candlesticks (**日K**) and the intraday-style line (**分时**). The lyrics feed highlights the current line and lists upcoming lines in time order.
4. Use the song header for previous/next track, pause, stop, mute and volume.
5. Open **音乐玩法** for comparisons, market events, AI keyword learning and song history. The terminal settings panel is collapsed by default.
6. Use the bottom **模拟交易** toolbar for paper trading. Open **盈亏明细** to inspect positions, profit/loss and trade history.

## Features

| Feature | Current behavior |
|---|---|
| Live spectrum | 100 logarithmic bands; fixed energy bars follow the current audio independently of chart navigation |
| Virtual security codes | Low frequencies map to Shanghai/Shenzhen main boards; mid frequencies to ChiNext/STAR; high frequencies to Beijing board codes |
| Candlesticks | Price axis on the left, percentage on the right; visible-window scaling with the leftmost visible candle's open as the percentage reference |
| Intraday-style chart | White current-price line, yellow average-price line and centered 0%; virtual open/high/low, traded amount, turnover and volume |
| Indicators | MA/BOLL/EXPMA overlays; MACD/KDJ/RSI/WR/BIAS/CCI/ATR/ROC subcharts; up to four optional subcharts plus the fixed energy panel |
| Navigation | Ctrl+wheel zoom, left-button drag, return to latest and double-click a candle to inspect its recorded development |
| Virtual valuation | Random opening price from 5–100; simulated likes/comments can be replaced manually, while all valuation figures remain virtual |
| Song history | Default capacity of 20 songs, configurable from 1–1,000; up to five overlaid versions per song, favoring frequently replayed songs |
| Per-song sampling | New tracks and complete repeats choose different price/reference bands, avoiding the previous pair |
| Keyword learning | Five initial keywords; learns from heard lyric excerpts and uses local AI emotion intensity to generate 2–12-second effects |
| Paper trading | Inline long/short orders and closing, 1×/2×/3×/5×/10× leverage, margin accounting and a separate read-only P&L view |
| Music gameplay | Song comparisons, arena scores, paper trading, bull/bear dynamics, positive/negative mystery outcomes, chorus returns, lyric event cards and achievements |
| Chorus limit streaks | Random upward/downward streaks, at most one group per song; supports flat limit bars, opening gaps and occasional breaks |
| Silence handling | Market updates and animations stop; live energy becomes zero. Audio resumes without generating candles for the silent interval |
| Text and displays | Nine menu sizes from 10–24 pt, default 10 pt; F11 full-screen support |

The [full user guide](Docs/UserGuide.md) covers parameters, valuation formulas, gameplay rules and troubleshooting in Chinese. Custom USB CDC transmission and display firmware are **not implemented**; see the [hardware plan](Docs/USBSideScreenPlan.md).

### Controls

| Input | Action |
|---|---|
| Ctrl+mouse wheel / left-button drag | Zoom/pan the candlestick view |
| End / Home | Latest/earliest recorded candles |
| Left/right arrows / +/− | Pan/zoom when the chart has focus |
| Indicator name + Enter | Type BOLL, MACD, etc. after focusing the chart |
| Select a subchart, then a bottom indicator tab or right-click | Change its indicator; double-click the indicator name to edit parameters |
| Ctrl+M / Ctrl+L | Toggle manual simulated spectrum / show or hide lyrics |
| F11 / Esc | Toggle full screen / clear indicator input or leave full screen |

## Paper trading and P&L

The bottom toolbar places orders without opening another window. Each round starts with **100,000 virtual funds**. Choose **1×, 2×, 3×, 5× or 10×** leverage and a **25%, 50% or 100%** fraction. Supported actions are opening a long, opening a short, closing a fraction and closing the entire position. Leverage is locked while a position is open; close it before switching direction. Short positions are available at 1× as well.

For opening orders, the fraction applies to available margin. For closing orders, it applies to the held quantity. The toolbar shows the trading price, available funds, occupied margin, equity, position direction, cost, realized P&L and unrealized P&L. Changing the chart does not change the trading valuation basis.

The separate P&L window is read-only: inspect positions, fills and historical rounds there. Legacy records without per-fill realized P&L display `--` rather than an invented value.

The simulator uses cross-margin accounting:

```text
Equity = available funds + occupied margin + unrealized P&L
Long unrealized P&L = quantity × (current price − average entry price)
Short unrealized P&L = quantity × (average entry price − current price)
```

When equity reaches or falls below **20% of occupied margin**, the simulator closes the position at the sampled price. Price gaps can result in negative equity; simulated losses are retained. There are no fees, interest or slippage. Silent/paused playback cannot accept orders. Switching songs or starting a new round resets the simulated account while keeping history; restarting does not restore open positions. See [trading verification](Docs/TradingWorkbarVerification.md).

## Lyric support and limitations

- **KuGou:** real playback time and local KRC/LRC reading have been tested with a running client. With a valid clock and cache, desktop lyrics are not required and starting mid-song can align correctly.
- **QQ Music, NetEase Cloud Music and Soda Music:** separate adapters prioritize SMTC time and local LRC, then online matching, window text or OCR. Adapter and simulated-window tests have passed. Live verification across official client versions is still incomplete; compatibility with every version is not guaranteed.
- Custom-drawn windows, inaccessible controls, missing lyrics or unmatched timelines may prevent synchronization. Visible desktop lyrics can help fallback capture. A single repeated chorus line is not used to guess the song position.
- Place your own LRC files in `Data/Lyrics/` beside the program, named `Artist - Title.lrc` or `Title - Artist.lrc`. Retrieved lyrics are cached in `Data/LyricCache/`.
- If the feed stops following, click **自动跟随**. If the market moves during apparent silence, check manual simulation mode and audio from other apps: system loopback captures other applications too.

See [multi-player verification](Docs/MultiPlayerLyricsVerification.md) and [silence/installer verification](Docs/SilenceAndInstallerVerification.md) for the tested scope.

## Local AI, data and network access

AI uses local Ollama with `qwen2.5:1.5b` by default. Manual deployment defaults to port `11434`; fresh installer configuration uses `11534`. The app's settings determine the active endpoint. Limited heard lyric excerpts are sent to the local service; remote endpoints and cloud models are rejected. AI judgments may be inaccurate and can be reviewed or edited manually.

Model downloads and first-time online lyric matching require network access. Lyric matching sends metadata such as song title and artist. **Local AI processing does not mean every feature works without a network connection.**

Settings and history reside in `Settings/` and `Data/` beside the executable. Back up both directories before upgrading. Source packages exclude personal settings, listening history, lyric caches, models and private diagnostics. See [data and privacy](Docs/Privacy.md).

## Source, build and contributions

The app uses C#/WinForms, Windows WASAPI loopback, SMTC, UI Automation and Windows OCR. It does not depend on the source code of QQ Music, NetEase or Soda Music clients.

Run these commands from the project root:

```powershell
dotnet build Source/SpectrumKlinePlayer.csproj -c Release
dotnet publish Source/SpectrumKlinePlayer.csproj -c Release -o Output-Major
dotnet run --project Tools/SilenceVerification -c Release
dotnet run --project Tools/MultiPlayerVerification -c Release
dotnet run --project Tools/TradingVerification -c Release
```

The [development guide](Docs/Development.md) explains builds, installer prerequisites and clean source packaging. The app builds independently; a complete installer needs additional official runtime assets.

Recorded verification includes 18 silence/offline-reminder checks, 28 lyric regression checks and multi-player adapter checks. Version 0.9.12 adds 34 trading checks, 65 gameplay regression checks and minimum-window layout checks for four subcharts and the fixed energy panel. These do not imply live verification of all third-party client versions.

See [CONTRIBUTING.md](CONTRIBUTING.md). Repository: [saixs8/MusicTide](https://github.com/saixs8/MusicTide). Detailed linked documents are mostly in Chinese.

## Layout

```text
MusicTide/
├── README.md / README.en.md / LICENSE / THIRD_PARTY_NOTICES.md
├── CONTRIBUTING.md
├── Source/         Application source and icon
├── Tools/          Build, verification and diagnostic tools
├── Docs/           User, development, privacy and verification notes
├── Licenses/       Original third-party licenses
├── Distribution/   Current installer, source package and checksums
├── Output-Major/   Local published app and user data (excluded from source packages)
├── Build/          Installer assets, caches and sandboxes (excluded)
├── Runtime/        Local Ollama, models and optional diagnostics (excluded)
├── Assets/         Video cover assets (excluded)
└── Archive/        Historical backups (excluded)
```

[Documentation index](Docs/README.md) · [Tools](Tools/README.md) · [Folder organization](Docs/FolderOrganization.md) · [Third-party notices](THIRD_PARTY_NOTICES.md)

## License and project description

**MusicTide is a Windows desktop visualization tool for music enthusiasts. Its source is available for learning, research, modification and non-commercial sharing. Commercial use requires written authorization. It transforms playing music into a live spectrum, virtual candlesticks, lyric feeds and music-driven gameplay, with optional local AI lyric-emotion analysis. All market data is virtual.**

The project's own code uses the [custom non-commercial source license](LICENSE). .NET, Ollama, Qwen models and underlying libraries retain their original licenses. Lyrics, music and third-party brands are not relicensed by this project. See [third-party notices](THIRD_PARTY_NOTICES.md).
