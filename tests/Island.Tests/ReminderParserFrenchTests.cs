using System;
using Island.Services;
using Xunit;

public class ReminderParserFrenchTests
{
    // Monday, 12:00
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0);

    [Fact]
    public void DemainAHeure()
    {
        var r = ReminderParser.Parse("appeler maman demain à 9", Now)!;
        Assert.Equal("Appeler maman", r.Text);
        Assert.Equal(new DateTime(2026, 10, 27, 9, 0, 0), r.Due);
        Assert.False(r.Daily);
    }

    [Fact]
    public void TousLesJoursAvecHeure()
    {
        var r = ReminderParser.Parse("cachets tous les jours à 23:00", Now)!;
        Assert.Equal("Cachets", r.Text);
        Assert.Equal(new DateTime(2026, 10, 26, 23, 0, 0), r.Due);
        Assert.True(r.Daily);
    }

    [Fact]
    public void DansMinutes()
    {
        var r = ReminderParser.Parse("dans 20 minutes éteindre la plaque", Now)!;
        Assert.Equal("Éteindre la plaque", r.Text);
        Assert.Equal(Now.AddMinutes(20), r.Due);
    }

    [Fact]
    public void JourDeSemaineAvecHeureHDeLaRelance()
    {
        var r = ReminderParser.Parse("rappelle-moi jeudi à 18h30 réunion", Now)!;
        Assert.Equal("Réunion", r.Text);
        Assert.Equal(new DateTime(2026, 10, 29, 18, 30, 0), r.Due);
    }

    [Fact]
    public void ApresDemain()
    {
        var r = ReminderParser.Parse("après-demain à 14h15 médecin", Now)!;
        Assert.Equal("Médecin", r.Text);
        Assert.Equal(new DateTime(2026, 10, 28, 14, 15, 0), r.Due);
    }

    [Fact]
    public void AujourdhuiAvecHeureSansMinutes()
    {
        var r = ReminderParser.Parse("aujourd'hui à 15 h faire les courses", Now)!;
        Assert.Equal("Faire les courses", r.Text);
        Assert.Equal(new DateTime(2026, 10, 26, 15, 0, 0), r.Due);
    }

    [Fact]
    public void FrenchWithoutTimeIsNotParsed()
    {
        Assert.Null(ReminderParser.Parse("appeler maman", Now));
    }
}
