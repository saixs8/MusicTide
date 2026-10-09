using SpectrumKlinePlayer;
using System.Reflection;
using System.Diagnostics;
using System.IO;
class Program
{
    [STAThread] static void Main()
    {
        using var form = new Form1();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object Get(string n) => typeof(Form1).GetField(n, flags)!.GetValue(form)!;
        void Set(string n, object v) => typeof(Form1).GetField(n, flags)!.SetValue(form,v);
        void Call(string n, params object[] args) => typeof(Form1).GetMethod(n, flags)!.Invoke(form,args);
        int count = 0;
        void Check(bool ok, string name) { if(!ok) throw new Exception(name); Console.WriteLine("PASS " + name); count++; }
        ((System.Windows.Forms.Timer)Get("frameTimer")).Stop();
        ((CancellationTokenSource)Get("lyricCaptureCts")).Cancel();
        ((AudioLoopbackCapture)Get("capture"))?.Dispose(); Set("capture",null!);
        Set("simulationMode",true); for(int i=0;i<8;i++){Call("OnFrame");Thread.Sleep(30);}
        Check((int)Get("timelineCandleCount")>0,"manual simulation still runs");
        Set("simulationMode",false); Set("liveAudio",true); Set("latestAudioRms",0f); Set("lastAudioPacketAt",Environment.TickCount64);
        float close=(float)Get("timelineClose"), minute=(float)Get("minuteLineValue"); int bars=(int)Get("timelineCandleCount");
        double volume=(double)Get("virtualVolume");
        float range=(float)Get("displayRange");
        Call("OnFrame");
        Check(((float[])Get("energyLevels")).All(v=>v==0),"first silent frame clears spectrum without a decay animation");
        Check((float)Get("musicEnergy")==0,"first silent frame clears gameplay energy");
        Check(((float[])Get("bandTargets")).All(v=>v==0),"quote table, sorting and ticker energies are zero");
        Set("currentLyric", "test"); Call("SpawnLyricParticles");
        var particles=(System.Collections.IList)Get("particles");
        object[] frozenParticles=particles.Cast<object>().ToArray();
        var timer=Stopwatch.StartNew();
        while(timer.Elapsed.TotalSeconds<2.2){Call("OnFrame");Thread.Sleep(30);}
        Check((float)Get("timelineClose")==close,"silence cannot drift K price");
        Check((float)Get("minuteLineValue")==minute,"silence cannot drift minute price");
        Check((int)Get("timelineCandleCount")==bars,"silence cannot create random new bars");
        Check((double)Get("virtualVolume")==volume,"silence cannot create fake volume");
        Check((float)Get("displayRange")==range,"silence cannot animate chart axes");
        Check(particles.Cast<object>().SequenceEqual(frozenParticles),"silence cannot animate lyric particles");
        Check(((float[])Get("energyLevels")).All(v=>v==0),"live energy decays to exact zero");
        Set("latestAudioRms",.2f); Set("lastAudioPacketAt",Environment.TickCount64-1000);Call("OnFrame");
        Check(!(bool)Get("hasAudioSignal")&&(float)Get("timelineClose")==close,"stale audio packets do not restart market");
        Call("ProcessCapturedBuffer",IntPtr.Zero,100,new WaveFormatInfo{Channels=1,BitsPerSample=8,SampleRate=48000},true);
        Check((float)Get("latestAudioRms")==0,"WASAPI silent 8-bit PCM must be zero, not minus one");
        Set("latestAudioRms",.1f);Set("lastAudioPacketAt",Environment.TickCount64);Call("OnFrame");
        Check((bool)Get("hasAudioSignal"),"fresh audible input resumes market");
        Check((int)Get("timelineCandleCount")<=bars+1,"resume has no silence-length catch-up burst");
        var optional=typeof(Form1).Assembly.GetType("SpectrumKlinePlayer.OptionalAiInstall")!;
        var ask=optional.GetMethod("ShouldAsk",BindingFlags.Static|BindingFlags.NonPublic)!;
        string preference=Path.Combine(AppContext.BaseDirectory,"ai-prompt-test.json");
        bool ShouldAsk() => (bool)ask.Invoke(null,[preference])!;
        try {
            if(File.Exists(preference)) File.Delete(preference);
            Check(!ShouldAsk(),"complete installation has no startup prompt");
            File.WriteAllText(preference,"{\"DontAskAgain\":false}");
            Check(ShouldAsk()&&ShouldAsk(),"offline installation asks on every startup");
            File.WriteAllText(preference,"{\"DontAskAgain\":true}");
            Check(!ShouldAsk(),"do not ask preference survives reload");
        } finally { if(File.Exists(preference)) File.Delete(preference); }
        Console.WriteLine($"All {count} silence checks passed");
    }
}
