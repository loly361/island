using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Island.Services;

public sealed class AppShortcut
{
    public string Name { get; set; } = "";
    public string Glyph { get; set; } = "square.grid.2x2";
    public string Target { get; set; } = "";
}

public sealed class Settings
{
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? City { get; set; }
    public string SearchUrl { get; set; } = "https://www.google.com/search?q={0}";
    public string AskUrl { get; set; } = "https://claude.ai/new?q={0}";
    public int NotifyPort { get; set; } = 47831;
    public bool NotificationsEnabled { get; set; } = true;
    /// <summary>A fixed accent as "#RRGGBB"; null follows the wallpaper.</summary>
    public string? AccentHex { get; set; }
    /// <summary>Shared secret that programs must send to POST /notify; generated on first run.</summary>
    public string NotifyToken { get; set; } = "";
    /// <summary>"standard" shows the full pill; "minimal" collapses to a small circle that opens on hover.</summary>
    public string Mode { get; set; } = ModeStandard;
    /// <summary>Interface language: "ru", "fr" or "en". Read once at startup, so a change applies after a restart.</summary>
    public string Language { get; set; } = "ru";
    /// <summary>Look for a new release on GitHub 30 seconds after start and then once a day.</summary>
    public bool AutoUpdateCheck { get; set; } = true;
    /// <summary>The release version the user has already been told about; no further pop-up until a newer one appears.</summary>
    public string? DismissedUpdateVersion { get; set; }

    public const string ModeStandard = "standard";
    public const string ModeMinimal = "minimal";

    /// <summary>Replaces every value with the other instance's, so the open window sees edits made on disk.</summary>
    public void CopyFrom(Settings other)
    {
        Latitude = other.Latitude;
        Longitude = other.Longitude;
        City = other.City;
        SearchUrl = other.SearchUrl;
        AskUrl = other.AskUrl;
        NotifyPort = other.NotifyPort;
        NotificationsEnabled = other.NotificationsEnabled;
        AccentHex = other.AccentHex;
        NotifyToken = other.NotifyToken;
        Mode = other.Mode;
        Language = other.Language;
        AutoUpdateCheck = other.AutoUpdateCheck;
        DismissedUpdateVersion = other.DismissedUpdateVersion;
        Shortcuts = other.Shortcuts;
    }
    public List<AppShortcut> Shortcuts { get; set; } = new()
    {
        new AppShortcut { Name = "Проводник", Glyph = "folder", Target = "explorer.exe" },
        new AppShortcut { Name = "Терминал", Glyph = "terminal", Target = "wt.exe" },
        new AppShortcut { Name = "Блокнот", Glyph = "square.and.pencil", Target = "notepad.exe" },
        new AppShortcut { Name = "Калькулятор", Glyph = "calculator", Target = "calc.exe" },
        new AppShortcut { Name = "Параметры", Glyph = "gearshape", Target = "ms-settings:" },
    };
}

public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island");

    public static string File(string name) => Path.Combine(DataDir, name);
}

/// <summary>Tiny JSON persistence: tolerant reads, atomic writes.</summary>
public static class JsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static T Load<T>(string path, Func<T> fallback)
    {
        try
        {
            if (!System.IO.File.Exists(path)) return fallback();
            return JsonSerializer.Deserialize<T>(System.IO.File.ReadAllText(path), Options) ?? fallback();
        }
        catch
        {
            return fallback();
        }
    }

    /// <summary>Like <see cref="Load{T}"/> but returns null when the file is missing or broken, so callers keep their current data.</summary>
    public static T? TryLoad<T>(string path) where T : class
    {
        try
        {
            if (!System.IO.File.Exists(path)) return null;
            return JsonSerializer.Deserialize<T>(System.IO.File.ReadAllText(path), Options);
        }
        catch
        {
            return null;
        }
    }

    public static void Save<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
            System.IO.File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            // Persistence is best-effort; the app keeps working from memory.
        }
    }
}
