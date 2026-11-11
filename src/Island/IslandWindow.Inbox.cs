using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Island.Services;

namespace Island;

public sealed class InboxRow
{
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public string Meta { get; init; } = "";
    public string App { get; init; } = "";
}

/// <summary>Shows Windows notifications as a banner on the island and keeps a short in-memory inbox.</summary>
public partial class IslandWindow
{
    private static readonly Brush InboxIconBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x9E));
    private readonly NotificationService _inbox = new();
    private readonly DispatcherTimer _inboxTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    private void InitInbox()
    {
        _inboxTimer.Tick += async (_, _) => await _inbox.PollAsync();
        _inbox.Arrived += OnInboxArrived;
        _inbox.Changed += () =>
        {
            if (_state == State.Expanded && _tab == IxInbox) RenderInbox();
        };
    }

    private async Task StartInboxAsync()
    {
        if (_settings.NotificationsEnabled)
        {
            await _inbox.InitAsync();
            _inboxTimer.Start();
        }

        RenderInbox();
    }

    /// <summary>Called from the tray menu; persists the choice.</summary>
    public async Task SetInboxEnabledAsync(bool enabled)
    {
        _settings.NotificationsEnabled = enabled;
        JsonStore.Save(AppPaths.File("settings.json"), _settings);
        if (enabled)
        {
            await StartInboxAsync();
            return;
        }

        _inboxTimer.Stop();
        _inbox.Clear();
        RenderInbox();
    }

    private void OnInboxArrived(InboxItem item)
    {
        if (!_settings.NotificationsEnabled) return;
        if (_settings.DoNotDisturb || NotificationRules.IsMuted(_settings.MutedApps, item.App)) return;
        var tag = string.Equals(item.Title.Trim(), item.App.Trim(), StringComparison.OrdinalIgnoreCase) ? null : item.App;
        ShowAlert(item.Title, item.Body, 6000, "bell", tag, InboxIconBrush);
    }

    private void RenderInbox()
    {
        var now = DateTime.Now;
        var rows = _inbox.Items
            .Select(i => new InboxRow
            {
                Title = i.Title,
                Body = i.Body,
                App = i.App,
                Meta = NotificationRules.IsMuted(_settings.MutedApps, i.App)
                    ? $"{i.App} · {Loc.T("без баннеров")} · {TimeLabels.Ago(i.Time, now)}"
                    : $"{i.App} · {TimeLabels.Ago(i.Time, now)}",
            })
            .ToList();
        InboxList.ItemsSource = rows;
        InboxEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        InboxEmpty.Text = !_settings.NotificationsEnabled
            ? Loc.T("Показ уведомлений Windows выключен. Включить его можно в меню значка в трее.")
            : !_inbox.Available
                ? (Environment.ProcessPath?.Length > 200
                    ? Loc.T("Windows не даёт доступ к уведомлениям, когда программа лежит в папке с очень длинным путём. Перенесите её, например, в Загрузки.")
                    : Loc.T("Нет доступа к уведомлениям Windows. Разрешите его в Параметры → Конфиденциальность и защита → Уведомления.") + $"\n({_inbox.Diagnostics})")
                : Loc.T("Новых уведомлений нет");
    }

    private void InboxClear_Click(object sender, RoutedEventArgs e)
    {
        _inbox.Clear();
        RenderInbox();
    }

    private void InboxMute_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: InboxRow row } || string.IsNullOrWhiteSpace(row.App)) return;
        NotificationRules.ToggleMuted(_settings.MutedApps, row.App);
        JsonStore.Save(AppPaths.File("settings.json"), _settings);
        RenderInbox();
    }

    private void RenderNotificationSettings()
    {
        DndButton.Content = _settings.DoNotDisturb ? Loc.T("Вкл") : Loc.T("Выкл");
        StyleModeButton(DndButton, _settings.DoNotDisturb);
        var muted = _settings.MutedApps;
        MutedAppsText.Text = muted.Count == 0
            ? Loc.T("Программы без баннеров не выбраны. Выключить баннеры программы можно на вкладке уведомлений.")
            : Loc.F("Без баннеров: {0}", string.Join(", ", muted));
        MutedClearButton.Visibility = muted.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Dnd_Click(object sender, RoutedEventArgs e) => SetDoNotDisturb(!_settings.DoNotDisturb);

    public bool DoNotDisturb => _settings.DoNotDisturb;

    /// <summary>Also used by the tray menu; persists the choice.</summary>
    public void SetDoNotDisturb(bool on)
    {
        _settings.DoNotDisturb = on;
        JsonStore.Save(AppPaths.File("settings.json"), _settings);
        RenderNotificationSettings();
    }

    private void MutedClear_Click(object sender, RoutedEventArgs e)
    {
        _settings.MutedApps.Clear();
        JsonStore.Save(AppPaths.File("settings.json"), _settings);
        RenderNotificationSettings();
    }
}
