using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Island.Services;

public sealed record ParsedReminder(string Text, DateTime Due, bool Daily);

/// <summary>Understands short Russian phrases such as «позвонить маме завтра в 9» or «через 20 минут выключить плиту».</summary>
public static class ReminderParser
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex DailyRx = new(@"\b(каждый\s+день|ежедневно)\b", Opts);

    private static readonly Regex RelativeRx = new(
        @"\bчерез\s+(?:(\d+)\s*(сек\w*|мин\w*|час\w*|ч|дн\w*|день|дня|дней)|(полчаса)|(час|минуту|секунду|день))(?!\w)", Opts);

    private static readonly Regex DayWordRx = new(@"\b(сегодня|завтра|послезавтра)\b", Opts);

    private static readonly Regex WeekdayRx = new(
        @"\b(?:в|во)\s+(понедельник|вторник|среду|четверг|пятницу|субботу|воскресенье)\b", Opts);

    private static readonly Regex TimeRx = new(
        @"\b(?:в|к)\s+(\d{1,2})(?:[:.](\d{2}))?(?:\s*час(?:ов|а)?)?(?:\s+(утра|дня|вечера|ночи))?(?!\d)", Opts);

    private static readonly Regex PartOfDayRx = new(@"\b(утром|днём|днем|вечером|ночью)\b", Opts);

    private static readonly Regex LeadRx = new(
        @"^\s*(?:напомни(?:ть)?|напоминай)(?:\s+мне)?\s*(?:,|что|о\s+том,?\s+что)?\s*", Opts);

    private static readonly string[] Weekdays =
        { "воскресенье", "понедельник", "вторник", "среду", "четверг", "пятницу", "субботу" };

    public static ParsedReminder? Parse(string? input, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var rest = input.Trim();

        var daily = Take(DailyRx, ref rest) is not null;

        DateTime? relative = null;
        if (Take(RelativeRx, ref rest) is { } rel)
            relative = now + RelativeSpan(rel);

        int? dayOffset = null;
        string? dayWord = null;
        if (relative is null)
        {
            if (Take(WeekdayRx, ref rest) is { } wd)
            {
                var target = Array.IndexOf(Weekdays, wd.Groups[1].Value.ToLowerInvariant());
                dayOffset = (target - (int)now.DayOfWeek + 7) % 7;
                dayWord = "weekday";
            }
            else if (Take(DayWordRx, ref rest) is { } dw)
            {
                dayWord = dw.Groups[1].Value.ToLowerInvariant();
                dayOffset = dayWord switch { "завтра" => 1, "послезавтра" => 2, _ => 0 };
            }
        }

        TimeSpan? time = null;
        if (relative is null && Take(TimeRx, ref rest) is { } tm) time = ParseTime(tm);

        TimeSpan? partOfDay = null;
        if (relative is null && Take(PartOfDayRx, ref rest) is { } pd)
        {
            partOfDay = pd.Groups[1].Value.ToLowerInvariant() switch
            {
                "утром" => TimeSpan.FromHours(9),
                "днём" or "днем" => TimeSpan.FromHours(13),
                "вечером" => TimeSpan.FromHours(19),
                _ => TimeSpan.FromHours(23),
            };
        }

        if (relative is null && time is null && partOfDay is null && dayOffset is null && !daily) return null;

        DateTime due;
        if (relative is { } r)
        {
            due = r;
        }
        else
        {
            var clock = time ?? partOfDay ?? TimeSpan.FromHours(9);
            var offset = dayOffset ?? 0;
            due = now.Date.AddDays(offset) + clock;

            if (due <= now)
            {
                if (dayWord is null or "weekday") due = due.AddDays(dayWord is null ? 1 : 7);
                else if (dayWord == "сегодня") return null;
            }
        }

        return new ParsedReminder(CleanText(rest), due, daily);
    }

    private static Match? Take(Regex rx, ref string text)
    {
        var m = rx.Match(text);
        if (!m.Success) return null;
        text = text.Remove(m.Index, m.Length).Insert(m.Index, " ");
        return m;
    }

    private static TimeSpan RelativeSpan(Match m)
    {
        if (m.Groups[3].Success) return TimeSpan.FromMinutes(30);
        if (m.Groups[4].Success)
        {
            return m.Groups[4].Value.ToLowerInvariant() switch
            {
                "час" => TimeSpan.FromHours(1),
                "минуту" => TimeSpan.FromMinutes(1),
                "секунду" => TimeSpan.FromSeconds(1),
                _ => TimeSpan.FromDays(1),
            };
        }

        var n = int.Parse(m.Groups[1].Value);
        var unit = m.Groups[2].Value.ToLowerInvariant();
        if (unit.StartsWith("сек")) return TimeSpan.FromSeconds(n);
        if (unit.StartsWith("мин")) return TimeSpan.FromMinutes(n);
        if (unit.StartsWith("час") || unit == "ч") return TimeSpan.FromHours(n);
        return TimeSpan.FromDays(n);
    }

    private static TimeSpan? ParseTime(Match m)
    {
        var hour = int.Parse(m.Groups[1].Value);
        var minute = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        if (hour > 23 || minute > 59) return null;

        switch (m.Groups[3].Value.ToLowerInvariant())
        {
            case "вечера" when hour < 12:
                hour += 12;
                break;
            case "дня" when hour is >= 1 and <= 5:
                hour += 12;
                break;
            case "ночи" when hour == 12:
                hour = 0;
                break;
        }

        return new TimeSpan(hour, minute, 0);
    }

    private static string CleanText(string text)
    {
        text = LeadRx.Replace(text, "");
        text = Regex.Replace(text, @"\s+", " ").Trim(' ', ',', '.', '-', '—', ':');
        if (text.Length == 0) return Loc.T("Напоминание");
        return char.ToUpperInvariant(text[0]) + text[1..];
    }
}

