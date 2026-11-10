using System;
using System.Collections.Generic;

namespace Island.Services;

/// <summary>Values of SHQueryUserNotificationState (QUERY_USER_NOTIFICATION_STATE).</summary>
public enum ShellNotificationState
{
    Unknown = 0,
    NotPresent = 1,
    Busy = 2,
    RunningD3DFullScreen = 3,
    PresentationMode = 4,
    AcceptsNotifications = 5,
    QuietTime = 6,
    App = 7,
}

/// <summary>What the game mode needs to know about the foreground window, read by <see cref="ForegroundProbe"/>.</summary>
public readonly record struct ForegroundWindowInfo(
    nint Handle,
    string ClassName,
    bool OwnProcess,
    bool Visible,
    bool Minimized,
    bool Maximized,
    bool HasCaption,
    PixelRect Bounds,
    PixelRect Monitor,
    bool OnPrimaryMonitor)
{
    public static ForegroundWindowInfo None => new(0, "", false, false, false, false, false, default, default, false);
}

/// <summary>Decides whether a full-screen app (a game, a video, a slide show) is in front. Pure logic.</summary>
public static class FullscreenDetector
{
    /// <summary>Desktop, taskbar, Alt+Tab, Task View and Start: they can cover the screen but are not apps to hide for.</summary>
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "MultitaskingViewFrame",
        "XamlExplorerHostIslandWindow",
        "ForegroundStaging",
        "TaskListThumbnailWnd",
        "Windows.UI.Core.CoreWindow",
    };

    public static bool IsShellWindow(string? className) => className is not null && ShellClasses.Contains(className);

    /// <summary>
    /// True while a full-screen window is in front. <paramref name="previous"/> is kept when one of our own windows is in
    /// front (the island itself, the selection overlay, the tray menu), so they never switch the mode on or off.
    /// </summary>
    public static bool IsFullscreen(ForegroundWindowInfo fg, ShellNotificationState state, bool previous)
    {
        if (fg.OwnProcess) return previous;
        // Exclusive Direct3D full screen and presentation mode are reported by Windows itself.
        if (state is ShellNotificationState.RunningD3DFullScreen or ShellNotificationState.PresentationMode) return true;
        if (fg.Handle == 0 || !fg.Visible || fg.Minimized || IsShellWindow(fg.ClassName)) return false;
        // The island sits on the primary monitor; a full-screen window elsewhere does not sit under it.
        if (!fg.OnPrimaryMonitor || fg.Monitor.IsEmpty) return false;
        // A maximised window with a title bar is an ordinary window, even when it covers the screen (auto-hidden taskbar).
        if (fg.Maximized && fg.HasCaption) return false;
        // QUNS_BUSY is the shell's own "full-screen app in front" verdict; the rectangle test catches borderless windows.
        return fg.Bounds.Contains(fg.Monitor) || state == ShellNotificationState.Busy;
    }
}
