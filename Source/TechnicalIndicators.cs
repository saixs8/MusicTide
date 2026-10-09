namespace SpectrumKlinePlayer;

public enum TechnicalIndicator { MA, BOLL, EXPMA, MACD, KDJ, RSI, WR, BIAS, CCI, ATR, ROC }
public readonly record struct IndicatorBar(double Open, double High, double Low, double Close);
public sealed record IndicatorParameters(int N1, int N2 = 0, int N3 = 0, double Factor = 2);
public sealed record IndicatorLine(string Name, double[] Values);
public sealed record IndicatorResult(IndicatorLine[] Lines, double[]? Histogram = null, double? Floor = null, double? Ceiling = null);

public static class TechnicalIndicators
{
    public static bool IsOverlay(TechnicalIndicator kind) => kind is TechnicalIndicator.MA or TechnicalIndicator.BOLL or TechnicalIndicator.EXPMA;
    public static IndicatorParameters Defaults(TechnicalIndicator kind) => kind switch
    {
        TechnicalIndicator.MA => new(5, 10, 20), TechnicalIndicator.BOLL => new(20, Factor: 2),
        TechnicalIndicator.EXPMA => new(12, 50), TechnicalIndicator.MACD => new(12, 26, 9),
        TechnicalIndicator.KDJ => new(9, 3, 3), TechnicalIndicator.RSI => new(6, 12, 24),
        TechnicalIndicator.WR => new(10, 6), TechnicalIndicator.BIAS => new(6, 12, 24),
        TechnicalIndicator.CCI => new(14), TechnicalIndicator.ATR => new(14), TechnicalIndicator.ROC => new(12, 6), _ => new(20)
    };

