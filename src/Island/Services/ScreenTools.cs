using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Island.Services;

public static class ScreenTools
{
    /// <summary>Grabs every monitor into one bitmap; <paramref name="bounds"/> is its origin and size in physical pixels.</summary>
    public static Bitmap CaptureScreen(out Rectangle bounds)
    {
        bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    public static Bitmap Crop(Bitmap source, Rectangle area)
    {
        area.Intersect(new Rectangle(0, 0, source.Width, source.Height));
        return source.Clone(area, PixelFormat.Format32bppArgb);
    }

    public static BitmapSource ToBitmapSource(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public static string ScreenshotsFolder()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string SavePng(Bitmap bmp)
    {
        var path = Path.Combine(ScreenshotsFolder(), $"Island_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    /// <summary>Recognises text with the Windows OCR engine in the user's profile languages.</summary>
    public static async Task<string?> OcrAsync(Bitmap bmp)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null) return null;

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ms.AsRandomAccessStream()).AsTask();
        using var software = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask();
        var result = await engine.RecognizeAsync(software).AsTask();
        return result.Text;
    }
}
