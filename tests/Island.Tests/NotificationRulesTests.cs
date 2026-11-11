using System.Collections.Generic;
using Island.Services;
using Xunit;

public class NotificationRulesTests
{
    [Fact]
    public void IsMuted_MatchesIgnoringCaseAndSpaces()
    {
        var muted = new List<string> { "Telegram" };
        Assert.True(NotificationRules.IsMuted(muted, "  telegram "));
        Assert.False(NotificationRules.IsMuted(muted, "Teams"));
    }

    [Fact]
    public void IsMuted_NullListOrBlankAppIsFalse()
    {
        Assert.False(NotificationRules.IsMuted(null, "Telegram"));
        Assert.False(NotificationRules.IsMuted(new List<string> { "" }, "   "));
        Assert.False(NotificationRules.IsMuted(new List<string> { "Telegram" }, null));
    }

    [Fact]
    public void ToggleMuted_AddsAppAndReturnsTrue()
    {
        var muted = new List<string>();
        Assert.True(NotificationRules.ToggleMuted(muted, "  Telegram "));
        Assert.Equal(new[] { "Telegram" }, muted);
    }

    [Fact]
    public void ToggleMuted_SecondCallRemovesAppAndReturnsFalse()
    {
        var muted = new List<string>();
        NotificationRules.ToggleMuted(muted, "Telegram");
        Assert.False(NotificationRules.ToggleMuted(muted, "telegram"));
        Assert.Empty(muted);
    }

    [Fact]
    public void ToggleMuted_BlankAppChangesNothing()
    {
        var muted = new List<string> { "Telegram" };
        Assert.False(NotificationRules.ToggleMuted(muted, "  "));
        Assert.Equal(new[] { "Telegram" }, muted);
    }
}
