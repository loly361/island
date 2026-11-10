using System;
using System.Collections.Generic;
using System.Media;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Island.Services;

namespace Island;

/// <summary>A banner waiting to be shown: kept while the panel is open or a game is in front.</summary>
public sealed record AlertRequest(string Title, string Message, int Ms, string Glyph, string? Tag, Brush? Icon, SystemSound? Sound, Action? OnClick);

/// <summary>
/// Game mode: while a game or another full-screen window is in front, the island hides, does not open on hover and keeps
/// its banners (with their sounds) until the window closes or is minimised. A screen recording keeps running meanwhile.
/// </summary>
public partial class IslandWindow
{
    private const int MaxDeferredAlerts = 50;

    private readonly DispatcherTimer _gameTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly LinkedList<AlertRequest> _deferred = new();
    private bool _gameHidden, _gameFullscreen, _gameRestoreExpanded;
    private IntPtr _gameLastForeground, _gameFullscreenWindow, _gameDismissedFor;

    private void InitGameMode()
    {
        _gameTimer.Tick += (_, _) => CheckGameMode();
        Loaded += (_, _) =>
        {
            RenderGameModeButton();
            _gameTimer.Start();
        };
    }

    /// <summary>The hover timer reports the foreground window every 50 ms; a change is checked at once, not on the next second.</summary>
    private void OnForegroundSeen(IntPtr foreground)
    {
        if (foreground == _gameLastForeground) return;
        _gameLastForeground = foreground;
        CheckGameMode();
    }

    private void CheckGameMode()
    {
        var hide = false;
        if (_settings.GameMode)
        {
            var fg = ForegroundProbe.Read(_pid);
            _gameFullscreen = FullscreenDetector.IsFullscreen(fg, ForegroundProbe.NotificationState(), _gameFullscreen);
            if (!fg.OwnProcess)
            {
                _gameFullscreenWindow = _gameFullscreen ? fg.Handle : IntPtr.Zero;
                // "Open" from the tray shows the island over this one window until another window comes to the front.
                if (fg.Handle != _gameDismissedFor) _gameDismissedFor = IntPtr.Zero;
            }

            hide = _gameFullscreen && _gameDismissedFor == IntPtr.Zero;
        }
        else
        {
            _gameFullscreen = false;
        }

        ApplyGameMode(hide);
    }

    private void ApplyGameMode(bool hide)
    {
        // The screenshot and recording tools hide and show the window themselves; the next check catches up.
        if (hide == _gameHidden || _toolBusy) return;

        if (hide)
        {
            // A banner that is on screen right now is not lost: it comes back after the game.
            if (_state == State.Alert && _currentAlert is { } shown && DateTime.UtcNow < _alertUntil) _deferred.AddFirst(shown);
            if (_pending is { } waiting)
            {
                _deferred.AddLast(waiting);
                _pending = null;
            }

            _gameRestoreExpanded = _state == State.Expanded && _pinned;
            if (_state != State.Collapsed)
            {
                FlushNote();
                SetState(State.Collapsed);
            }

            _gameHidden = true;
            Hide();
            return;
        }

        _gameHidden = false;
        // ShowActivated is false on this window, so coming back never takes focus from the app in front.
        Show();
        PositionWindow();
        if (_gameRestoreExpanded && _pinned) SetState(State.Expanded);
        _gameRestoreExpanded = false;
    }

    /// <summary>The tray menu's "Open" and "Search" show the island even over a full-screen window.</summary>
    private void RevealOverFullscreen()
    {
        if (!_gameHidden) return;
        _gameDismissedFor = _gameFullscreenWindow;
        ApplyGameMode(false);
    }

    private void DeferAlert(AlertRequest alert)
    {
        _deferred.AddLast(alert);
        while (_deferred.Count > MaxDeferredAlerts) _deferred.RemoveFirst();
    }

    private void ShowNextDeferred()
    {
        if (_gameHidden || _deferred.First is not { } first) return;
        _deferred.RemoveFirst();
        ShowAlert(first.Value);
    }

    private void RenderGameModeButton()
    {
        GameModeButton.Content = _settings.GameMode ? Loc.T("Вкл") : Loc.T("Выкл");
        StyleModeButton(GameModeButton, _settings.GameMode);
    }

    private void GameMode_Click(object sender, RoutedEventArgs e)
    {
        _settings.GameMode = !_settings.GameMode;
        JsonStore.Save(AppPaths.File("settings.json"), _settings);
        RenderGameModeButton();
        CheckGameMode();
    }
}
