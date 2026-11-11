using System;
using System.Collections.Generic;

namespace Island.Services;

/// <summary>Per-app banner muting; no Windows calls, so everything here is unit-tested.</summary>
public static class NotificationRules
{
    public static bool IsMuted(IEnumerable<string>? muted, string? app)
    {
        if (muted is null || string.IsNullOrWhiteSpace(app)) return false;
        var name = app.Trim();
        foreach (var m in muted)
        {
            if (string.Equals(m, name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Mutes the app if it is not muted yet, otherwise unmutes it. Returns true when the app is now muted.</summary>
    public static bool ToggleMuted(List<string> muted, string app)
    {
        if (string.IsNullOrWhiteSpace(app)) return false;
        if (IsMuted(muted, app))
        {
            var name = app.Trim();
            muted.RemoveAll(m => string.Equals(m, name, StringComparison.OrdinalIgnoreCase));
            return false;
        }

        muted.Add(app.Trim());
        return true;
    }
}
