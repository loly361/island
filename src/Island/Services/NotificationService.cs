using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Island.Services;

public sealed record InboxItem(uint Id, string App, string Title, string Body, DateTime Time);

/// <summary>
/// Mirrors Windows toast notifications into an in-memory inbox. Nothing is written to disk and
/// notifications are never dismissed from the Windows Action Center.
/// </summary>
public sealed class NotificationService
{
    private const int MaxItems = 50;
    private readonly HashSet<uint> _seen = new();

    public bool Available { get; private set; }

    /// <summary>Access status or the error that prevented access; shown when the inbox is unavailable.</summary>
    public string? Diagnostics { get; private set; }

    /// <summary>Newest first.</summary>
    public List<InboxItem> Items { get; } = new();

    public event Action<InboxItem>? Arrived;

    public event Action? Changed;

    public async Task InitAsync()
    {
        try
        {
            // The listener must be created and used on the same (thread-pool) apartment; on the UI thread it fails.
            var status = await Task.Run(() => UserNotificationListener.Current.RequestAccessAsync().AsTask());
            Diagnostics = status.ToString();
            Available = status == UserNotificationListenerAccessStatus.Allowed;
            if (Available) await PollAsync(initial: true);
        }
        catch (Exception ex)
        {
            Diagnostics = $"{ex.GetType().Name} 0x{ex.HResult:X8} @ {ex.StackTrace?.Split((char)10).FirstOrDefault()?.Trim()}";
            Available = false;
        }
    }

    public async Task PollAsync(bool initial = false)
    {
        if (!Available) return;

        IReadOnlyList<UserNotification> current;
        try
        {
            current = await Task.Run(() => UserNotificationListener.Current.GetNotificationsAsync(NotificationKinds.Toast).AsTask());
        }
        catch
        {
            return;
        }

        var changed = false;
        foreach (var n in current.OrderBy(n => n.CreationTime))
        {
            if (!_seen.Add(n.Id)) continue;
            var item = Convert(n);
            if (item is null) continue;

            Items.Insert(0, item);
            changed = true;
            if (!initial) Arrived?.Invoke(item);
        }

        if (!changed) return;
        if (Items.Count > MaxItems) Items.RemoveRange(MaxItems, Items.Count - MaxItems);
        Changed?.Invoke();
    }

    public void Clear()
    {
        Items.Clear();
        Changed?.Invoke();
    }

    private static InboxItem? Convert(UserNotification n)
    {
        try
        {
            var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
            var texts = binding?.GetTextElements()
                .Select(t => t.Text)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();
            if (texts is null || texts.Count == 0) return null;

            var app = n.AppInfo?.DisplayInfo?.DisplayName;
            return new InboxItem(
                n.Id,
                string.IsNullOrWhiteSpace(app) ? "Windows" : app,
                texts[0],
                string.Join(" ", texts.Skip(1)),
                n.CreationTime.LocalDateTime);
        }
        catch
        {
            return null;
        }
    }
}
