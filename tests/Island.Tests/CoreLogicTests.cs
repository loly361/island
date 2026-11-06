using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Island.Services;

namespace Island.Tests;

public class ClipboardHistoryTests
{
    [Fact]
    public void Add_IgnoresBlankText()
    {
        var h = new ClipboardHistory();
        Assert.False(h.Add("   ", "x"));
        Assert.False(h.Add(null, "x"));
        Assert.Empty(h.Items);
    }

    [Fact]
    public void Add_MovesDuplicateToTopAndKeepsPin()
    {
        var h = new ClipboardHistory();
        h.Add("a", "src");
        h.Add("b", "src");
        h.TogglePin(h.Items.First(e => e.Text == "a"));

        h.Add("a", "other");

        Assert.Equal(2, h.Items.Count);
        Assert.Equal("a", h.Items[0].Text);
        Assert.Equal("other", h.Items[0].Source);
        Assert.True(h.Items[0].Pinned);
    }

    [Fact]
    public void Add_CapsUnpinnedButKeepsPinned()
    {
        var h = new ClipboardHistory();
        h.Add("pinned", "s");
        h.TogglePin(h.Items[0]);
        for (var i = 0; i < ClipboardHistory.MaxUnpinned + 20; i++) h.Add($"item {i}", "s");

        Assert.Equal(ClipboardHistory.MaxUnpinned, h.Items.Count(e => !e.Pinned));
        Assert.Contains(h.Items, e => e.Text == "pinned");
    }

    [Fact]
    public void Add_TruncatesHugeText()
    {
        var h = new ClipboardHistory();
        h.Add(new string('x', ClipboardHistory.MaxTextLength + 500), "s");
        Assert.Equal(ClipboardHistory.MaxTextLength, h.Items[0].Text.Length);
    }

    [Fact]
    public void Filter_IsCaseInsensitiveAndPinnedFirst()
    {
        var h = new ClipboardHistory();
        h.Add("Ссылка на макет", "s", new DateTime(2026, 1, 1));
        h.Add("другое", "s", new DateTime(2026, 1, 2));
        h.Add("ссылка на трансляцию", "s", new DateTime(2026, 1, 3));
        h.TogglePin(h.Items.First(e => e.Text == "Ссылка на макет"));

        var result = h.Filter("ССЫЛК");

        Assert.Equal(new[] { "Ссылка на макет", "ссылка на трансляцию" }, result.Select(e => e.Text));
    }

    [Fact]
    public void ClearUnpinned_KeepsPinnedOnly()
    {
        var h = new ClipboardHistory();
        h.Add("keep", "s");
        h.TogglePin(h.Items[0]);
        h.Add("drop", "s");

        h.ClearUnpinned();

        Assert.Single(h.Items);
        Assert.Equal("keep", h.Items[0].Text);
    }

    [Fact]
    public void Persists_AndReloadsFromDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"island-test-{Guid.NewGuid():N}.json");
        try
        {
            var first = new ClipboardHistory(path);
            first.Add("hello", "src");

            var second = new ClipboardHistory(path);

            Assert.Equal("hello", Assert.Single(second.Items).Text);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class TimeLabelTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0);

    [Theory]
    [InlineData(10, "сейчас")]
    [InlineData(600, "10 мин")]
    [InlineData(7200, "2 ч")]
    [InlineData(100000, "вчера")]
    [InlineData(400000, "4 дн")]
    public void Ago_FormatsRelativeTime(int secondsAgo, string expected) =>
        Assert.Equal(expected, TimeLabels.Ago(Now.AddSeconds(-secondsAgo), Now));

    [Fact]
    public void Ago_TreatsFutureAsNow() =>
        Assert.Equal("сейчас", TimeLabels.Ago(Now.AddMinutes(5), Now));

    [Theory]
    [InlineData(65, "1:05")]
    [InlineData(3725, "1:02:05")]
    public void Clock_FormatsTrackTime(int seconds, string expected) =>
        Assert.Equal(expected, TimeLabels.Clock(TimeSpan.FromSeconds(seconds)));
}

