using System.Windows;
using System.Windows.Media;
using Island.Services;

namespace Island;

/// <summary>
/// Weather icons: monochrome outlines from <see cref="IconSet"/> (sun.max, moon, cloud.sun, cloud.rain…), drawn in white.
/// </summary>
public static class WeatherIcons
{
    public static string KindName(WxKind kind) => kind switch
    {
        WxKind.Clear => "sun.max",
        WxKind.ClearNight => "moon",
        WxKind.PartlyCloudy => "cloud.sun",
        WxKind.PartlyCloudyNight => "cloud.moon",
        WxKind.Cloudy => "cloud",
        WxKind.Fog => "cloud.fog",
        WxKind.Drizzle => "cloud.drizzle",
        WxKind.Rain => "cloud.rain",
        WxKind.Snow => "cloud.snow",
        WxKind.Thunder => "cloud.bolt",
        _ => "cloud",
    };

    public static FrameworkElement Create(WxKind kind, double size) =>
        new Icon { Kind = KindName(kind), Size = size, Foreground = Brushes.White };
}
