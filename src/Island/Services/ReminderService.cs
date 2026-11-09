using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Island.Services;

public sealed record ParsedReminder(string Text, DateTime Due, bool Daily);

/// <summary>
/// Understands short phrases in Russian («позвонить маме завтра в 9», «через 20 минут выключить плиту»), in French
/// («appeler maman demain à 9», «dans 20 minutes éteindre la plaque») and in English («call mom tomorrow at 9»,
/// «in 20 minutes turn off the stove»). The words of the input decide the language, not the interface setting.
/// </summary>
public static class ReminderParser
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex DailyRx = new(
        @"\b(каждый\s+день|ежедневно|tous\s+les\s+jours|chaque\s+jour|quotidiennement|every\s*day|daily)\b", Opts);

    private static readonly Regex RelativeRx = new(
        @"\bчерез\s+(?:(\d+)\s*(сек\w*|мин\w*|час\w*|ч|дн\w*|день|дня|дней)|(полчаса)|(час|минуту|секунду|день))(?!\w)", Opts);

    private static readonly Regex RelativeFrRx = new(
        @"\bdans\s+(?:(\d+)\s*(secondes?|minutes?|heures?|h|jours?)|(une\s+demi-heure)|(une\s+heure|une\s+minute|une\s+seconde|un\s+jour))(?!\w)", Opts);

    private static readonly Regex RelativeEnRx = new(
        @"\bin\s+(?:(\d+)\s*(seconds?|secs?|minutes?|mins?|m|hours?|hrs?|h|days?)|(half\s+an\s+hour)|(an\s+hour|a\s+minute|a\s+second|a\s+day))(?!\w)", Opts);

    private static readonly Regex DayWordRx = new(
        @"\b(сегодня|завтра|послезавтра|aujourd['’]hui|demain|après-demain|today|(?:the\s+)?day\s+after\s+tomorrow|tomorrow)\b", Opts);

    private static readonly Regex WeekdayRx = new(
        @"\b(?:(?:в|во)\s+(понедельник|вторник|среду|четверг|пятницу|субботу|воскресенье)|(?:le\s+)?(lundi|mardi|mercredi|jeudi|vendredi|samedi|dimanche)|(?:on\s+)?(monday|tuesday|wednesday|thursday|friday|saturday|sunday))\b", Opts);

    private static readonly Regex TimeRx = new(
        @"\b(?:в|к)\s+(\d{1,2})(?:[:.](\d{2}))?(?:\s*час(?:ов|а)?)?(?:\s+(утра|дня|вечера|ночи))?(?!\d)", Opts);

    private static readonly Regex TimeFrRx = new(
        @"\bà\s+(\d{1,2})(?:[:h](\d{2}))?(?:\s*h(?:eures?)?)?(?!\w)", Opts);

    /// <summary>"at 9", "at 9:30", "at 9pm", "at 9 a.m.", and without "at" only with am/pm ("9pm", "9:30 p.m.");
    /// group 3 is the am/pm marker.</summary>
    private static readonly Regex TimeEnRx = new(
        @"\b(?:at\s+|(?=\d{1,2}(?:[:.]\d{2})?\s*[ap]\.?m\.?(?!\w)))(\d{1,2})(?:[:.](\d{2}))?(?:\s*(a\.?m\.?|p\.?m\.?)|\s*o['’]clock)?(?!\w)", Opts);

    private static readonly Regex PartOfDayRx = new(
        @"\b(?:(?:in\s+the|this|at)\s+)?(утром|днём|днем|вечером|ночью|matin|après-midi|soir|nuit|morning|afternoon|evening|tonight|night)\b", Opts);

    /// <summary>Upper bound for "in N minutes" and the like; larger numbers make the phrase unparsable.</summary>
    private const int MaxRelativeAmount = 9999;

    private static readonly Regex LeadRx = new(
        @"^\s*(?:(?:напомни(?:ть)?|напоминай)(?:\s+мне)?|(?:rappelle-moi|rappelez-moi)(?:\s+de|\s+que)?|remind\s+me(?:\s+(?:to|that|about)\b)?)\s*(?:,|что|о\s+том,?\s+что)?\s*", Opts);

    private static readonly string[] Weekdays =
        { "воскресенье", "понедельник", "вторник", "среду", "четверг", "пятницу", "субботу" };

    private static readonly string[] FrenchWeekdays =
        { "dimanche", "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi" };

    private static readonly string[] EnglishWeekdays =
        { "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday" };

    public static ParsedReminder? Parse(string? input, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var rest = input.Trim();

        var daily = Take(DailyRx, ref rest) is not null;

        DateTime? relative = null;
        TimeSpan? span = null;
        var hasRelative = true;
        if (Take(RelativeRx, ref rest) is { } rel)
            span = RelativeSpan(rel);
        else if (Take(RelativeFrRx, ref rest) is { } relFr)
            span = RelativeSpanFr(relFr);
        else if (Take(RelativeEnRx, ref rest) is { } relEn)
            span = RelativeSpanEn(relEn);
        else
            hasRelative = false;
        if (hasRelative)
        {
            // "in 99999999999 minutes": a number out of range makes the whole phrase unparsable.
            if (span is not { } s) return null;
            relative = now + s;
        }

        int? dayOffset = null;
        string? dayWord = null;
        if (relative is null)
        {
            if (Take(WeekdayRx, ref rest) is { } wd)
            {
                var group = wd.Groups[1].Success ? wd.Groups[1] : wd.Groups[2].Success ? wd.Groups[2] : wd.Groups[3];
                var name = group.Value.ToLowerInvariant();
                var target = Array.IndexOf(Weekdays, name);
                if (target < 0) target = Array.IndexOf(FrenchWeekdays, name);
                if (target < 0) target = Array.IndexOf(EnglishWeekdays, name);
                dayOffset = (target - (int)now.DayOfWeek + 7) % 7;
                dayWord = "weekday";
            }
            else if (Take(DayWordRx, ref rest) is { } dw)
            {
                dayWord = CanonicalDay(dw.Groups[1].Value);
                dayOffset = dayWord switch { "завтра" => 1, "послезавтра" => 2, _ => 0 };
            }
        }

        TimeSpan? time = null;
        var timeHasMarker = false;
        var midnight = false;
        if (relative is null && (Take(TimeRx, ref rest) ?? Take(TimeFrRx, ref rest) ?? Take(TimeEnRx, ref rest)) is { } tm)
        {
            time = ParseTime(tm);
            timeHasMarker = tm.Groups[3].Success;
            // «в 12 ночи» is the midnight that ends the day.
            midnight = time is { Hours: 0 } && tm.Groups[1].Value == "12"
                && tm.Groups[3].Value.Equals("ночи", StringComparison.OrdinalIgnoreCase);
        }

        TimeSpan? partOfDay = null;
        if (relative is null && Take(PartOfDayRx, ref rest) is { } pd)
        {
            partOfDay = pd.Groups[1].Value.ToLowerInvariant() switch
            {
                "утром" or "matin" or "morning" => TimeSpan.FromHours(9),
                "днём" or "днем" or "après-midi" or "afternoon" => TimeSpan.FromHours(13),
                "вечером" or "soir" or "evening" or "tonight" => TimeSpan.FromHours(19),
                _ => TimeSpan.FromHours(23),
            };
        }

        // "at 9 in the evening" / «в 7 вечером»: a 12-hour clock plus a later part of the day means the afternoon hour,
        // but only when the hour fits that part of the day («днём в 11» stays 11:00, «днём в 2» is 14:00).
        if (time is { } t && !timeHasMarker && partOfDay is { } p && t.Hours < 12
            && ((p.Hours == 13 && t.Hours is >= 1 and <= 6) || (p.Hours == 19 && t.Hours >= 4) || (p.Hours == 23 && t.Hours >= 6)))
            time = t + TimeSpan.FromHours(12);

        // «ночью в 12», "at 12 at night", «à 12 h la nuit»: midnight, not noon.
        if (time is { Hours: 12 } noon && !timeHasMarker && partOfDay is { Hours: 23 })
        {
            time = noon - TimeSpan.FromHours(12);
            midnight = true;
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
            // Midnight "at night" closes the named day, so it falls on the next date.
            var offset = (dayOffset ?? 0) + (midnight ? 1 : 0);
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

    /// <summary>Maps the French and English day words onto the Russian ones the rest of the parser uses.</summary>
    private static string CanonicalDay(string word) =>
        Regex.Replace(word.ToLowerInvariant().Replace('’', '\''), @"\s+", " ") switch
        {
            "aujourd'hui" or "today" => "сегодня",
            "demain" or "tomorrow" => "завтра",
            "après-demain" or "day after tomorrow" or "the day after tomorrow" => "послезавтра",
            var w => w,
        };

    /// <summary>The digits of "in N …" as a number in 0..<see cref="MaxRelativeAmount"/>, or null.</summary>
    private static int? Amount(Group digits) =>
        int.TryParse(digits.Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
        && n <= MaxRelativeAmount ? n : null;

    private static TimeSpan? RelativeSpanEn(Match m)
    {
        if (m.Groups[3].Success) return TimeSpan.FromMinutes(30);
        if (m.Groups[4].Success)
        {
            return Regex.Replace(m.Groups[4].Value.ToLowerInvariant(), @"\s+", " ") switch
            {
                "an hour" => TimeSpan.FromHours(1),
                "a minute" => TimeSpan.FromMinutes(1),
                "a second" => TimeSpan.FromSeconds(1),
                _ => TimeSpan.FromDays(1),
            };
        }

        if (Amount(m.Groups[1]) is not { } n) return null;
        var unit = m.Groups[2].Value.ToLowerInvariant();
        if (unit.StartsWith("sec")) return TimeSpan.FromSeconds(n);
        if (unit.StartsWith("m")) return TimeSpan.FromMinutes(n);
        if (unit.StartsWith("h")) return TimeSpan.FromHours(n);
        return TimeSpan.FromDays(n);
    }

    private static TimeSpan? RelativeSpan(Match m)
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

        if (Amount(m.Groups[1]) is not { } n) return null;
        var unit = m.Groups[2].Value.ToLowerInvariant();
        if (unit.StartsWith("сек")) return TimeSpan.FromSeconds(n);
        if (unit.StartsWith("мин")) return TimeSpan.FromMinutes(n);
        if (unit.StartsWith("час") || unit == "ч") return TimeSpan.FromHours(n);
        return TimeSpan.FromDays(n);
    }

    private static TimeSpan? RelativeSpanFr(Match m)
    {
        if (m.Groups[3].Success) return TimeSpan.FromMinutes(30);
        if (m.Groups[4].Success)
        {
            return m.Groups[4].Value.ToLowerInvariant() switch
            {
                "une heure" => TimeSpan.FromHours(1),
                "une minute" => TimeSpan.FromMinutes(1),
                "une seconde" => TimeSpan.FromSeconds(1),
                _ => TimeSpan.FromDays(1),
            };
        }

        if (Amount(m.Groups[1]) is not { } n) return null;
        var unit = m.Groups[2].Value.ToLowerInvariant();
        if (unit.StartsWith("seconde")) return TimeSpan.FromSeconds(n);
        if (unit.StartsWith("minute")) return TimeSpan.FromMinutes(n);
        if (unit == "h" || unit.StartsWith("heure")) return TimeSpan.FromHours(n);
        return TimeSpan.FromDays(n);
    }

    private static TimeSpan? ParseTime(Match m)
    {
        var hour = int.Parse(m.Groups[1].Value);
        var minute = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        if (hour > 23 || minute > 59) return null;

        switch (m.Groups[3].Value.ToLowerInvariant().Replace(".", ""))
        {
            case "pm" when hour < 12:
                hour += 12;
                break;
            case "am" when hour == 12:
                hour = 0;
                break;
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