    public static IndicatorResult Calculate(TechnicalIndicator kind, IReadOnlyList<IndicatorBar> bars, IndicatorParameters? settings = null)
    {
        var p = settings ?? Defaults(kind);
        if (p.N1 < 1 || p.N1 > 250 || p.N2 < 0 || p.N2 > 250 || p.N3 < 0 || p.N3 > 250 || p.Factor <= 0)
            throw new ArgumentOutOfRangeException(nameof(settings));
        double[] c = bars.Select(b => b.Close).ToArray(); int count = c.Length;
        double[] Empty() => Enumerable.Repeat(double.NaN, count).ToArray();
        IndicatorLine Line(string name, double[] values) => new(name, values);
        switch (kind)
        {
            case TechnicalIndicator.MA:
                return new([Line($"MA{p.N1}", Average(c,p.N1)),Line($"MA{p.N2}",Average(c,Math.Max(1,p.N2))),Line($"MA{p.N3}",Average(c,Math.Max(1,p.N3)))]);
            case TechnicalIndicator.EXPMA:
                return new([Line($"EMA{p.N1}", Ema(c,p.N1)),Line($"EMA{p.N2}", Ema(c,Math.Max(1,p.N2)))]);
            case TechnicalIndicator.BOLL:
                var mid=Average(c,p.N1);var upper=Empty();var lower=Empty();
                for(int i=p.N1-1;i<count;i++)
                {
                    double variance=0;for(int j=i-p.N1+1;j<=i;j++)variance+=Math.Pow(c[j]-mid[i],2);
                    double deviation=p.Factor*Math.Sqrt(variance/p.N1);upper[i]=mid[i]+deviation;lower[i]=mid[i]-deviation;
                }
                return new([Line("MID",mid),Line("UPPER",upper),Line("LOWER",lower)]);
            case TechnicalIndicator.MACD:
                var fast=Ema(c,p.N1);var slow=Ema(c,Math.Max(1,p.N2));var dif=c.Select((_,i)=>fast[i]-slow[i]).ToArray();
                var dea=Ema(dif,Math.Max(1,p.N3));var histogram=dif.Select((v,i)=>2*(v-dea[i])).ToArray();
                return new([Line("DIF",dif),Line("DEA",dea)],histogram);
            case TechnicalIndicator.KDJ:
                var k=Empty();var d=Empty();var jj=Empty();double priorK=50,priorD=50;
                for(int i=0;i<count;i++)
                {
                    double high=double.MinValue,low=double.MaxValue;
                    for(int j=Math.Max(0,i-p.N1+1);j<=i;j++){high=Math.Max(high,bars[j].High);low=Math.Min(low,bars[j].Low);}
                    double rsv=high==low?50:100*(c[i]-low)/(high-low);
                    k[i]=priorK+(rsv-priorK)/Math.Max(1,p.N2);d[i]=priorD+(k[i]-priorD)/Math.Max(1,p.N3);jj[i]=3*k[i]-2*d[i];priorK=k[i];priorD=d[i];
                }
                return new([Line("K",k),Line("D",d),Line("J",jj)],Floor:0,Ceiling:100);
            case TechnicalIndicator.RSI:
                double[] Rsi(int n){var values=Empty();double gain=0,change=0;for(int i=1;i<count;i++){double delta=c[i]-c[i-1];gain+=(Math.Max(0,delta)-gain)/n;change+=(Math.Abs(delta)-change)/n;values[i]=change<1e-12?50:100*gain/change;}return values;}
                return new([Line($"RSI{p.N1}",Rsi(p.N1)),Line($"RSI{p.N2}",Rsi(Math.Max(1,p.N2))),Line($"RSI{p.N3}",Rsi(Math.Max(1,p.N3)))],Floor:0,Ceiling:100);
            case TechnicalIndicator.WR:
                double[] Wr(int n){var values=Empty();for(int i=n-1;i<count;i++){double h=double.MinValue,l=double.MaxValue;for(int j=i-n+1;j<=i;j++){h=Math.Max(h,bars[j].High);l=Math.Min(l,bars[j].Low);}values[i]=h==l?50:100*(h-c[i])/(h-l);}return values;}
                return new([Line($"WR{p.N1}",Wr(p.N1)),Line($"WR{p.N2}",Wr(Math.Max(1,p.N2)))],Floor:0,Ceiling:100);
            case TechnicalIndicator.BIAS:
                double[] Bias(int n){var ma=Average(c,n);return c.Select((v,i)=>double.IsFinite(ma[i])&&Math.Abs(ma[i])>1e-12?100*(v/ma[i]-1):double.NaN).ToArray();}
                return new([Line($"BIAS{p.N1}",Bias(p.N1)),Line($"BIAS{p.N2}",Bias(Math.Max(1,p.N2))),Line($"BIAS{p.N3}",Bias(Math.Max(1,p.N3)))]);
            case TechnicalIndicator.CCI:
                var tp=bars.Select(b=>(b.High+b.Low+b.Close)/3).ToArray();var mean=Average(tp,p.N1);var cci=Empty();
                for(int i=p.N1-1;i<count;i++){double dev=0;for(int j=i-p.N1+1;j<=i;j++)dev+=Math.Abs(tp[j]-mean[i]);cci[i]=dev<1e-12?0:(tp[i]-mean[i])/(.015*dev/p.N1);}
                return new([Line("CCI",cci)],Floor:-100,Ceiling:100);
            case TechnicalIndicator.ATR:
                var tr=bars.Select((b,i)=>i==0?b.High-b.Low:Math.Max(b.High-b.Low,Math.Max(Math.Abs(b.High-c[i-1]),Math.Abs(b.Low-c[i-1])))).ToArray();var atr=Average(tr,p.N1);
                return new([Line($"ATR{p.N1}",atr)],Floor:0);
            case TechnicalIndicator.ROC:
                var roc=Empty();for(int i=p.N1;i<count;i++)roc[i]=100*(c[i]/c[i-p.N1]-1);
                return new([Line("ROC",roc),Line($"MAROC{p.N2}",Average(roc,Math.Max(1,p.N2)))]);
            default: return new([]);
        }
    }

    public static double[] Average(double[] values,int period)
    {
        var result=Enumerable.Repeat(double.NaN,values.Length).ToArray();
        double sum=0;int valid=0;
        for(int i=0;i<values.Length;i++)
        {
            if(double.IsFinite(values[i])){sum+=values[i];valid++;}
            if(i>=period&&double.IsFinite(values[i-period])){sum-=values[i-period];valid--;}
            if(i>=period-1&&valid==period)result[i]=sum/period;
        }
        return result;
    }
    public static double[] Ema(double[] values,int period)
    {
        var result=new double[values.Length];if(values.Length==0)return result;
        result[0]=values[0];double alpha=2d/(period+1);
        for(int i=1;i<values.Length;i++)result[i]=result[i-1]+alpha*(values[i]-result[i-1]);return result;
    }
}