public class WeatherCodeTests
{
    [Theory]
    [InlineData(0, true, WxKind.Clear)]
    [InlineData(0, false, WxKind.ClearNight)]
    [InlineData(2, true, WxKind.PartlyCloudy)]
    [InlineData(2, false, WxKind.PartlyCloudyNight)]
    [InlineData(3, true, WxKind.Cloudy)]
    [InlineData(45, true, WxKind.Fog)]
    [InlineData(53, true, WxKind.Drizzle)]
    [InlineData(63, true, WxKind.Rain)]
    [InlineData(81, true, WxKind.Rain)]
    [InlineData(73, true, WxKind.Snow)]
    [InlineData(95, true, WxKind.Thunder)]
    public void ToKind_MapsCodes(int code, bool day, WxKind expected) =>
        Assert.Equal(expected, WeatherCodes.ToKind(code, day));
}
public class SearchRankerTests
{
    private static readonly AppEntry[] Apps =
    {
        new("Microsoft Edge", "edge.lnk"),
        new("Edge Canary Tools", "canary.lnk"),
        new("Knowledge", "know.lnk"),
        new("Блокнот", "notepad.lnk"),
        new("Paint", "paint.lnk"),
    };

    [Fact]
    public void RankApps_PrefersPrefixOverWordStartOverContains()
    {
        var names = SearchRanker.RankApps(Apps, "edge", 10).Select(a => a.Name).ToList();

        Assert.Equal("Edge Canary Tools", names[0]);
        Assert.Equal("Microsoft Edge", names[1]);
        Assert.Equal("Knowledge", names[2]);
    }

    [Fact]
    public void RankApps_MatchesCyrillicIgnoringCase() =>
        Assert.Equal("Блокнот", Assert.Single(SearchRanker.RankApps(Apps, "БЛОК", 5)).Name);

    [Fact]
    public void RankApps_EmptyQueryReturnsNothing() =>
        Assert.Empty(SearchRanker.RankApps(Apps, "  ", 5));

    [Fact]
    public void RankApps_RespectsMax() =>
        Assert.Single(SearchRanker.RankApps(Apps, "e", 1));
}

