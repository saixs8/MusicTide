namespace SpectrumKlinePlayer;

public sealed class DayKViewport
{
    public int Capacity { get; private set; } = 50;
    public int Start { get; private set; }
    public bool Following { get; private set; } = true;
    public (int Start, int End) Range(int total)
    {
        int length = Math.Min(Math.Max(0,total), Capacity);
        Start = Following ? Math.Max(0,total-Capacity) : Math.Clamp(Start,Math.Min(0,total-Capacity),Math.Max(0,total-Capacity));
        return (Math.Max(0,Start),Math.Min(total,Start+Capacity));
    }
    public void MoveTo(int start,int total)
    {
        int last=Math.Max(0,total-Math.Min(total,Capacity));
        Start=Math.Clamp(start,Math.Min(0,total-Capacity),last); Following=Start==last;
    }
    public void Pan(int delta,int total) { Range(total); MoveTo(Start+delta,total); }
    public void Zoom(int direction,int total,double anchor=.5)
    {
        var old=Range(total); bool following=Following;
        int next=Math.Clamp((int)Math.Round(Capacity*(direction>0?.8:1.25)),12,500);
        if(next==Capacity)return;
        double position=Start+Capacity*Math.Clamp(anchor,0,1);
        Capacity=next;
        if(following) { Range(total); return; }
        MoveTo((int)Math.Round(position-Capacity*Math.Clamp(anchor,0,1)),total);
    }
    public void Latest(int total) { Following=true; Range(total); }
    public void RemoveOldest() { Start=Math.Max(0,Start-1); }
    public void Reset() { Capacity=50;Start=0;Following=true; }
}
