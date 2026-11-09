using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Markup;
using Island.Services;

namespace Island.Tests;

/// <summary>These tests switch the global Loc.Language, so they never run alongside the other tests.</summary>
[CollectionDefinition(nameof(LanguageSwitchCollection), DisableParallelization = true)]
public class LanguageSwitchCollection;

[Collection(nameof(LanguageSwitchCollection))]
public class LocTests
{
    private static void WithLanguage(string language, Action action)
    {
        Loc.Language = language;
        try { action(); }
        finally { Loc.Language = Loc.Russian; }
    }

    [Fact]
    public void Russian_ReturnsOriginal() =>
        WithLanguage("ru", () => Assert.Equal("Погода", Loc.T("Погода")));

    [Fact]
    public void French_ReturnsTranslationForKnownKey() =>
        WithLanguage("fr", () => Assert.Equal("Météo", Loc.T("Погода")));

    [Fact]
    public void French_FallsBackToRussianWhenMissing() =>
        WithLanguage("fr", () => Assert.Equal("Строка без перевода", Loc.T("Строка без перевода")));

    [Fact]
    public void UnknownLanguage_FallsBackToRussian()
    {
        WithLanguage("de", () =>
        {
            Assert.Equal("ru", Loc.Language);
            Assert.Equal("Погода", Loc.T("Погода"));
        });
    }

    [Fact]
    public void Format_FillsTranslatedTemplate() =>
        WithLanguage("fr", () => Assert.Equal("Ressenti 5°", Loc.F("Ощущается как {0}°", 5)));

    [Fact]
    public void RelativeTimeAndReminderLabels_FollowLanguage()
    {
        var now = new DateTime(2026, 11, 9, 12, 0, 0);
        WithLanguage("fr", () =>
        {
            Assert.Equal("hier", TimeLabels.Ago(now.AddHours(-30), now));
            Assert.Equal("demain 21:00", ReminderFormat.Due(now.Date.AddDays(1).AddHours(21), false, now));
            Assert.Equal("20/11 21:00", ReminderFormat.Due(new DateTime(2026, 11, 20, 21, 0, 0), false, now));
        });
        Assert.Equal("вчера", TimeLabels.Ago(now.AddHours(-30), now));
        Assert.Equal("20.11 21:00", ReminderFormat.Due(new DateTime(2026, 11, 20, 21, 0, 0), false, now));
    }

    [Fact]
    public void English_TranslatesTextLabelsAndDates()
    {
        var now = new DateTime(2026, 11, 9, 12, 0, 0);
        WithLanguage("en", () =>
        {
            Assert.Equal("en", Loc.Language);
            Assert.Equal("en-US", Loc.Culture.Name);
            Assert.Equal("Weather", Loc.T("Погода"));
            Assert.Equal("Feels like 5°", Loc.F("Ощущается как {0}°", 5));
            Assert.Equal("yesterday", TimeLabels.Ago(now.AddHours(-30), now));
            Assert.Equal("tomorrow 21:00", ReminderFormat.Due(now.Date.AddDays(1).AddHours(21), false, now));
            Assert.Equal("Nov 20 21:00", ReminderFormat.Due(new DateTime(2026, 11, 20, 21, 0, 0), false, now));
            Assert.Equal("Строка без перевода", Loc.T("Строка без перевода"));
        });
    }

    [Fact]
    public void XamlExtension_ParsesQuotedTextWithCommasAndBraces()
    {
        const string xaml =
            "<StackPanel xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
            "xmlns:loc='clr-namespace:Island.Services;assembly=Island'>" +
            "<TextBlock Text=\"{loc:T 'адрес с \\{0\\}'}\"/>" +
            "<TextBlock Text=\"{loc:T 'Здесь появится всё, что вы копируете'}\"/>" +
            "<TextBlock Text=\"{loc:T Погода}\"/>" +
            "</StackPanel>";

        string[] Parse()
        {
            string[] result = Array.Empty<string>();
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var panel = (StackPanel)XamlReader.Parse(xaml);
                    result = panel.Children.OfType<TextBlock>().Select(t => t.Text).ToArray();
                }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error is not null) throw error;
            return result;
        }

        Assert.Equal(new[] { "адрес с {0}", "Здесь появится всё, что вы копируете", "Погода" }, Parse());
        WithLanguage("fr", () =>
            Assert.Equal(new[] { "adresse avec {0}", "Tout ce que vous copiez apparaîtra ici", "Météo" }, Parse()));
    }
}

