using System;
using System.Text;
using Island.Interop;

namespace Island.Services;

/// <summary>Reads the foreground window and the shell's notification state for <see cref="FullscreenDetector"/>.</summary>
public static class ForegroundProbe
{
    public static ForegroundWindowInfo Read(uint ownProcessId)
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return ForegroundWindowInfo.None;

        Native.GetWindowThreadProcessId(hwnd, out var pid);
        var name = new StringBuilder(128);
        Native.GetClassName(hwnd, name, name.Capacity);
        Native.GetWindowRect(hwnd, out var rect);

        var monitor = default(PixelRect);
        var primary = false;
        var handle = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (handle != IntPtr.Zero && Native.GetMonitorInfo(handle, ref info))
        {
            monitor = ToPixels(info.rcMonitor);
            primary = (info.dwFlags & Native.MONITORINFOF_PRIMARY) != 0;
        }

        var style = Native.GetWindowLong(hwnd, Native.GWL_STYLE);
        return new ForegroundWindowInfo(
            hwnd,
            name.ToString(),
            pid == ownProcessId,
            Native.IsWindowVisible(hwnd),
            Native.IsIconic(hwnd),
            Native.IsZoomed(hwnd),
            (style & Native.WS_CAPTION) == Native.WS_CAPTION,
            ToPixels(rect),
            monitor,
            primary);
    }

    public static ShellNotificationState NotificationState() =>
        Native.SHQueryUserNotificationState(out var state) == 0 && Enum.IsDefined(typeof(ShellNotificationState), state)
            ? (ShellNotificationState)state
            : ShellNotificationState.Unknown;

    private static PixelRect ToPixels(Native.RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
}