public class NotesStoreTests
{
    [Fact]
    public void CreateSaveDelete_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"island-notes-{Guid.NewGuid():N}.json");
        try
        {
            var store = new NotesStore(path);
            var note = store.Create();
            store.Save(note, "Заголовок", "{\\rtf1 hi}");

            var reloaded = new NotesStore(path);
            var loaded = Assert.Single(reloaded.Items);
            Assert.Equal("Заголовок", loaded.Title);
            Assert.Equal("{\\rtf1 hi}", loaded.Rtf);

            reloaded.Delete(loaded);
            Assert.Empty(new NotesStore(path).Items);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class ReminderParserTests
{
    // Monday, 12:00
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0);

    [Fact]
    public void Tomorrowathour()
    {
        var r = ReminderParser.Parse("позвонить маме завтра в 9", Now)!;
        Assert.Equal("Позвонить маме", r.Text);
        Assert.Equal(new DateTime(2026, 10, 27, 9, 0, 0), r.Due);
        Assert.False(r.Daily);
    }

    [Fact]
    public void DailyWithClock()
    {
        var r = ReminderParser.Parse("таблетки каждый день в 23:00", Now)!;
        Assert.Equal("Таблетки", r.Text);
        Assert.Equal(new DateTime(2026, 10, 26, 23, 0, 0), r.Due);
        Assert.True(r.Daily);
    }

    [Theory]
    [InlineData("через 20 минут выключить плиту", 12, 20, "Выключить плиту")]
    [InlineData("через час позвонить", 13, 0, "Позвонить")]
    [InlineData("через полчаса чай", 12, 30, "Чай")]
    [InlineData("через 2 часа встреча", 14, 0, "Встреча")]
    public void Relative(string input, int hour, int minute, string text)
    {
        var r = ReminderParser.Parse(input, Now)!;
        Assert.Equal(text, r.Text);
        Assert.Equal(new DateTime(2026, 10, 26, hour, minute, 0), r.Due);
    }

    [Fact]
    public void WeekdayWithTime()
    {
        var r = ReminderParser.Parse("в пятницу в 18:30 созвон", Now)!;
        Assert.Equal("Созвон", r.Text);
        Assert.Equal(new DateTime(2026, 10, 30, 18, 30, 0), r.Due);
    }

    [Fact]
    public void EveningMeansPm()
    {
        var r = ReminderParser.Parse("в 9 вечера выпить чай", Now)!;
        Assert.Equal(new DateTime(2026, 10, 26, 21, 0, 0), r.Due);
        Assert.Equal("Выпить чай", r.Text);
    }

    [Fact]
    public void PassedClockRollsToTomorrow()
    {
        var r = ReminderParser.Parse("в 8 утра зарядка", Now)!;
        Assert.Equal(new DateTime(2026, 10, 27, 8, 0, 0), r.Due);
    }

    [Fact]
    public void LeadingRemindWordIsDropped()
    {
        var r = ReminderParser.Parse("напомни мне завтра в 10 купить хлеб", Now)!;
        Assert.Equal("Купить хлеб", r.Text);
        Assert.Equal(new DateTime(2026, 10, 27, 10, 0, 0), r.Due);
    }

    [Fact]
    public void DayAfterTomorrow()
    {
        var r = ReminderParser.Parse("послезавтра в 14:15 врач", Now)!;
        Assert.Equal(new DateTime(2026, 10, 28, 14, 15, 0), r.Due);
    }

    [Theory]
    [InlineData("просто текст")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("сегодня в 8 утра")]
    [InlineData("встреча в 25 часов")]
    public void Unparseable_ReturnsNull(string input) => Assert.Null(ReminderParser.Parse(input, Now));
}

public class ReminderStoreTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0);

    [Fact]
    public void TakeDue_RemovesOneShotAndReschedulesDaily()
    {
        var store = new ReminderStore();
        store.Add(new ParsedReminder("once", Now.AddMinutes(-1), false));
        store.Add(new ParsedReminder("daily", Now.AddDays(-2), true));
        store.Add(new ParsedReminder("later", Now.AddHours(3), false));

        var due = store.TakeDue(Now);

        Assert.Equal(new[] { "once", "daily" }, due.Select(r => r.Text).OrderByDescending(t => t));
        Assert.DoesNotContain(store.Items, r => r.Text == "once");
        Assert.True(store.Items.Single(r => r.Text == "daily").Due > Now);
        Assert.Equal(2, store.Items.Count);
    }

    [Theory]
    [InlineData(0, false, "сегодня 21:00")]
    [InlineData(1, false, "завтра 21:00")]
    [InlineData(4, false, "пт 21:00")]
    [InlineData(10, false, "05.11 21:00")]
    [InlineData(0, true, "каждый день 21:00")]
    public void DueLabel(int daysAhead, bool daily, string expected) =>
        Assert.Equal(expected, ReminderFormat.Due(Now.Date.AddDays(daysAhead).AddHours(21), daily, Now));
}

