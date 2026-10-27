using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Island.Services;

/// <summary>Picks the island's accent colour from the desktop wallpaper, falling back to the Windows accent.</summary>
public static class AccentService
{
    public static readonly Color Fallback = Color.FromRgb(0x0A, 0x84, 0xFF);

    private const int HueBins = 24;

    public static Color FromWallpaper()
    {
        try
        {
            var path = WallpaperPath();
            if (path is not null && DominantColor(path) is { } color) return Tune(color);
        }
        catch
        {
            // unreadable wallpaper: use the system colour below
        }

        return SystemAccent() is { } system ? Tune(system) : Fallback;
    }

    private static string? WallpaperPath()
    {
        var transcoded = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Themes\TranscodedWallpaper");
        if (File.Exists(transcoded)) return transcoded;

        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        var configured = key?.GetValue("WallPaper") as string;
        return !string.IsNullOrWhiteSpace(configured) && File.Exists(configured) ? configured : null;
    }

    /// <summary>The most vivid hue on the wallpaper: dull, dark and near-white pixels are ignored.</summary>
    public static Color? DominantColor(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path);
        image.DecodePixelWidth = 64;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.EndInit();
        image.Freeze();

        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        var weight = new double[HueBins];
        var sumR = new double[HueBins];
        var sumG = new double[HueBins];
        var sumB = new double[HueBins];
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            var r = pixels[i + 2];
            var g = pixels[i + 1];
            var b = pixels[i];
            ToHsv(r, g, b, out var h, out var s, out var v);
            if (v < 0.22 || s < 0.28) continue;

            var bin = Math.Min(HueBins - 1, (int)(h / 360 * HueBins));
            var w = s * v;
            weight[bin] += w;
            sumR[bin] += r * w;
            sumG[bin] += g * w;
            sumB[bin] += b * w;
        }

        var best = -1;
        var bestScore = 0.0;
        for (var i = 0; i < HueBins; i++)
        {
            var score = weight[i] + 0.5 * (weight[(i + 1) % HueBins] + weight[(i + HueBins - 1) % HueBins]);
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        if (best < 0 || weight[best] < 1.5) return null;
        return Color.FromRgb(
            (byte)(sumR[best] / weight[best]),
            (byte)(sumG[best] / weight[best]),
            (byte)(sumB[best] / weight[best]));
    }

    private static Color? SystemAccent()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
        if (key?.GetValue("AccentColor") is not int abgr) return null;
        return Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
    }

    /// <summary>Makes any colour usable as an accent: vivid enough to see, never so light that white text vanishes on it.</summary>
    public static Color Tune(Color c)
    {
        ToHsv(c.R, c.G, c.B, out var h, out var s, out var v);
        s = Math.Clamp(s, 0.55, 0.9);
        v = Math.Clamp(v, 0.8, 1.0);

        var tuned = FromHsv(h, s, v);
        while (v > 0.45 && Luminance(tuned) > 0.5)
        {
            v -= 0.04;
            tuned = FromHsv(h, s, v);
        }

        return tuned;
    }

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    private static void ToHsv(byte r8, byte g8, byte b8, out double h, out double s, out double v)
    {
        double r = r8 / 255.0, g = g8 / 255.0, b = b8 / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        v = max;
        s = max <= 0 ? 0 : d / max;
        if (d <= 0)
        {
            h = 0;
            return;
        }

        h = max == r ? 60 * (((g - b) / d) % 6)
            : max == g ? 60 * ((b - r) / d + 2)
            : 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
    }

    private static Color FromHsv(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
