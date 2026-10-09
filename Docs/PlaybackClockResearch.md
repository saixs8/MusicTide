# Playback clock fix — v0.2.0 (2026-10-08)

## GitHub research

- https://github.com/StarCompute/bluemusic/blob/master/src/main.cpp
  Registers AVRCP metadata and playback-position callbacks. Position notifications provide milliseconds; metadata includes track duration. Lyrics over Bluetooth require the player to publish lyric content in its metadata.
- https://github.com/pschatzmann/ESP32-A2DP/blob/main/examples/bt_music_receiver_with_metadata/bt_music_receiver_with_metadata.ino
  Demonstrates AVRCP metadata reception. Track duration metadata alone is not the current playback position.

The Windows application needs the same authoritative position input. No Bluetooth stack or third-party GPL source was copied into the project.

## Root cause and implemented acquisition

KuGou publishes the title and playing state in SMTC, but no timeline. Its standalone accessibility clock label contains MM:SS/MM:SS without a song title. The earlier generic parser rejected that label because it tried to extract a title from the same text.

Source/KugouPlaybackTimeCapture.cs now locates the owning KuGou main window, validates its title against the current media track, reads the clock element directly and caches it. Accessibility names are fetched together via CacheRequest on the initial traversal; subsequent reads use the cached element.

The shipping code is read-only: it does not restore, move, activate or minimize KuGou. The actual clock was verified while KuGou remained minimized and desktop lyrics were disabled.

Whole-second repeated samples retain UI interpolation rather than rewinding the clock. Authoritative player time has priority over OCR lyric anchors. Current lyrics stay enlarged at the top even during history browsing.

## Actual observations

Independent playback probe: 01:46 -> 01:47 -> 01:48.
Actual application mid-song check: 184.06 -> 186.59 -> 188.40 seconds, with 67 cached lyric lines and current index 45 -> 46.
Subsequent actual run: 47.19 -> 49.75 -> 51.94 seconds, with 57 lines and index 8 -> 9.
See Logs/PlaybackClockLiveCheck.log and UiPreview/Terminal_LiveLyrics.png.

21 automated regressions passed; Release publish succeeded. Pause and seek behaviors were checked by regression samples; no user playback controls were changed to run those checks.
