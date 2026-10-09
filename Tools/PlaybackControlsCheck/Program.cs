using SpectrumKlinePlayer;
using Windows.Media.Control;

class Check
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var form = new Form1();
        form.Show();
        for (int i = 0; i < 60; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
        var track = WindowsMediaSessionCapture.TryCapture();
        var session = WindowsMediaSessionCapture.ActiveSession ?? throw new Exception("No active music session");
        var controls = session.GetPlaybackInfo().Controls;
        Console.WriteLine($"Source={session.SourceAppUserModelId}; Track={track?.Title}");
        Console.WriteLine($"Previous={controls.IsPreviousEnabled}; Next={controls.IsNextEnabled}; Stop={controls.IsStopEnabled}; Toggle={controls.IsPlayPauseToggleEnabled}; Pause={controls.IsPauseEnabled}");
        var source = session.SourceAppUserModelId;
        Console.WriteLine($"Background volume={Task.Run(() => MusicAudioVolume.Read(source)).GetAwaiter().GetResult()}");
        var original = MusicAudioVolume.Read(source) ?? throw new Exception("No music audio volume");
        Console.WriteLine($"Original volume={original.Level:F3}; Muted={original.Muted}");
        try
        {
            float target = original.Level > .9f ? original.Level - .05f : original.Level + .05f;
            if (!Task.Run(() => MusicAudioVolume.Set(source, target)).GetAwaiter().GetResult()) throw new Exception("Volume write failed");
            var changed = MusicAudioVolume.Read(source) ?? throw new Exception("Volume read failed");
            if (Math.Abs(changed.Level - target) > .005f) throw new Exception("Volume mismatch");
            Console.WriteLine($"Volume readback={changed.Level:F3}: PASS");
            if (!Task.Run(() => MusicAudioVolume.Set(source, muted: !original.Muted)).GetAwaiter().GetResult()) throw new Exception("Mute write failed");
            if (MusicAudioVolume.Read(source)?.Muted != !original.Muted) throw new Exception("Mute mismatch");
            Console.WriteLine("Mute readback: PASS");
        }
        finally
        {
            MusicAudioVolume.Set(source, original.Level, original.Muted);
            Console.WriteLine($"Restored volume={MusicAudioVolume.Read(source)}");
        }
        bool playing = session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        if (playing)
        {
            try
            {
                bool result = WindowsMediaSessionCapture.ControlAsync(PlayerCommand.Toggle).GetAwaiter().GetResult();
                System.Threading.Thread.Sleep(400);
                var status = session.GetPlaybackInfo().PlaybackStatus;
                Console.WriteLine($"Pause accepted={result}; Status={status}");
                if (!result || status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) throw new Exception("Pause failed");
            }
            finally
            {
                Console.WriteLine($"Resume accepted={session.TryPlayAsync().GetAwaiter().GetResult()}");
            }
        }
        form.Close();
    }
}