public class CountdownTimerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RunsDownAndFinishesOnce()
    {
        var t = new CountdownTimer();
        t.Set(TimeSpan.FromMinutes(5));
        t.Start(T0);

        Assert.False(t.Tick(T0.AddMinutes(2)));
        Assert.Equal(TimeSpan.FromMinutes(3), t.Remaining);
        Assert.True(t.Tick(T0.AddMinutes(5)));
        Assert.False(t.Tick(T0.AddMinutes(6)));
        Assert.False(t.Running);
        Assert.Equal(TimeSpan.FromMinutes(5), t.Remaining);
    }

    [Fact]
    public void PauseAndResumeKeepRemaining()
    {
        var t = new CountdownTimer();
        t.Set(TimeSpan.FromMinutes(10));
        t.Start(T0);
        t.Pause(T0.AddMinutes(4));

        Assert.True(t.Paused);
        Assert.Equal(TimeSpan.FromMinutes(6), t.Remaining);

        t.Start(T0.AddMinutes(30));
        Assert.True(t.Tick(T0.AddMinutes(36)));
    }

    [Fact]
    public void FractionAndReset()
    {
        var t = new CountdownTimer();
        t.Set(TimeSpan.FromMinutes(4));
        t.Start(T0);
        t.Tick(T0.AddMinutes(1));

        Assert.Equal(0.75, t.Fraction, 3);
        t.Reset();
        Assert.Equal(1.0, t.Fraction);
        Assert.False(t.Running);
    }
}

public class ForecastTests
{
    private const string Sample = """
    {"current":{"time":"2026-10-26T21:15","temperature_2m":10.4,"apparent_temperature":8.6,"relative_humidity_2m":73,"precipitation":0.0,"weather_code":3,"is_day":0,"wind_speed_10m":3.2,"wind_direction_10m":140},
     "hourly":{"time":["2026-10-26T20:00","2026-10-26T21:00","2026-10-26T22:00"],"temperature_2m":[11.0,10.4,9.6],"weather_code":[3,3,61]},
     "daily":{"time":["2026-10-26","2026-10-27"],"weather_code":[3,61],"temperature_2m_max":[12.2,9.5],"temperature_2m_min":[6.0,4.4],"precipitation_sum":[0.0,1.26],"sunrise":["2026-10-26T07:17","2026-10-27T07:19"],"sunset":["2026-10-26T17:09","2026-10-27T17:07"]}}
    """;

    [Fact]
    public void Parse_MapsCurrentHourlyAndDaily()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(Sample);
        var f = WeatherService.Parse(doc.RootElement, "Москва");

        Assert.Equal("Москва", f.City);
        Assert.Equal(10, f.TempC);
        Assert.Equal(9, f.FeelsC);
        Assert.Equal("Пасмурно", f.Description);
        Assert.Equal("ЮВ", f.WindDir);
        Assert.Equal(73, f.Humidity);
        Assert.False(f.IsDay);
        Assert.Equal(new TimeSpan(7, 17, 0), f.Sunrise.TimeOfDay);

        Assert.Equal(2, f.Hours.Count);
        Assert.Equal(21, f.Hours[0].Time.Hour);
        Assert.Equal(WxKind.Rain, f.Hours[1].Kind);

        Assert.Equal(2, f.Days.Count);
        Assert.Equal((6, 12), (f.Days[0].MinC, f.Days[0].MaxC));
        Assert.Equal(10, f.Days[1].MaxC);
        Assert.Equal(1.26, f.Days[1].PrecipMm, 2);
    }

    [Theory]
    [InlineData(0, "С")]
    [InlineData(90, "В")]
    [InlineData(140, "ЮВ")]
    [InlineData(225, "ЮЗ")]
    [InlineData(350, "С")]
    [InlineData(-45, "СЗ")]
    public void WindDirection(double degrees, string expected) =>
        Assert.Equal(expected, WeatherCodes.WindDirection(degrees));

    [Theory]
    [InlineData(0, "Ясно")]
    [InlineData(63, "Дождь")]
    [InlineData(66, "Ледяной дождь")]
    [InlineData(75, "Снег")]
    [InlineData(95, "Гроза")]
    public void Describe(int code, string expected) => Assert.Equal(expected, WeatherCodes.Describe(code));
}