public sealed class Reminder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public DateTime Due { get; set; }
    public bool Daily { get; set; }
}

public static class ReminderFormat
{
    private static readonly string[] Short = { "вс", "пн", "вт", "ср", "чт", "пт", "сб" };

    public static string Due(DateTime due, bool daily, DateTime now)
    {
        var clock = due.ToString("HH:mm");
        if (daily) return Loc.F("каждый день {0}", clock);
        var days = (due.Date - now.Date).Days;
        return days switch
        {
            0 => Loc.F("сегодня {0}", clock),
            1 => Loc.F("завтра {0}", clock),
            > 1 and < 7 => $"{Loc.T(Short[(int)due.DayOfWeek])} {clock}",
            _ => $"{Loc.DayMonth(due)} {clock}",
        };
    }
}

/// <summary>Persisted reminders; <see cref="TakeDue"/> returns what fired and reschedules the daily ones.</summary>
public sealed class ReminderStore
{
    private readonly List<Reminder> _items;
    private readonly string? _path;

    public ReminderStore(string? path = null)
    {
        _path = path;
        _items = path is null ? new List<Reminder>() : JsonStore.Load(path, () => new List<Reminder>());
    }

    public List<Reminder> Items => _items.OrderBy(r => r.Due).ToList();

    public Reminder Add(ParsedReminder parsed)
    {
        var reminder = new Reminder { Text = parsed.Text, Due = parsed.Due, Daily = parsed.Daily };
        _items.Add(reminder);
        Persist();
        return reminder;
    }

    public void Remove(Reminder reminder)
    {
        if (_items.Remove(reminder)) Persist();
    }

    public List<Reminder> TakeDue(DateTime now)
    {
        var due = _items.Where(r => r.Due <= now).ToList();
        if (due.Count == 0) return due;

        foreach (var r in due)
        {
            if (!r.Daily)
            {
                _items.Remove(r);
                continue;
            }

            while (r.Due <= now) r.Due = r.Due.AddDays(1);
        }

        Persist();
        return due;
    }

    private void Persist()
    {
        if (_path is not null) JsonStore.Save(_path, _items);
    }
}
