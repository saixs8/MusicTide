# Lyric synchronization verification — v0.2.0

2026-10-08: Release build/publish succeeded. 21 regression checks passed.

The previous estimated-clock check is superseded by real KuGou clock acquisition. The root cause was rejecting a separate accessibility time label because the label did not contain the song title.

Actual KuGou clock probe while minimized: 01:46 -> 01:47 -> 01:48.
Actual program started mid-song at 184.06 seconds, then advanced to 186.59 and 188.40. Loaded 67 lyric lines; active index changed 45 -> 46.
A subsequent run read 47.19 -> 49.75 -> 51.94, loaded 57 lines, and advanced index 8 -> 9.

Terminal_LiveLyrics.png shows an actual application capture with authoritative player progress, current lyrics enlarged at the top, and later lyrics below. Other Terminal_*.png files use demo data for layout inspection.

Research and implementation details: PlaybackClockResearch.md.
Reproduce: dotnet run --project Tools/PlaybackTimeProbe/PlaybackTimeProbe.csproj -c Release.
Regressions: dotnet run --project Tools/LyricVerification/LyricVerification.csproj -c Release.
