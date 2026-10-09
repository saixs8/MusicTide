using System.Security.Cryptography;
using System.Text;

namespace SpectrumKlinePlayer;

public sealed record SongHeat(long Likes, long Comments, bool Simulated = true)
{
    public static SongHeat Estimate(string title)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(title.Trim().ToUpperInvariant()));
        return new(1000 + BitConverter.ToUInt32(hash, 0) % 2000000,
            100 + BitConverter.ToUInt32(hash, 4) % 60000);
    }
    public double Shares => Math.Clamp(50000000d + Likes * 800d + Comments * 3000d, 50000000, 8000000000);
    public double FloatShares => Shares * .6;
    public double OpeningPE => 12 + Math.Log10(1 + Likes + Comments * 10d) * 5;
}

public readonly record struct MarketAxis(double Minimum, double Maximum, double Reference)
{
    public static MarketAxis Fit(IEnumerable<double> values, double reference)
    {
        var valid = values.Where(v => double.IsFinite(v) && v > 0).ToArray();
        double low = valid.Length > 0 ? valid.Min() : reference;
        double high = valid.Length > 0 ? valid.Max() : reference;
        double padding = Math.Max((high - low) * .08, reference * .002);
        return new(Math.Max(.001, low - padding), high + padding, reference);
    }
    public static MarketAxis Centered(IEnumerable<double> values, double reference)
    {
        double radius = values.Where(v => double.IsFinite(v) && v > 0).Select(v => Math.Abs(v - reference)).DefaultIfEmpty(0).Max();
        radius = Math.Max(radius * 1.08, reference * .002);
        return new(reference - radius, reference + radius, reference);
    }
    public double Percent(double price) => (price / Reference - 1) * 100;
    public float Y(Rectangle plot, double price) => plot.Bottom - (float)((price - Minimum) / (Maximum - Minimum)) * plot.Height;
    public double PriceAt(Rectangle plot, float y) => Maximum - (y - plot.Top) / plot.Height * (Maximum - Minimum);
}
