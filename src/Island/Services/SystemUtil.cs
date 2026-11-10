using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Island.Services;

public static class SystemUtil
{
    public static void Launch(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            // Missing program or blocked URL: nothing sensible to show beyond staying silent.
        }
    }

    /// <summary>Opens Explorer on the file's folder with the file selected.</summary>
    public static void RevealInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            Launch(System.IO.Path.GetDirectoryName(path) ?? path);
        }
    }

    public static string FriendlyProcessName(string? process) => (process ?? "").ToLowerInvariant() switch
    {
        "" => "Буфер",
        "chrome" => "Chrome",
        "msedge" => "Edge",
        "firefox" => "Firefox",
        "yandex" or "browser" => "Яндекс Браузер",
        "telegram" => "Telegram",
        "discord" => "Discord",
        "code" => "VS Code",
        "explorer" => "Проводник",
        "notepad" => "Блокнот",
        "windowsterminal" => "Терминал",
        "winword" => "Word",
        "excel" => "Excel",
        "claude" => "Claude",
        var other => char.ToUpperInvariant(other[0]) + other[1..],
    };
}

public static class ClipboardUtil
{
    private const string ExcludeFormat = "ExcludeClipboardContentFromMonitorProcessing";

    /// <summary>Password managers flag sensitive copies with this format; history must skip them.</summary>
    public static bool IsExcludedFromHistory() => Try(() => Clipboard.ContainsData(ExcludeFormat), false);

    public static string? GetText() => Try(() => Clipboard.ContainsText() ? Clipboard.GetText() : null, null);

    public static bool SetText(string text) => Try(() => { Clipboard.SetText(text); return true; }, false);

    public static bool SetImage(BitmapSource image) => Try(() => { Clipboard.SetImage(image); return true; }, false);

    // The clipboard is a shared, briefly-locked resource; a few quick retries cover contention.
    private static T Try<T>(Func<T> action, T fallback)
    {
        for (var i = 0; i < 6; i++)
        {
            try
            {
                return action();
            }
            catch (COMException)
            {
                Thread.Sleep(30);
            }
            catch (ExternalException)
            {
                Thread.Sleep(30);
            }
        }

        return fallback;
    }
}

public static class Autostart
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Island";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key, writable: true);
        if (key is null) return;
        if (enabled)
        {
            // Only the shipped exe may be registered: a debug run under "dotnet" must not replace the entry.
            var exe = Environment.ProcessPath;
            if (exe is not null && System.IO.Path.GetFileName(exe).Equals("Island.exe", StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(ValueName, $"\"{exe}\"");
            }
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
