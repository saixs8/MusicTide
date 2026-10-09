using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace SpectrumKlinePlayer;

// Reads only visible KuGou lyric-sized windows. Pixels stay in memory; nothing is saved.
public static class KugouScreenLyricCapture
{
    private static DateTime nextCaptureUtc;
    private static readonly OcrEngine? engine = OcrEngine.TryCreateFromLanguage(
        new Windows.Globalization.Language("zh-Hans-CN"));

    public static LiveLyricPosition? TryCapture(string trackTitle, double? expectedSeconds = null)
    {
        if (engine is null || DateTime.UtcNow < nextCaptureUtc) return null;
        nextCaptureUtc = DateTime.UtcNow.AddMilliseconds(900);
        LiveLyricPosition? match = null;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            GetWindowThreadProcessId(window, out uint processId);
            if (!IsKugouProcess(processId) || !GetWindowRect(window, out Rect rect)) return true;
            var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (bounds.Width < 150 || bounds.Height < 25 || bounds.Height > 1600 || bounds.Width > 2400)
                return true;
            bool mainWindow = bounds.Height > 330;
            if (mainWindow)
                bounds = new Rectangle(bounds.Left, bounds.Bottom - Math.Min(150, bounds.Height / 3), bounds.Width, Math.Min(150, bounds.Height / 3));
            bounds = Rectangle.Intersect(bounds, SystemInformation.VirtualScreen);
            if (bounds.Width < 150 || bounds.Height < 25) return true;

            try
            {
                foreach (string text in ReadText(bounds))
                {
                    if (mainWindow)
                    {
                        if (TryParseProgress(text) is { } progress)
                        {
                            match = new LiveLyricPosition(text, progress.Position, progress.Duration, true);
                            return false;
                        }
                        continue;
                    }
                    if (KugouLocalLyricCapture.TryGetLineStartSeconds(trackTitle, text, expectedSeconds) is { } seconds)
                    {
                        if (match is null || (expectedSeconds is { } expected
                            && Math.Abs(seconds - expected) < Math.Abs(match.Value.PositionSeconds - expected)))
                            match = new LiveLyricPosition(text, seconds);
                    }
                }
            }
            catch
            {
                // A lyric window can disappear while its pixels are being captured.
            }
            return true;
        }, IntPtr.Zero);
        return match;
    }

    public static (double Position, double Duration)? TryParseProgress(string text)
    {
        var match = Regex.Match(text, @"(?<!\d)(\d{1,3})\s*[:：]\s*(\d{2})\s*[/／|]\s*(\d{1,3})\s*[:：]\s*(\d{2})(?!\d)");
        if (!match.Success) return null;
        int seconds = int.Parse(match.Groups[2].Value), totalSeconds = int.Parse(match.Groups[4].Value);
        double position = int.Parse(match.Groups[1].Value) * 60 + seconds;
        double duration = int.Parse(match.Groups[3].Value) * 60 + totalSeconds;
        return seconds < 60 && totalSeconds < 60 && duration >= 2 && position <= duration
            ? (position, duration) : null;
    }

    private static IEnumerable<string> ReadText(Rectangle bounds)
    {
        using var image = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(image))
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);

        return ReadBitmapText(image);
    }

    public static string[] ReadBitmapText(Bitmap image)
    {
        if (engine is null) return [];

        var bitmapData = image.LockBits(new Rectangle(0, 0, image.Width, image.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] pixels;
        try
        {
            pixels = new byte[image.Width * image.Height * 4];
            Marshal.Copy(bitmapData.Scan0, pixels, 0, pixels.Length);
        }
        finally { image.UnlockBits(bitmapData); }

        using var writer = new DataWriter();
        writer.WriteBytes(pixels);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(),
            BitmapPixelFormat.Bgra8, image.Width, image.Height, BitmapAlphaMode.Premultiplied);
        var result = engine!.RecognizeAsync(bitmap).AsTask().GetAwaiter().GetResult();
        return result.Lines.Select(line => line.Text).Where(text => !string.IsNullOrWhiteSpace(text)).ToArray();
    }

    private static bool IsKugouProcess(uint id)
    {
        if (id == 0) return false;
        try
        {
            string name = Process.GetProcessById((int)id).ProcessName;
            return name.Contains("kugou", StringComparison.OrdinalIgnoreCase)
                || name.Contains("kgmusic", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
}

public readonly record struct LiveLyricPosition(string Text, double PositionSeconds, double DurationSeconds = -1, bool HasExactPosition = false);
