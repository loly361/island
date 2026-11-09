using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.Services;

namespace Island;

/// <summary>The «Погода» tab: current conditions, next hours and a seven-day outlook.</summary>
public partial class IslandWindow
{
    private static readonly string[] DayShort = { "Вс", "Пн", "Вт", "Ср", "Чт", "Пт", "Сб" };
    private Forecast? _forecast;

    private async Task UpdateWeatherAsync()
    {
        var forecast = await _weather.FetchAsync();
        if (forecast is null) return;

        _forecast = forecast;
        WeatherIconHost.Content = WeatherIcons.Create(forecast.Kind, 17);
        TempText.Text = $"{forecast.TempC}°";
        if (_state == State.Expanded && _tab == IxWeather) RenderWeather();
    }

    private async void WeatherRefresh_Click(object sender, RoutedEventArgs e) => await UpdateWeatherAsync();

    private void RenderWeather()
    {
        HourPanel.Children.Clear();
        DayPanel.Children.Clear();

        if (_forecast is not { } f)
        {
            WxCity.Text = Loc.T("Погода");
            WxDesc.Text = Loc.T("Нет данных");
            WxFeels.Text = Loc.T("Проверьте подключение к интернету и нажмите обновить.");
            return;
        }

        WxCard.Background = CardGradient(f.Kind, f.IsDay);
        WxCity.Text = f.City ?? Loc.T("Погода");
        WxIcon.Content = WeatherIcons.Create(f.Kind, 40);
        WxTemp.Text = $"{f.TempC}°";
        WxDesc.Text = f.Description;

        var today = f.Days.FirstOrDefault(d => d.Date.Date == DateTime.Now.Date) ?? f.Days.FirstOrDefault();
        WxFeels.Text = today is null
            ? Loc.F("Ощущается как {0}°", f.FeelsC)
            : Loc.F("Ощущается как {0}° · {1}°…{2}°", f.FeelsC, today.MinC, today.MaxC);
        WxWind.Text = Loc.F("{0:0.#} м/с", f.WindMs);
        // The tile is too narrow for the direction next to the caption, so it lives in the tooltip.
        WxWindCaption.ToolTip = string.IsNullOrEmpty(f.WindDir) ? null : Loc.F("Направление: {0}", f.WindDir);
        WxHum.Text = $"{f.Humidity}%";
        WxPrecip.Text = Loc.F("{0:0.#} мм", f.PrecipMm);
        WxSun.Text = Loc.F("Восход {0:HH:mm} · закат {1:HH:mm}", f.Sunrise, f.Sunset);

        for (var i = 0; i < f.Hours.Count; i++)
        {
            var h = f.Hours[i];
            var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            column.Children.Add(Text(i == 0 ? Loc.T("Сейчас") : $"{h.Time.Hour}:00", 10, "SubBrush", center: true));
            var hourIcon = WeatherIcons.Create(i == 0 ? f.Kind : h.Kind, 20);
            hourIcon.Margin = new Thickness(0, 5, 0, 5);
            hourIcon.HorizontalAlignment = HorizontalAlignment.Center;
            column.Children.Add(hourIcon);
            column.Children.Add(Text($"{(i == 0 ? f.TempC : h.TempC)}°", 12, "TextBrush", center: true, semibold: true));
            HourPanel.Children.Add(i == 0
                ? new Border { Child = column, Background = (Brush)FindResource("Surface2"), CornerRadius = new CornerRadius(16), Padding = new Thickness(0, 6, 0, 6), Margin = new Thickness(2, -4, 2, -4) }
                : column);
        }

        if (f.Days.Count == 0) return;
        var weekMin = f.Days.Min(d => d.MinC);
        var weekMax = f.Days.Max(d => d.MaxC);
        foreach (var d in f.Days)
        {
            DayPanel.Children.Add(DayRow(d, weekMin, weekMax));
        }
    }

    private UIElement DayRow(DayPoint d, int weekMin, int weekMax)
    {
        var row = new Grid { Height = 30 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });

        var isToday = d.Date.Date == DateTime.Now.Date;
        Place(row, Text(Loc.T(isToday ? "Сегодня" : DayShort[(int)d.Date.DayOfWeek]), 12, "TextBrush", semibold: true), 0);
        var dayIcon = WeatherIcons.Create(d.Kind, 18);
        dayIcon.HorizontalAlignment = HorizontalAlignment.Left;
        dayIcon.VerticalAlignment = VerticalAlignment.Center;
        Place(row, dayIcon, 1);
        Place(row, Text($"{d.MinC}°", 11, "SubBrush", right: true), 2);

