using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Island.Services;

namespace Island;

/// <summary>Keeps the accent colour in step with the wallpaper.</summary>
public partial class IslandWindow
{
    private const int WM_SETTINGCHANGE = 0x001A;

    private readonly DispatcherTimer _accentTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };

    private void InitAccent()
    {
        _accentTimer.Tick += async (_, _) =>
        {
            _accentTimer.Stop();
            await RefreshAccentAsync(animate: true);
        };
    }

    /// <summary>A wallpaper change arrives as WM_SETTINGCHANGE; wait for the file to settle, then re-sample.</summary>
    private void ScheduleAccentRefresh()
    {
        _accentTimer.Stop();
        _accentTimer.Start();
    }

    private async Task RefreshAccentAsync(bool animate)
    {
        // A colour picked in settings wins over the wallpaper, including on wallpaper-change events.
        var color = _settings.AccentHex is { } hex
            ? AccentService.Tune((Color)ColorConverter.ConvertFromString(hex))
            : await Task.Run(AccentService.FromWallpaper);
        ApplyAccent(color, animate);
    }

    private void ApplyAccent(Color color, bool animate)
    {
        var current = ((SolidColorBrush)FindResource("AccentBrush")).Color;
        if (current == color) return;

        // DynamicResource consumers pick the new brush up; animating the new one cross-fades from the old colour.
        var brush = new SolidColorBrush(animate ? current : color);
        Resources["AccentBrush"] = brush;
        if (animate) brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, TimeSpan.FromMilliseconds(600)));
    }
}
