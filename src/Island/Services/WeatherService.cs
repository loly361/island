using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Island.Services;

public enum WxKind { Clear, ClearNight, PartlyCloudy, PartlyCloudyNight, Cloudy, Fog, Drizzle, Rain, Snow, Thunder }

public sealed record HourPoint(DateTime Time, int TempC, WxKind Kind);

public sealed record DayPoint(DateTime Date, int MinC, int MaxC, WxKind Kind, double PrecipMm);

public sealed record Forecast(
    string? City,
    int TempC,
    int FeelsC,
    WxKind Kind,
    string Description,
    double WindMs,
    string WindDir,
    int Humidity,
    double PrecipMm,
    bool IsDay,
    DateTime Sunrise,
    DateTime Sunset,
    IReadOnlyList<HourPoint> Hours,
    IReadOnlyList<DayPoint> Days);

public static class WeatherCodes
{
    /// <summary>Maps a WMO weather code (as used by Open-Meteo) to an icon kind.</summary>
    public static WxKind ToKind(int code, bool isDay) => code switch
    {
        0 => isDay ? WxKind.Clear : WxKind.ClearNight,
        1 or 2 => isDay ? WxKind.PartlyCloudy : WxKind.PartlyCloudyNight,
        45 or 48 => WxKind.Fog,
        >= 51 and <= 57 => WxKind.Drizzle,
        >= 61 and <= 67 => WxKind.Rain,
        >= 71 and <= 77 => WxKind.Snow,
        >= 80 and <= 82 => WxKind.Rain,
        85 or 86 => WxKind.Snow,
        >= 95 => WxKind.Thunder,
        _ => WxKind.Cloudy,
    };
    public static string Describe(int code) => code switch
    {
        0 => "Ясно",
        1 => "Преимущественно ясно",
        2 => "Переменная облачность",
        3 => "Пасмурно",
        45 or 48 => "Туман",
        >= 51 and <= 57 => "Морось",
        66 or 67 => "Ледяной дождь",
        >= 61 and <= 65 => "Дождь",
        >= 71 and <= 77 => "Снег",
        >= 80 and <= 82 => "Ливень",
        85 or 86 => "Снегопад",
        >= 95 => "Гроза",
        _ => "—",
    };

    private static readonly string[] Compass = { "С", "СВ", "В", "ЮВ", "Ю", "ЮЗ", "З", "СЗ" };

    /// <summary>Wind direction (the way it blows from) as a Russian compass abbreviation.</summary>
    public static string WindDirection(double degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        return Compass[(int)Math.Round(normalized / 45.0) % 8];
    }
}

public sealed class WeatherService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly Settings _settings;

    public WeatherService(Settings settings) => _settings = settings;

    public async Task<Forecast?> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            if (_settings.Latitude is null || _settings.Longitude is null)
            {
                await ResolveLocationAsync(ct);
                if (_settings.Latitude is null || _settings.Longitude is null) return null;
            }

            var url = FormattableString.Invariant(
                $"https://api.open-meteo.com/v1/forecast?latitude={_settings.Latitude}&longitude={_settings.Longitude}") +
                "&current=temperature_2m,apparent_temperature,relative_humidity_2m,precipitation,weather_code,is_day,wind_speed_10m,wind_direction_10m" +
                "&hourly=temperature_2m,weather_code" +
                "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum,sunrise,sunset" +
                "&wind_speed_unit=ms&timezone=auto&forecast_days=7";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
            return Parse(doc.RootElement, _settings.City);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Turns an Open-Meteo response into a <see cref="Forecast"/>.</summary>
    public static Forecast Parse(JsonElement root, string? city)
    {
        var cur = root.GetProperty("current");
        var now = ParseTime(cur.GetProperty("time").GetString()!);
        var code = cur.GetProperty("weather_code").GetInt32();
        var isDay = cur.GetProperty("is_day").GetInt32() == 1;

        var daily = root.GetProperty("daily");
        var dates = Strings(daily, "time").Select(ParseTime).ToList();
        var codes = Ints(daily, "weather_code");
        var maxes = Doubles(daily, "temperature_2m_max");
        var mins = Doubles(daily, "temperature_2m_min");
        var precip = Doubles(daily, "precipitation_sum");
        var rises = Strings(daily, "sunrise").Select(ParseTime).ToList();
        var sets = Strings(daily, "sunset").Select(ParseTime).ToList();

        var days = new List<DayPoint>();
        for (var i = 0; i < dates.Count; i++)
        {
            days.Add(new DayPoint(dates[i], Round(mins[i]), Round(maxes[i]), WeatherCodes.ToKind(codes[i], true), precip[i]));
        }

        var hourly = root.GetProperty("hourly");
        var hourTimes = Strings(hourly, "time").Select(ParseTime).ToList();
        var hourTemps = Doubles(hourly, "temperature_2m");
        var hourCodes = Ints(hourly, "weather_code");
        var hours = new List<HourPoint>();
        var startOfHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
        for (var i = 0; i < hourTimes.Count && hours.Count < 8; i++)
        {
            if (hourTimes[i] < startOfHour) continue;
            var di = dates.FindIndex(d => d.Date == hourTimes[i].Date);
            var day = di >= 0 && hourTimes[i] >= rises[di] && hourTimes[i] < sets[di];
            hours.Add(new HourPoint(hourTimes[i], Round(hourTemps[i]), WeatherCodes.ToKind(hourCodes[i], day)));
        }

        var todayIndex = Math.Max(0, dates.FindIndex(d => d.Date == now.Date));
        return new Forecast(
            city,
            Round(cur.GetProperty("temperature_2m").GetDouble()),
            Round(cur.GetProperty("apparent_temperature").GetDouble()),
            WeatherCodes.ToKind(code, isDay),
            WeatherCodes.Describe(code),
            cur.GetProperty("wind_speed_10m").GetDouble(),
            WeatherCodes.WindDirection(cur.GetProperty("wind_direction_10m").GetDouble()),
            (int)Math.Round(cur.GetProperty("relative_humidity_2m").GetDouble()),
            cur.GetProperty("precipitation").GetDouble(),
            isDay,
            rises[todayIndex],
            sets[todayIndex],
            hours,
            days);
    }

    private static DateTime ParseTime(string s) => DateTime.Parse(s, CultureInfo.InvariantCulture);

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static IEnumerable<string> Strings(JsonElement parent, string name) =>
        parent.GetProperty(name).EnumerateArray().Select(e => e.GetString() ?? "");

    private static List<int> Ints(JsonElement parent, string name) =>
        parent.GetProperty(name).EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Null ? 0 : e.GetInt32()).ToList();

    private static List<double> Doubles(JsonElement parent, string name) =>
        parent.GetProperty(name).EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Null ? 0 : e.GetDouble()).ToList();

    private async Task ResolveLocationAsync(CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://ipwho.is/", ct));
        var root = doc.RootElement;
        if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False) return;
        _settings.Latitude = root.GetProperty("latitude").GetDouble();
        _settings.Longitude = root.GetProperty("longitude").GetDouble();
        _settings.City = root.TryGetProperty("city", out var c) ? c.GetString() : null;
        JsonStore.Save(AppPaths.File("settings.json"), _settings);
    }
}
