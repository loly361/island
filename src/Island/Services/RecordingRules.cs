using System;
using System.Globalization;
using System.IO;

namespace Island.Services;

/// <summary>A rectangle in physical screen pixels.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(PixelRect other) => X <= other.X && Y <= other.Y && Right >= other.Right && Bottom >= other.Bottom;

    public PixelRect Intersect(PixelRect other)
    {
        var x = Math.Max(X, other.X);
        var y = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right <= x || bottom <= y ? default : new PixelRect(x, y, right - x, bottom - y);
    }
}

/// <summary>Limits and naming of screen recordings; no Windows calls, so everything here is unit-tested.</summary>
public static class RecordingRules
{
    public const int FrameRate = 30;
    /// <summary>Smallest recorded side: hardware H.264 encoders refuse tiny frames.</summary>
    public const int MinSide = 128;
    /// <summary>H.264 level 5.1 limit that every Windows encoder accepts; bigger screens are scaled down.</summary>
    public const int MaxWidth = 4096, MaxHeight = 2304;
    public const long MinFreeBytesToStart = 500L * 1024 * 1024;
    public const long MinFreeBytesToContinue = 200L * 1024 * 1024;
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan FrameInterval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / FrameRate);

    /// <summary>Videos\Island; not created here.</summary>
    public static string Folder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Island");

    public static string FileName(DateTime at) =>
        "Island-" + at.ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture) + ".mp4";

    /// <summary>The name itself, or "name-2.mp4", "name-3.mp4"… when it is taken.</summary>
    public static string UniqueFileName(string fileName, Func<string, bool> taken)
    {
        if (!taken(fileName)) return fileName;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 2; ; i++)
        {
            var candidate = $"{stem}-{i}{ext}";
            if (!taken(candidate)) return candidate;
        }
    }

    /// <summary>The recording indicator: "mm:ss", or "h:mm:ss" past an hour.</summary>
    public static string Elapsed(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        var total = (long)t.TotalSeconds;
        var hours = total / 3600;
        var minutes = total / 60 % 60;
        var seconds = total % 60;
        return hours > 0
            ? $"{hours}:{minutes:00}:{seconds:00}"
            : $"{total / 60:00}:{seconds:00}";
    }

    /// <summary>
    /// Clips the selection to the screen, grows it to at least <see cref="MinSide"/> around its centre and
    /// trims it to even width and height, which H.264 requires. Null when nothing of it is on screen.
    /// </summary>
    public static PixelRect? FitRegion(PixelRect requested, PixelRect screen)
    {
        var r = requested.Intersect(screen);
        if (r.IsEmpty) return null;

        var (x, w) = Grow(r.X, r.Width, screen.X, screen.Width);
        var (y, h) = Grow(r.Y, r.Height, screen.Y, screen.Height);
        w &= ~1;
        h &= ~1;
        return w < 2 || h < 2 ? null : new PixelRect(x, y, w, h);
    }

    private static (int Start, int Size) Grow(int start, int size, int screenStart, int screenSize)
    {
        var target = Math.Min(Math.Max(size, MinSide), screenSize);
        if (target == size) return (start, size);
        var newStart = start - (target - size) / 2;
        newStart = Math.Clamp(newStart, screenStart, screenStart + screenSize - target);
        return (newStart, target);
    }

    /// <summary>Encoded size: the source size, scaled down to fit the H.264 limits when needed; always even.</summary>
    public static (int Width, int Height) OutputSize(int width, int height)
    {
        var scale = Math.Min(1.0, Math.Min((double)MaxWidth / width, (double)MaxHeight / height));
        var w = (int)Math.Floor(width * scale) & ~1;
        var h = (int)Math.Floor(height * scale) & ~1;
        return (Math.Max(2, w), Math.Max(2, h));
    }

    /// <summary>About 0.1 bit per pixel per frame: crisp text without huge files (≈6 Mbit/s at 1080p).</summary>
    public static uint Bitrate(int width, int height)
    {
        var bits = (long)width * height * FrameRate / 10;
        return (uint)Math.Clamp(bits, 2_000_000L, 25_000_000L);
    }

    public static bool ReachedLimit(TimeSpan elapsed) => elapsed >= MaxDuration;

    public static bool HasRoomToStart(long freeBytes) => freeBytes >= MinFreeBytesToStart;

    public static bool HasRoomToContinue(long freeBytes) => freeBytes >= MinFreeBytesToContinue;
}