public class AccentServiceTests
{
    private static double Luminance(System.Windows.Media.Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    [Theory]
    [InlineData(0xFF, 0xFF, 0x00)]
    [InlineData(0xFF, 0xFF, 0xFF)]
    [InlineData(0x30, 0x30, 0x30)]
    [InlineData(0xA0, 0xE0, 0x40)]
    [InlineData(0x10, 0x20, 0xC0)]
    public void Tune_KeepsAccentVisibleUnderWhiteText(byte r, byte g, byte b)
    {
        var tuned = AccentService.Tune(System.Windows.Media.Color.FromRgb(r, g, b));

        Assert.True(Luminance(tuned) <= 0.5, $"too light: {tuned}");
        Assert.True(Math.Max(tuned.R, Math.Max(tuned.G, tuned.B)) >= 0.45 * 255, $"too dark: {tuned}");
    }

    [Fact]
    public void Tune_KeepsTheHue()
    {
        var tuned = AccentService.Tune(System.Windows.Media.Color.FromRgb(0xD0, 0x40, 0x40));

        Assert.True(tuned.R > tuned.G && tuned.R > tuned.B);
    }
}

public class NotifyAuthTests
{
    [Fact]
    public void AcceptsOnlyTheExactToken()
    {
        Assert.True(NotifyServer.IsAuthorized("abc123", "abc123"));
        Assert.False(NotifyServer.IsAuthorized("abc123", "abc124"));
        Assert.False(NotifyServer.IsAuthorized("abc123", "abc1234"));
        Assert.False(NotifyServer.IsAuthorized("abc123", null));
    }

    [Fact]
    public void EmptyExpectedTokenNeverMatches()
    {
        Assert.False(NotifyServer.IsAuthorized("", ""));
        Assert.False(NotifyServer.IsAuthorized("", null));
    }

    [Fact]
    public async Task ReadBody_RejectsBodiesOverTheLimitEvenWithoutDeclaredLength()
    {
        using var small = new MemoryStream(new byte[NotifyServer.MaxBodyBytes]);
        using var big = new MemoryStream(new byte[NotifyServer.MaxBodyBytes + 1]);

        Assert.NotNull(await NotifyServer.ReadBodyAsync(small));
        Assert.Null(await NotifyServer.ReadBodyAsync(big));
    }
}

public class SettingsCopyTests
{
    [Fact]
    public void CopyFrom_ReplacesEveryValue()
    {
        var target = new Settings { City = "Old", NotifyToken = "a" };
        var source = new Settings { City = "New", NotifyToken = "b", AccentHex = "#0A84FF", SearchUrl = "https://www.google.com/search?q={0}", Mode = Settings.ModeMinimal };

        target.CopyFrom(source);

        Assert.Equal(Settings.ModeMinimal, target.Mode);
        Assert.Equal("New", target.City);
        Assert.Equal("b", target.NotifyToken);
        Assert.Equal("#0A84FF", target.AccentHex);
        Assert.Equal("https://www.google.com/search?q={0}", target.SearchUrl);
    }

    [Fact]
    public void TryLoad_ReturnsNullForBrokenFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"island-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ broken");

            Assert.Null(JsonStore.TryLoad<Settings>(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class ClipboardRetentionTests
{
    [Fact]
    public void Startup_DropsOldUnpinnedCopiesButKeepsPinned()
    {
        var path = Path.Combine(Path.GetTempPath(), $"island-clip-{Guid.NewGuid():N}.dat");
        try
        {
            var first = new ClipboardHistory(path);
            first.Add("старое", "s", DateTime.Now.AddDays(-3));
            first.Add("закреплённое старое", "s", DateTime.Now.AddDays(-3));
            first.TogglePin(first.Items.First(e => e.Text == "закреплённое старое"));
            first.Add("свежее", "s", DateTime.Now);

            var second = new ClipboardHistory(path);

            Assert.Contains(second.Items, e => e.Text == "свежее");
            Assert.Contains(second.Items, e => e.Text == "закреплённое старое");
            Assert.DoesNotContain(second.Items, e => e.Text == "старое");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
