using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Island.Services;

namespace Island;

/// <summary>
/// The «ОБНОВЛЕНИЯ» card and the background check: 30 seconds after start, then once a day, only when
/// <see cref="Settings.AutoUpdateCheck"/> is on. A found update is announced once per version and installed
/// only when the user presses the update button.
/// </summary>
public partial class IslandWindow
{
    private static readonly TimeSpan FirstUpdateCheck = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(24);

    private readonly UpdateService _updater = new();
    private readonly DispatcherTimer _updateTimer = new() { Interval = FirstUpdateCheck };
    private readonly CancellationTokenSource _updateCts = new();
    private ReleaseInfo? _availableUpdate;
    private bool _updateBusy;
    private string? _updateStatus;

    private void InitUpdates()
    {
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = UpdateCheckInterval;
            if (_settings.AutoUpdateCheck) await CheckForUpdatesAsync(manual: false);
        };
        _updateTimer.Start();
        // Folders of installers that already ran; the installer cannot remove the folder it was started from.
        _ = Task.Run(() => UpdateService.CleanupOldDownloads(TimeSpan.FromDays(1)));
    }

    private void StopUpdates()
    {
        _updateTimer.Stop();
        if (_updateCts.IsCancellationRequested) return; // already stopped (Cancel on a disposed source would throw)
        _updateCts.Cancel();
        _updateCts.Dispose();
        _updater.Dispose();
    }

    private void RenderUpdateCard()
    {
        UpdateVersionText.Text = Loc.F("Версия {0}", UpdateService.CurrentVersion);
        UpdateAutoButton.Content = _settings.AutoUpdateCheck ? Loc.T("Вкл") : Loc.T("Выкл");
        StyleModeButton(UpdateAutoButton, _settings.AutoUpdateCheck);

        UpdateCheckButton.IsEnabled = !_updateBusy;
        UpdateInstallButton.IsEnabled = !_updateBusy;
        if (_availableUpdate is { } release)
        {
            UpdateInstallButton.Content = Loc.F("Обновить до {0}", release.Version);
            UpdateInstallButton.Visibility = Visibility.Visible;
            StyleModeButton(UpdateInstallButton, true);
        }
        else
        {
            UpdateInstallButton.Visibility = Visibility.Collapsed;
        }

        UpdateStatusText.Text = _updateStatus ?? "";
        UpdateStatusText.Visibility = string.IsNullOrEmpty(_updateStatus) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetUpdateStatus(string? status)
    {
        _updateStatus = status;
        RenderUpdateCard();
    }

    private void UpdateAuto_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoUpdateCheck = !_settings.AutoUpdateCheck;
        JsonStore.Save(AppPaths.File("settings.json"), _settings);
        if (_settings.AutoUpdateCheck)
        {
            // Turning it back on checks soon instead of waiting for the next daily tick.
            _updateTimer.Stop();
            _updateTimer.Interval = FirstUpdateCheck;
            _updateTimer.Start();
        }

        RenderUpdateCard();
    }

    private async void UpdateCheck_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(manual: true);

    /// <summary>The automatic check stays silent on failure; the manual one says it could not check.</summary>
    private async Task CheckForUpdatesAsync(bool manual)
    {
        // After StopUpdates the source is cancelled and disposed: its token must not be touched any more.
        if (_updateBusy || _updateCts.IsCancellationRequested) return;

        _updateBusy = true;
        // Disables "check now" and "update" for the duration of the check.
        if (manual) SetUpdateStatus(Loc.T("Проверка…"));
        else RenderUpdateCard();
        UpdateCheckResult result;
        try
        {
            result = await _updater.CheckAsync(_updateCts.Token);
        }
        finally
        {
            _updateBusy = false;
        }

        if (_updateCts.IsCancellationRequested) return;

        switch (result.Status)
        {
            case UpdateCheckStatus.Available when result.Release is { } release:
                _availableUpdate = release;
                SetUpdateStatus(Loc.F("Доступна версия {0}", release.Version));
                if (!manual) AnnounceUpdate(release);
                break;
            case UpdateCheckStatus.UpToDate:
                _availableUpdate = null;
                // Drops a stale «Доступна версия X» left from an earlier check.
                SetUpdateStatus(manual ? Loc.T("У вас последняя версия") : null);
                break;
            default:
                // Keep an update found earlier; only a manual check reports the failure.
                SetUpdateStatus(manual ? Loc.T("Не удалось проверить обновления") : _updateStatus);
                break;
        }
    }

    /// <summary>One pop-up per version, remembered across restarts.</summary>
    private void AnnounceUpdate(ReleaseInfo release)
    {
        var version = release.Version.ToString();
        if (string.Equals(_settings.DismissedUpdateVersion, version, StringComparison.Ordinal)) return;

        _settings.DismissedUpdateVersion = version;
        JsonStore.Save(AppPaths.File("settings.json"), _settings);
        ShowAlert(Loc.T("Доступна новая версия Island"), Loc.F("Версия {0}. Обновить можно в настройках.", version), 6000, "arrow.down.circle");
    }

    private async void UpdateInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_updateBusy || _updateCts.IsCancellationRequested || _availableUpdate is not { } release) return;

        _updateBusy = true;
        SetUpdateStatus(Loc.T("Загрузка…"));
        var lastPercent = -1;
        var downloading = true;
        var progress = new Progress<double>(fraction =>
        {
            var percent = (int)Math.Clamp(fraction * 100, 0, 100);
            if (percent == lastPercent || !downloading) return;
            lastPercent = percent;
            UpdateStatusText.Text = Loc.F("Загрузка… {0}%", percent);
        });

        DownloadedInstaller installer;
        try
        {
            installer = await _updater.DownloadAsync(release, progress, _updateCts.Token);
        }
        catch (UpdateIntegrityException)
        {
            downloading = false;
            _updateBusy = false;
            SetUpdateStatus(Loc.T("Файл обновления повреждён или подменён"));
            return;
        }
        catch (Exception ex)
        {
            downloading = false;
            _updateBusy = false;
            if (_updateCts.IsCancellationRequested) return;
            App.LogError(ex);
            SetUpdateStatus(Loc.T("Не удалось скачать обновление"));
            return;
        }

        downloading = false;
        SetUpdateStatus(Loc.T("Установка обновления…"));
        // Starting the process and hashing the file are quick, but keep them off the UI thread anyway.
        var started = await Task.Run(() => UpdateService.TryLaunchInstaller(installer));
        if (!started)
        {
            _updateBusy = false;
            SetUpdateStatus(Loc.T("Не удалось запустить установщик"));
            return;
        }

        // The installer closes a running Island itself; leaving now frees the files and the single-instance mutex.
        if (Application.Current is App app) app.Quit();
        else Application.Current?.Shutdown();
    }
}