        var bar = new Grid { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Height = 4 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(d.MinC - weekMin, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1, d.MaxC - d.MinC), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(weekMax - d.MaxC, GridUnitType.Star) });
        var track = new Border { CornerRadius = new CornerRadius(2), Background = (Brush)FindResource("Surface2") };
        Grid.SetColumnSpan(track, 3);
        bar.Children.Add(track);
        var range = new Border
        {
            CornerRadius = new CornerRadius(2),
            Background = Gradient(TempColor(d.MinC), TempColor(d.MaxC), horizontal: true),
        };
        Grid.SetColumn(range, 1);
        bar.Children.Add(range);
        Place(row, bar, 3);

        Place(row, Text($"{d.MaxC}°", 12, "TextBrush", semibold: true), 4);
        if (d.PrecipMm >= 0.1)
        {
            Place(row, new TextBlock
            {
                Text = Loc.F("{0:0.#} мм", d.PrecipMm),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x79, 0xB8, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right,
            }, 5);
        }

        return row;
    }

    private static void Place(Grid grid, UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private TextBlock Text(string text, double size, string brush, bool center = false, bool right = false, bool semibold = false) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = (Brush)FindResource(brush),
        FontWeight = semibold ? FontWeights.SemiBold : FontWeights.Normal,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
        TextAlignment = right ? TextAlignment.Right : center ? TextAlignment.Center : TextAlignment.Left,
    };

    /// <summary>Sky colours for the current-conditions card; night and clear-sky tones follow the same palette family.</summary>
    private static Brush CardGradient(WxKind kind, bool isDay)
    {
        var (top, bottom) = kind switch
        {
            WxKind.Clear => (0x2E7BE8, 0x7CC4F8),
            WxKind.ClearNight or WxKind.PartlyCloudyNight => (0x0E1630, 0x2A3563),
            WxKind.PartlyCloudy => (0x4A7FC1, 0x8FB4D9),
            WxKind.Cloudy => (0x5B6B80, 0x8D9BAE),
            WxKind.Fog => (0x6B7685, 0xA3ADB9),
            WxKind.Drizzle or WxKind.Rain => (0x334A66, 0x5D7690),
            WxKind.Snow => (0x6D86A6, 0xB7C8DC),
            WxKind.Thunder => (0x2B2F4A, 0x55597C),
            _ => (0x2E7BE8, 0x7CC4F8),
        };

        var from = Rgb(top);
        var to = Rgb(bottom);
        if (!isDay && kind is not (WxKind.ClearNight or WxKind.PartlyCloudyNight))
        {
            from = Scale(from, 0.45);
            to = Scale(to, 0.5);
        }

        return Gradient(from, to);
    }

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static Color Scale(Color c, double k) => Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));

    private static readonly (int Temp, int Rgb)[] TempStops =
    {
        (-10, 0x5AC8FA), (0, 0x64D2FF), (10, 0x5FD9A6), (18, 0xA8E05F), (25, 0xFFD60A), (32, 0xFF9F0A), (40, 0xFF453A),
    };

    /// <summary>Cool-to-warm colour for a temperature, used for the week's range bars.</summary>
    private static Color TempColor(int celsius)
    {
        if (celsius <= TempStops[0].Temp) return Rgb(TempStops[0].Rgb);
        for (var i = 1; i < TempStops.Length; i++)
        {
            if (celsius > TempStops[i].Temp) continue;
            var (t0, c0) = (TempStops[i - 1].Temp, Rgb(TempStops[i - 1].Rgb));
            var (t1, c1) = (TempStops[i].Temp, Rgb(TempStops[i].Rgb));
            var k = (celsius - t0) / (double)(t1 - t0);
            return Color.FromRgb(
                (byte)(c0.R + (c1.R - c0.R) * k),
                (byte)(c0.G + (c1.G - c0.G) * k),
                (byte)(c0.B + (c1.B - c0.B) * k));
        }

        return Rgb(TempStops[^1].Rgb);
    }

    private static LinearGradientBrush Gradient(Color from, Color to, bool horizontal = false) => new(from, to, horizontal ? 0 : 90);
}