public class LocCoverageTests
{
    private static readonly string SourceDir = FindSourceDir();

    private static string FindSourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Island", "Island.csproj"))) dir = dir.Parent;
        return dir is null ? "" : Path.Combine(dir.FullName, "src", "Island");
    }

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(SourceDir, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    public static TheoryData<string> Translated => new() { Loc.French, Loc.English };

    private static IEnumerable<string> Holes(string text) =>
        Regex.Matches(text, @"\{\d+[^}]*\}").Select(x => x.Value).Order();

    [Fact]
    public void Settings_CopyFromCopiesLanguage()
    {
        var target = new Settings();
        target.CopyFrom(new Settings { Language = "fr" });
        Assert.Equal("fr", target.Language);
        target.CopyFrom(new Settings { Language = "en" });
        Assert.Equal("en", target.Language);
        Assert.Equal("ru", new Settings().Language);
    }

    [Theory]
    [InlineData("ru", "ru")]
    [InlineData("fr", "fr")]
    [InlineData("FR", "fr")]
    [InlineData("en", "en")]
    [InlineData("En", "en")]
    [InlineData("de", "ru")]
    [InlineData("", "ru")]
    [InlineData(null, "ru")]
    public void Normalize_KnowsThreeLanguages(string? input, string expected) =>
        Assert.Equal(expected, Loc.Normalize(input));

    [Fact]
    public void Dictionaries_HaveTheSameKeys()
    {
        var fr = Loc.Keys(Loc.French).ToHashSet();
        var en = Loc.Keys(Loc.English).ToHashSet();
        Assert.NotEmpty(fr);
        Assert.Empty(fr.Except(en));
        Assert.Empty(en.Except(fr));
    }

    [Theory]
    [MemberData(nameof(Translated))]
    public void EveryDictionaryEntry_KeepsPlaceholders(string language)
    {
        var problems = Loc.Keys(language)
            .Where(key => !Holes(key).SequenceEqual(Holes(Loc.Translate(key, language))))
            .ToList();
        Assert.Empty(problems);
    }

    [Theory]
    [MemberData(nameof(Translated))]
    public void EveryXamlKey_HasTranslation(string language)
    {
        Assert.True(Directory.Exists(SourceDir), SourceDir);
        var missing = new List<string>();
        foreach (var file in SourceFiles("*.xaml"))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\{loc:T '((?:[^'\\]|\\.)*)'\}"))
            {
                var key = Regex.Replace(m.Groups[1].Value, @"\\(.)", "$1");
                if (!Loc.HasTranslation(key, language)) missing.Add(key);
            }
        }
        Assert.Empty(missing);
    }

    [Theory]
    [MemberData(nameof(Translated))]
    public void EveryCodeKey_HasTranslationWithSamePlaceholders(string language)
    {
        Assert.True(Directory.Exists(SourceDir), SourceDir);
        var problems = new List<string>();
        foreach (var file in SourceFiles("*.cs"))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"Loc\.[TF]\(""((?:[^""\\]|\\.)*)"""))
            {
                var key = m.Groups[1].Value;
                if (!Loc.HasTranslation(key, language)) { problems.Add("missing: " + key); continue; }
                if (!Holes(key).SequenceEqual(Holes(Loc.Translate(key, language)))) problems.Add("placeholders: " + key);
            }
        }
        Assert.Empty(problems);
    }
}
