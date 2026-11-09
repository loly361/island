using System;
using Island.Services;
using Xunit;

/// <summary>Huge numbers, midnight "at night", "tonight" and am/pm without "at".</summary>
public class ReminderParserEdgeTests
{
    // Monday, 12:00
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0);

    // ---------- numbers out of range: no exception, no reminder ----------

    [Theory]
    [InlineData("in 99999999999 minutes turn off the stove")]
    [InlineData("in 10000 minutes turn off the stove")]
    [InlineData("in 99999999999999999999999 days x")]
    [InlineData("через 99999999999 минут выключить плиту")]
    [InlineData("через 10000 дней выключить плиту")]
    [InlineData("dans 99999999999 minutes éteindre la plaque")]
    [InlineData("dans 10000 heures éteindre la plaque")]
    public void HugeRelativeNumber_IsNotParsed(string input) =>
        Assert.Null(ReminderParser.Parse(input, Now));

    [Theory]
    [InlineData("in 9999 minutes turn off the stove", 9999)]
    [InlineData("через 9999 минут выключить плиту", 9999)]
    [InlineData("dans 9999 minutes éteindre la plaque", 9999)]
    [InlineData("через 0 минут выключить плиту", 0)]
    public void RelativeNumberAtTheLimit_IsParsed(string input, int minutes) =>
        Assert.Equal(Now.AddMinutes(minutes), ReminderParser.Parse(input, Now)!.Due);

    [Fact]
    public void LargestDaysStillFit() =>
        Assert.Equal(Now.AddDays(9999), ReminderParser.Parse("через 9999 дней проверить", Now)!.Due);

    // ---------- 12 at night is midnight (the one that ends the named day) ----------

    [Theory]
    [InlineData("в 12 ночи лечь спать", 27)]
    [InlineData("ночью в 12 лечь спать", 27)]
    [InlineData("сегодня в 12 ночи лечь спать", 27)]
    [InlineData("сегодня ночью в 12 лечь спать", 27)]
    [InlineData("завтра в 12 ночи лечь спать", 28)]
    [InlineData("завтра ночью в 12 лечь спать", 28)]
    [InlineData("at 12 at night go to bed", 27)]
    [InlineData("today at 12 at night go to bed", 27)]
    [InlineData("tomorrow at 12 at night go to bed", 28)]
    [InlineData("demain à 12 h la nuit dormir", 28)]
    public void TwelveAtNight_IsMidnight(string input, int day) =>
        Assert.Equal(new DateTime(2026, 10, day, 0, 0, 0), ReminderParser.Parse(input, Now)!.Due);

    [Theory]
    [InlineData("завтра в 12 обед", 12)]
    [InlineData("tomorrow at 12 lunch", 12)]
    [InlineData("demain à 12h déjeuner", 12)]
    [InlineData("завтра в 2 ночи будильник", 2)]
    [InlineData("tomorrow at 11 at night movie", 23)]
    [InlineData("завтра ночью в 11 кино", 23)]
    public void OtherNightAndNoonHours_AreUnchanged(string input, int hour) =>
        Assert.Equal(new DateTime(2026, 10, 27, hour, 0, 0), ReminderParser.Parse(input, Now)!.Due);

    // ---------- tonight ----------

    [Fact]
    public void AtHourTonight_IsEvening()
    {
        var r = ReminderParser.Parse("call mom at 8 tonight", Now)!;
        Assert.Equal("Call mom", r.Text);
        Assert.Equal(new DateTime(2026, 10, 26, 20, 0, 0), r.Due);
    }

    [Fact]
    public void TonightAtHour_IsEvening() =>
        Assert.Equal(new DateTime(2026, 10, 26, 23, 0, 0), ReminderParser.Parse("tonight at 11 movie", Now)!.Due);

    [Fact]
    public void TonightAlone_IsSevenPmToday()
    {
        var r = ReminderParser.Parse("tonight call mom", Now)!;
        Assert.Equal("Call mom", r.Text);
        Assert.Equal(new DateTime(2026, 10, 26, 19, 0, 0), r.Due);
    }

    // ---------- am/pm without "at" ----------

    [Theory]
    [InlineData("tomorrow 9pm gym", 27, 21, 0)]
    [InlineData("tomorrow 9:30pm gym", 27, 21, 30)]
    [InlineData("tomorrow 9.30 p.m. gym", 27, 21, 30)]
    [InlineData("gym tomorrow 7 am", 27, 7, 0)]
    [InlineData("gym 3pm", 26, 15, 0)]
    [InlineData("gym 12am", 27, 0, 0)]
    public void AmPmWithoutAt(string input, int day, int hour, int minute)
    {
        var r = ReminderParser.Parse(input, Now)!;
        Assert.Equal("Gym", r.Text);
        Assert.Equal(new DateTime(2026, 10, day, hour, minute, 0), r.Due);
    }

    [Fact]
    public void AmWithoutAt_PastHour_MovesToTomorrow()
    {
        var r = ReminderParser.Parse("9am call", Now)!;
        Assert.Equal("Call", r.Text);
        Assert.Equal(new DateTime(2026, 10, 27, 9, 0, 0), r.Due);
    }

    [Theory]
    [InlineData("buy 2 apples tomorrow", "Buy 2 apples")]
    [InlineData("I am late tomorrow", "I am late")]
    [InlineData("room 5 tomorrow", "Room 5")]
    public void NumbersWithoutAmPm_StayInTheText(string input, string text)
    {
        var r = ReminderParser.Parse(input, Now)!;
        Assert.Equal(text, r.Text);
        Assert.Equal(new DateTime(2026, 10, 27, 9, 0, 0), r.Due);
    }

    [Fact]
    public void BareNumberWithoutAnyTimeWord_IsNotParsed() =>
        Assert.Null(ReminderParser.Parse("buy 2 apples", Now));

    // ---------- no regressions for Russian and French ----------

    [Theory]
    [InlineData("позвонить маме завтра в 9", "Позвонить маме", 27, 9, 0)]
    [InlineData("завтра в 7 вечера ужин", "Ужин", 27, 19, 0)]
    [InlineData("в 15:30 созвон", "Созвон", 26, 15, 30)]
    [InlineData("appeler maman demain à 9h", "Appeler maman", 27, 9, 0)]
    [InlineData("demain à 14h30 réunion", "Réunion", 27, 14, 30)]
    [InlineData("demain soir à 8 dîner", "Dîner", 27, 20, 0)]
    public void RussianAndFrench_Unchanged(string input, string text, int day, int hour, int minute)
    {
        var r = ReminderParser.Parse(input, Now)!;
        Assert.Equal(text, r.Text);
        Assert.Equal(new DateTime(2026, 10, day, hour, minute, 0), r.Due);
    }
}
