using System;
using Island.Services;
using Xunit;

public class ReminderParserEnglishTests
{
    // Monday, 12:00
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0);

    [Fact]
    public void TomorrowAtHour()
    {
        var r = ReminderParser.Parse("call mom tomorrow at 9", Now)!;
        Assert.Equal("Call mom", r.Text);
        Assert.Equal(new DateTime(2026, 10, 27, 9, 0, 0), r.Due);
        Assert.False(r.Daily);
    }

    [Fact]
    public void EveryDayAtPm()
    {
        var r = ReminderParser.Parse("pills every day at 11pm", Now)!;
        Assert.Equal("Pills", r.Text);
        Assert.Equal(new DateTime(2026, 10, 26, 23, 0, 0), r.Due);
        Assert.True(r.Daily);
    }

    [Fact]
    public void DailyAtClock()
    {
        var r = ReminderParser.Parse("daily at 9:30 standup", Now)!;
        Assert.Equal("Standup", r.Text);
        Assert.Equal(new DateTime(2026, 10, 27, 9, 30, 0), r.Due);
        Assert.True(r.Daily);
    }

    [Theory]
    [InlineData("in 20 minutes turn off the stove", 20)]
    [InlineData("in 2 hours turn off the stove", 120)]
    [InlineData("in half an hour turn off the stove", 30)]
    [InlineData("in an hour turn off the stove", 60)]
    public void InRelative(string input, int minutes)
    {
        var r = ReminderParser.Parse(input, Now)!;
        Assert.Equal("Turn off the stove", r.Text);
        Assert.Equal(Now.AddMinutes(minutes), r.Due);
    }

    [Fact]
    public void RemindMeToOnWeekdayAm()
    {
        var r = ReminderParser.Parse("remind me to send the report on Friday at 9 am", Now)!;
        Assert.Equal("Send the report", r.Text);
        Assert.Equal(new DateTime(2026, 10, 30, 9, 0, 0), r.Due);
    }

    [Fact]
    public void RemindMeThatWeekdayWithoutOn()
    {
        var r = ReminderParser.Parse("remind me that Thursday at 6:30pm meeting", Now)!;
        Assert.Equal("Meeting", r.Text);
        Assert.Equal(new DateTime(2026, 10, 29, 18, 30, 0), r.Due);
    }

    [Fact]
    public void DayAfterTomorrow()
    {
        var r = ReminderParser.Parse("the day after tomorrow at 14:15 doctor", Now)!;
        Assert.Equal("Doctor", r.Text);
        Assert.Equal(new DateTime(2026, 10, 28, 14, 15, 0), r.Due);
    }

    [Fact]
    public void TodayAtPm()
    {
        var r = ReminderParser.Parse("today at 3pm buy groceries", Now)!;
        Assert.Equal("Buy groceries", r.Text);
        Assert.Equal(new DateTime(2026, 10, 26, 15, 0, 0), r.Due);
    }

    [Fact]
    public void TodayInThePastIsRejected() =>
        Assert.Null(ReminderParser.Parse("today at 9 am buy groceries", Now));

    [Theory]
    [InlineData("tomorrow morning water the plants", 9)]
    [InlineData("tomorrow afternoon water the plants", 13)]
    [InlineData("tomorrow evening water the plants", 19)]
    [InlineData("tomorrow at night water the plants", 23)]
    [InlineData("tomorrow at 9 in the evening water the plants", 21)]
    public void PartsOfDay(string input, int hour)
    {
        var r = ReminderParser.Parse(input, Now)!;
        Assert.Equal("Water the plants", r.Text);
        Assert.Equal(new DateTime(2026, 10, 27, hour, 0, 0), r.Due);
    }

    [Theory]
    [InlineData("завтра днём в 11 обед", 11)]
    [InlineData("завтра днём в 2 обед", 14)]
    [InlineData("завтра днём в 12 обед", 12)]
    [InlineData("завтра вечером в 7 обед", 19)]
    [InlineData("demain après-midi à 10 réunion", 10)]
    [InlineData("demain après-midi à 3 réunion", 15)]
    [InlineData("demain soir à 8 réunion", 20)]
    [InlineData("tomorrow afternoon at 11 lunch", 11)]
    [InlineData("tomorrow at 10 in the afternoon lunch", 10)]
    [InlineData("tomorrow afternoon at 2 lunch", 14)]
    [InlineData("tomorrow evening at 7 lunch", 19)]
    public void PartOfDayShiftsOnlyMatchingHours(string input, int hour)
    {
        var r = ReminderParser.Parse(input, Now)!;
        Assert.Equal(new DateTime(2026, 10, 27, hour, 0, 0), r.Due);
    }

    [Fact]
    public void TwelveAmIsMidnight()
    {
        var r = ReminderParser.Parse("backup tomorrow at 12 am", Now)!;
        Assert.Equal(new DateTime(2026, 10, 27, 0, 0, 0), r.Due);
    }

    [Fact]
    public void LeadWordsDoNotEatTheTask()
    {
        var r = ReminderParser.Parse("remind me tomatoes tomorrow at 9", Now)!;
        Assert.Equal("Tomatoes", r.Text);
    }

    [Fact]
    public void EnglishWithoutTimeIsNotParsed() =>
        Assert.Null(ReminderParser.Parse("call mom", Now));
}
