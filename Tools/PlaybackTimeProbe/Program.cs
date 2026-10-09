using SpectrumKlinePlayer;
class Probe {
[STAThread] static void Main(string[] args){
if(args.Contains("--windows")){WindowProbe.Run();return;}
for(int i=0;i<4;i++){var track=WindowsMediaSessionCapture.TryCapture();if(track is {} t){var actual=KugouPlaybackTimeCapture.TryCapture(t.Title,t.IsPlaying);Console.WriteLine($"{DateTime.Now:HH:mm:ss} {actual} status={KugouPlaybackTimeCapture.LastStatus}");}Thread.Sleep(800);}
}
}
