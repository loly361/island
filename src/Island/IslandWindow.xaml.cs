using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Island.Interop;
using Island.Services;
using Drawing = System.Drawing;

namespace Island;

public sealed class ClipRow
{
    public required ClipEntry Entry { get; init; }
    public string Preview { get; init; } = "";
    public string Meta { get; init; } = "";
    public string PinGlyph { get; init; } = "pin";
    public Brush PinBrush { get; init; } = Brushes.Gray;
    public bool Pinned { get; init; }
}

public sealed class SearchRow
{
    public required SearchResult Result { get; init; }
    public string Glyph { get; init; } = "";
    public string Title => Result.Title;
    public string Subtitle => Result.Subtitle;
}

public sealed class NoteRow
{
    public required Note Note { get; init; }
    public string Title => string.IsNullOrWhiteSpace(Note.Title) ? Loc.T("Новая заметка") : Note.Title;
}

public partial class IslandWindow : Window
{
    private enum State { Collapsed, Expanded, Alert }

    private const double CollapsedW = 190, CollapsedH = 34;
    private const double ExpandedW = 600;
    private const double PillClipOverhang = 40;
    private const double MinimalSize = 18, MinimalTop = 6;
    private const double AlertW = 390, AlertH = 62;
    private const int HotkeyId = 0x1510;
    private const int HotkeyShotId = 0x1511, HotkeyRecordId = 0x1512;
    private static readonly TimeSpan TypingGrace = TimeSpan.FromSeconds(12);
    private const int IxHome = 0, IxClip = 1, IxInbox = 2, IxNotes = 3, IxWeather = 4, IxControl = 5, IxSearch = 6, IxSettings = 7;
    private static readonly double[] TabHeights = { 395, 375, 375, 395, 395, 275, 395, 395 };

    private readonly Settings _settings;
    private readonly ClipboardHistory _clips;
    private readonly NotesStore _notes;
    private readonly MediaService _media;
    private readonly VolumeService _volume = new();
    private readonly WeatherService _weather;
    private readonly SearchService _search = new();
    private readonly NotifyServer _notify;
    private readonly uint _pid = (uint)Environment.ProcessId;
    private readonly Dictionary<UIElement, int> _fadeTokens = new();

    private readonly DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _weatherTimer = new() { Interval = TimeSpan.FromMinutes(20) };
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _noteTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };

    private State _state = State.Collapsed;
    private int _tab;
    private bool _pinned, _toolBusy, _overlayOpen, _settingTab, _syncingVolume, _loadingNote, _refreshingNotes;
    private double _curW = CollapsedW, _curH = CollapsedH, _scale = 1;
    private int _winX, _winY, _winW;
    private IntPtr _hwnd, _lastExternal;
    private HwndSource? _source;
    private DateTime? _hoverSince;
    private bool _hoveredSinceExpand;
    private DateTime _alertUntil = DateTime.MinValue;
    private DateTime _suppressUntil = DateTime.MinValue;
    private DateTime _lastKeyUtc = DateTime.MinValue;
    private AlertRequest? _currentAlert;
    private Action? _alertAction;
    private bool _timerChipShown, _coverShown, _recChipShown;

    /// <summary>The collapsed pill grows to fit the timer ring, the album cover and the recording time when they are shown.</summary>
    private double CollapsedNow => CollapsedW + (_timerChipShown ? 80 : 0) + (_coverShown ? 26 : 0) + (_recChipShown ? 68 : 0);

    private bool Minimal => string.Equals(_settings.Mode, Settings.ModeMinimal, StringComparison.OrdinalIgnoreCase);

    /// <summary>Size, corner radius and top offset of the resting shape: the full pill, or a small circle in minimal mode.</summary>
    private (double W, double H, double Radius, double Top) CollapsedShape() =>
        Minimal ? (MinimalSize, MinimalSize, MinimalSize / 2, MinimalTop) : (CollapsedNow, CollapsedH, 18, 0);
    private Note? _currentNote;
    private CancellationTokenSource? _searchCts;
    private string _hotkeyLabel = "Alt+Space";

    public IslandWindow(Settings settings, ClipboardHistory clips, NotesStore notes)
    {
        _settings = settings;
        _clips = clips;
        _notes = notes;
        _weather = new WeatherService(settings);
        _notify = new NotifyServer(settings.NotifyToken);
        WatchSettingsFile();
        InitializeComponent();
        _media = new MediaService(Dispatcher);

        BuildShortcuts();
        TimeText.Text = DateTime.Now.ToString("HH:mm");
        OnMediaChanged(new MediaSnapshot());
        RenderVolume();
        if (Minimal) ShowMinimalShape();

        _media.Changed += OnMediaChanged;
        _hoverTimer.Tick += OnHoverTick;
        _clockTimer.Tick += (_, _) => TimeText.Text = DateTime.Now.ToString("HH:mm");
        _weatherTimer.Tick += async (_, _) => await UpdateWeatherAsync();
        _uiTimer.Tick += OnUiTick;
        InitTimerAndReminders();
        InitInbox();
        InitAccent();
        InitUpdates();
        InitGameMode();
        InitRecording();
        _noteTimer.Tick += (_, _) => { _noteTimer.Stop(); SaveCurrentNote(); };
        _searchTimer.Tick += async (_, _) => { _searchTimer.Stop(); await RunSearchAsync(SearchBox.Text); };

        Loaded += async (_, _) =>
        {
            await RefreshAccentAsync(animate: false);
            _hoverTimer.Start();
            _clockTimer.Start();
            _weatherTimer.Start();
            _uiTimer.Start();
            await _media.InitAsync();
            _ = _search.IndexAppsAsync();
            StartNotifyServer();
            await StartInboxAsync();
            await UpdateWeatherAsync();
        };
        Closed += (_, _) => Cleanup();
    }

    // ---------- window plumbing ----------

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        var ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TOOLWINDOW);
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
        Native.AddClipboardFormatListener(_hwnd);
        RegisterHotkey();
        PositionWindow();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        PositionWindow();
    }

    private void PositionWindow()
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new Drawing.Rectangle(0, 0, 1920, 1080);
        _scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        _winW = (int)Math.Round(Width * _scale);
        var h = (int)Math.Round(Height * _scale);
        _winX = bounds.X + (bounds.Width - _winW) / 2;
        _winY = bounds.Y;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, _winX, _winY, _winW, h, Native.SWP_NOACTIVATE);
    }

    private void RegisterHotkey()
    {
        if (Native.RegisterHotKey(_hwnd, HotkeyId, Native.MOD_ALT | Native.MOD_NOREPEAT, Native.VK_SPACE))
        {
            _hotkeyLabel = "Alt+Space";
        }
        else if (Native.RegisterHotKey(_hwnd, HotkeyId, Native.MOD_ALT | Native.MOD_CONTROL | Native.MOD_NOREPEAT, Native.VK_SPACE))
        {
            _hotkeyLabel = "Ctrl+Alt+Space";
        }
        else
        {
            _hotkeyLabel = "";
        }

        HotkeyHint.Text = _hotkeyLabel.Length == 0 ? "" : Loc.F("{0} — поиск", _hotkeyLabel);
        Native.RegisterHotKey(_hwnd, HotkeyShotId, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, Native.VK_S);
        Native.RegisterHotKey(_hwnd, HotkeyRecordId, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, Native.VK_R);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Native.WM_HOTKEY when wParam.ToInt32() == HotkeyId:
                HotkeyPressed();
                handled = true;
                break;
            case Native.WM_HOTKEY when wParam.ToInt32() == HotkeyShotId:
                if (!_toolBusy) _ = RunScreenToolAsync("area");
                handled = true;
                break;
            case Native.WM_HOTKEY when wParam.ToInt32() == HotkeyRecordId:
                if (IsRecording) StopRecording();
                else if (!_toolBusy) _ = StartRecordingAsync(area: false);
                handled = true;
                break;
            case Native.WM_CLIPBOARDUPDATE:
                OnClipboardChanged();
                break;
            case Native.WM_DISPLAYCHANGE:
                Dispatcher.BeginInvoke(new Action(PositionWindow));
                OnDisplayChangedWhileRecording();
                break;
            case WM_SETTINGCHANGE:
                ScheduleAccentRefresh();
                break;
        }

        return IntPtr.Zero;
    }

    private void Cleanup()
    {
        _hoverTimer.Stop();
        _clockTimer.Stop();
        _weatherTimer.Stop();
        _uiTimer.Stop();
        FlushNote();
        _noteTimer.Stop();
        _searchTimer.Stop();
        _inboxTimer.Stop();
        _timerTick.Stop();
        _reminderTick.Stop();
        _recTick.Stop();
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        Native.UnregisterHotKey(_hwnd, HotkeyId);
        Native.UnregisterHotKey(_hwnd, HotkeyShotId);
        Native.UnregisterHotKey(_hwnd, HotkeyRecordId);
        Native.RemoveClipboardFormatListener(_hwnd);
        _notify.Dispose();
        _reloadTimer.Stop();
        _settingsWatcher?.Dispose();
        _volume.Dispose();
        StopUpdates();
        _gameTimer.Stop();
        FinishRecordingNow();
    }

    // ---------- state machine and animation ----------

    private void SetState(State next)
    {
        var prev = _state;
        _state = next;
        if (next != State.Alert)
        {
            _alertAction = null;
            _currentAlert = null;
        }

        if (prev != State.Expanded) _hoveredSinceExpand = false;
        _hoverSince = null;

        switch (next)
        {
            case State.Collapsed:
            {
                var shape = CollapsedShape();
                var delay = prev == State.Expanded ? 140 : 40;
                AnimateTo(shape.W, shape.H, shape.Radius, top: shape.Top);
                Fade(AlertView, false);
                Fade(ExpandedView, false);
                Fade(CollapsedView, !Minimal, delay);
                Fade(MinimalDot, Minimal, delay);
                break;
            }
            case State.Alert:
                AnimateTo(AlertW, AlertH, 30, spring: true);
                Fade(CollapsedView, false);
                Fade(MinimalDot, false);
                Fade(ExpandedView, false);
                Fade(AlertView, true, 90);
                break;
            case State.Expanded:
                ExpandedView.Height = TabHeights[_tab];
                AnimateTo(ExpandedW, TabHeights[_tab], 34, spring: true);
                Fade(CollapsedView, false);
                Fade(MinimalDot, false);
                Fade(AlertView, false);
                Fade(ExpandedView, true, 40);
                if (prev != State.Expanded) PopExpanded();
                break;
        }

        ExpandedView.IsHitTestVisible = next == State.Expanded;
    }

    private void RefreshCollapsedSize()
    {
        if (_state != State.Collapsed) return;
        var shape = CollapsedShape();
        if (Math.Abs(_curW - shape.W) > 0.5) AnimateTo(shape.W, shape.H, shape.Radius, top: shape.Top);
    }

    /// <summary>Re-applies the display mode after a settings change; an open panel switches once it closes.</summary>
    private void ApplyMode()
    {
        if (_state == State.Collapsed) SetState(State.Collapsed);
    }

    /// <summary>Starts in the circle directly, so the window does not animate down from the standard pill.</summary>
    private void ShowMinimalShape()
    {
        var shape = CollapsedShape();
        _curW = shape.W;
        _curH = shape.Top + shape.H;
        PillClip.Rect = ClipRect(shape.W, shape.H, shape.Top);
        PillClip.RadiusX = PillClip.RadiusY = shape.Radius;
        CollapsedView.Visibility = Visibility.Collapsed;
        CollapsedView.Opacity = 0;
        MinimalDot.Visibility = Visibility.Visible;
        MinimalDot.Opacity = 1;
    }

    /// <summary>
    /// A shape hanging from the top edge starts above the window, so its rounded top corners are never visible and only
    /// the bottom corners show; a floating circle (top &gt; 0) keeps all four.
    /// </summary>
    private static Rect ClipRect(double w, double h, double top)
    {
        var overhang = top > 0 ? 0 : PillClipOverhang;
        return new Rect((ExpandedW - w) / 2, top - overhang, w, h + overhang);
    }

    /// <summary>Resizes the pill; a spring adds a small overshoot like iOS, a plain ease settles without one.</summary>
    private void AnimateTo(double w, double h, double radius, bool spring = false, double top = 0)
    {
        _curW = w;
        _curH = top + h;
        // Opening decelerates smoothly with no overshoot: a bouncing clip edge reads as a stutter.
        var duration = TimeSpan.FromMilliseconds(spring ? 460 : 260);
        IEasingFunction ease = spring
            ? new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 }
            : new CubicEase { EasingMode = EasingMode.EaseOut };

        var target = ClipRect(w, h, top);
        PillClip.BeginAnimation(RectangleGeometry.RectProperty, new RectAnimation(target, duration) { EasingFunction = ease });
        PillClip.BeginAnimation(RectangleGeometry.RadiusXProperty, new DoubleAnimation(radius, duration) { EasingFunction = ease });
        PillClip.BeginAnimation(RectangleGeometry.RadiusYProperty, new DoubleAnimation(radius, duration) { EasingFunction = ease });
    }

    private void PopExpanded()
    {
        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 };
        foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            ExpandedScale.BeginAnimation(property, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(460))
            {
                EasingFunction = ease,
            });
        }
    }

    /// <summary>Slides the active tab's content up a few pixels while it fades in.</summary>
    private static void RevealView(UIElement view)
    {
        if (view.RenderTransform is not TranslateTransform { IsFrozen: false } shift)
        {
            shift = new TranslateTransform();
            view.RenderTransform = shift;
        }

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) =>
        {
            view.BeginAnimation(OpacityProperty, null);
            view.Opacity = 1;
        };
        view.BeginAnimation(OpacityProperty, fade);
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private void MoveTabIndicator(int tab, bool animate)
    {
        var x = tab * 40.0;
        if (!animate)
        {
            TabIndicatorShift.BeginAnimation(TranslateTransform.XProperty, null);
            TabIndicatorShift.X = x;
            return;
        }

        TabIndicatorShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(360))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.55 },
        });
    }

    private void Fade(UIElement element, bool show, int delayMs = 0)
    {
        var token = _fadeTokens.GetValueOrDefault(element) + 1;
        _fadeTokens[element] = token;

        var from = element.Opacity;
        element.BeginAnimation(OpacityProperty, null);
        element.Opacity = from;
        if (show) element.Visibility = Visibility.Visible;

        var animation = new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(show ? 180 : 110))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
        };
        animation.Completed += (_, _) =>
        {
            if (_fadeTokens.GetValueOrDefault(element) != token) return;
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = show ? 1 : 0;
            if (!show) element.Visibility = Visibility.Collapsed;
        };
        element.BeginAnimation(OpacityProperty, animation);
    }

    private void Expand(int tab)
    {
        SetTab(tab);
        if (_state != State.Expanded) SetState(State.Expanded);
    }

    private void Collapse()
    {
        if (_state == State.Collapsed) return;
        var wasActive = IsActive && _state == State.Expanded;
        FlushNote();
        RecordChoice.Visibility = Visibility.Collapsed;
        ToolTiles.Visibility = Visibility.Visible;
        SetState(State.Collapsed);
        if (wasActive && _lastExternal != IntPtr.Zero) Native.SetForegroundWindow(_lastExternal);

    }

    /// <summary>
    /// Shows a banner on the island. <paramref name="sound"/> plays with it; <paramref name="onClick"/> makes the banner
    /// clickable (hovering it then keeps it instead of opening the panel). In game mode the banner waits for the game to close.
    /// </summary>
    public void ShowAlert(string title, string message, int ms = 2400, string glyph = "checkmark", string? tag = null,
        Brush? iconBrush = null, System.Media.SystemSound? sound = null, Action? onClick = null) =>
        ShowAlert(new AlertRequest(title, message, ms, glyph, tag, iconBrush, sound, onClick));

    private void ShowAlert(AlertRequest alert)
    {
        if (_gameHidden)
        {
            DeferAlert(alert);
            return;
        }

        if (_state == State.Expanded)
        {
            DeferAlert(alert);
            return;
        }

        alert.Sound?.Play();
        alert = alert with { Sound = null };

        AlertTitle.Inlines.Clear();
        AlertTitle.Inlines.Add(new System.Windows.Documents.Run(alert.Title));
        if (!string.IsNullOrWhiteSpace(alert.Tag))
        {
            AlertTitle.Inlines.Add(new System.Windows.Documents.Run("  " + alert.Tag)
            {
                Foreground = (Brush)FindResource("SubBrush"),
                FontWeight = FontWeights.Normal,
                FontSize = 11,
            });
        }

        AlertMessage.Text = alert.Message;
        AlertMessage.Visibility = string.IsNullOrEmpty(alert.Message) ? Visibility.Collapsed : Visibility.Visible;
        AlertGlyph.Kind = alert.Glyph;
        AlertIcon.Background = alert.Icon ?? (Brush)FindResource("AccentBrush");
        _alertUntil = DateTime.UtcNow.AddMilliseconds(alert.Ms);
        _currentAlert = alert;
        _alertAction = alert.OnClick;
        AlertView.Cursor = alert.OnClick is null ? null : Cursors.Hand;
        SetState(State.Alert);
    }

    private void Alert_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_state != State.Alert || _alertAction is not { } action) return;
        _alertAction = null;
        _currentAlert = null;
        SetState(State.Collapsed);
        action();
    }

    // ---------- hover tracking ----------

    private void OnHoverTick(object? sender, EventArgs e)
    {
        OnForegroundSeen(TrackForeground());
        if (_toolBusy || !IsVisible || !Native.GetCursorPos(out var p)) return;

        var now = DateTime.UtcNow;
        var widthPx = _curW * _scale;
        var left = _winX + (_winW - widthPx) / 2;
        var right = left + widthPx;
        var bottom = _winY + _curH * _scale;
        // The small circle is hard to hit, so its hover area is padded more generously than the pill's.
        var circle = Minimal && _state == State.Collapsed;
        var pad = circle ? 14 : 6;
        var inside = p.X >= left - pad && p.X <= right + pad && p.Y >= _winY - 2 && p.Y <= bottom + 8;
        var centerX = _winX + _winW / 2.0;
        var trigger = p.Y <= _winY + 1 && Math.Abs(p.X - centerX) <= ((circle ? MinimalSize : CollapsedNow) / 2 + 30) * _scale;

        if (_state != State.Expanded)
        {
            // A clickable banner stays while the cursor is on it, so it can be clicked instead of opening the panel.
            if (_state == State.Alert && _alertAction is not null && inside)
            {
                _hoverSince = null;
                if (_alertUntil < now.AddSeconds(1.5)) _alertUntil = now.AddSeconds(1.5);
                return;
            }

            if (inside || trigger)
            {
                _hoverSince ??= now;
                if (now - _hoverSince >= TimeSpan.FromMilliseconds(110)) Expand(0);
            }
            else
            {
                _hoverSince = null;
            }

            if (_state == State.Alert && now > _alertUntil)
            {
                _alertAction = null;
                _currentAlert = null;
                SetState(State.Collapsed);
            }

            // Banners held back during a game come out one after another.
            if (_state == State.Collapsed && _hoverSince is null && _deferred.Count > 0) ShowNextDeferred();
            return;
        }

        if (inside)
        {
            _hoveredSinceExpand = true;
            return;
        }

        // Leaving with the cursor closes the panel on the next tick; typing only holds it open when it was opened from the keyboard.
        if (!KeepOpen()) Collapse();
    }

    private bool KeepOpen() =>
        _pinned
        || _overlayOpen
        || Mouse.LeftButton == MouseButtonState.Pressed
        || (!_hoveredSinceExpand && IsActive && Keyboard.FocusedElement is TextBoxBase && DateTime.UtcNow - _lastKeyUtc < TypingGrace);

    private IntPtr TrackForeground()
    {
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == _hwnd) return fg;
        Native.GetWindowThreadProcessId(fg, out var pid);
        if (pid != _pid) _lastExternal = fg;
        return fg;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        _lastKeyUtc = DateTime.UtcNow;
        if (e.Key == Key.Escape && _state == State.Expanded)
        {
            Collapse();
            e.Handled = true;
        }
    }

    // ---------- tabs ----------

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || _settingTab || sender is not RadioButton { Tag: string tag }) return;
        SetTab(int.Parse(tag));
    }

    private void SetTab(int tab)
    {
        if (tab != IxNotes) FlushNote();
        var previous = _tab;
        _tab = tab;

        var radios = new[] { TabHome, TabClip, TabInbox, TabNotes, TabWeather, TabControl, TabSearch, TabSettings };
        var views = new UIElement[] { HomeView, ClipView, InboxView, NotesView, WeatherView, ControlView, SearchView, SettingsView };
        _settingTab = true;
        for (var i = 0; i < radios.Length; i++)
        {
            radios[i].IsChecked = i == tab;
            views[i].Visibility = i == tab ? Visibility.Visible : Visibility.Collapsed;
        }

        _settingTab = false;

        var visible = _state == State.Expanded;
        MoveTabIndicator(tab, visible && previous != tab);
        // Laid out once per tab switch, not per animation frame.
        ExpandedView.Height = TabHeights[tab];
        if (visible)
        {
            AnimateTo(ExpandedW, TabHeights[tab], 34, spring: true);
            if (previous != tab) RevealView(views[tab]);
        }

        switch (tab)
        {
            case IxHome:
                _volume.Refresh();
                RenderVolume();
                break;
            case IxClip:
                RefreshClips();
                break;
            case IxInbox:
                RenderInbox();
                break;
            case IxNotes:
                RefreshNotes();
                RenderTimer();
                RenderReminders();
                break;
            case IxWeather:
                RenderWeather();
                break;
            case IxControl:
                _ = RefreshControlAsync();
                break;
            case IxSearch:
                SearchBox.Text = "";
                SearchList.ItemsSource = null;
                FocusLater(SearchBox);
                break;
            case IxSettings:
                RenderSettings();
                break;
        }
    }

    private void FocusLater(UIElement target)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            Activate();
            Native.SetForegroundWindow(_hwnd);
            target.Focus();
            Keyboard.Focus(target);
        }), DispatcherPriority.Input);
    }

    public void OpenHome()
    {
        RevealOverFullscreen();
        Expand(0);
    }

    public void OpenSearch()
    {
        RevealOverFullscreen();
        HotkeyPressed();
    }

    private void HotkeyPressed()
    {
        // In a game Alt+Space belongs to the game; the tray menu still opens the island on purpose.
        if (_toolBusy || _gameHidden) return;
        _lastKeyUtc = DateTime.UtcNow;
        if (_state == State.Expanded && _tab == IxSearch && IsActive)
        {
            Collapse();
            return;
        }

        Expand(IxSearch);
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        _pinned = !_pinned;
        PinButton.Content = _pinned ? "pin.fill" : "pin";
        PinButton.Foreground = _pinned ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("SubBrush");
    }

    // ---------- clock, weather ----------

    // ---------- media and volume ----------

    private void OnMediaChanged(MediaSnapshot s)
    {
        MediaTitle.Text = !s.HasSession ? Loc.T("Ничего не играет") : string.IsNullOrWhiteSpace(s.Title) ? Loc.T("Без названия") : s.Title;
        MediaArtist.Text = s.Artist;
        if (s.Art is null)
        {
            ArtBorder.Background = (Brush)FindResource("Surface2");
            ArtFallback.Visibility = Visibility.Visible;
        }
        else
        {
            ArtBorder.Background = new ImageBrush(s.Art) { Stretch = Stretch.UniformToFill };
            ArtFallback.Visibility = Visibility.Collapsed;
        }

        PlayButton.Content = s.IsPlaying ? "pause.fill" : "play.fill";
        PrevButton.IsEnabled = s.HasSession && s.CanPrev;
        NextButton.IsEnabled = s.HasSession && s.CanNext;
        PlayButton.IsEnabled = s.HasSession;
        // A playing track shows its cover on the pill; without a cover, the plain dot stays.
        var art = s.IsPlaying ? s.Art : null;
        CoverChip.Background = art is null ? null : new ImageBrush(art) { Stretch = Stretch.UniformToFill };
        CoverChip.Visibility = art is null ? Visibility.Collapsed : Visibility.Visible;
        PlayingDot.Visibility = s.IsPlaying && art is null ? Visibility.Visible : Visibility.Collapsed;
        if ((art is not null) != _coverShown)
        {
            _coverShown = art is not null;
            RefreshCollapsedSize();
        }

        UpdateProgress();
    }

    private async void Prev_Click(object sender, RoutedEventArgs e) => await _media.PreviousAsync();

    private async void Play_Click(object sender, RoutedEventArgs e) => await _media.TogglePlayPauseAsync();

    private async void Next_Click(object sender, RoutedEventArgs e) => await _media.NextAsync();

    private void OnUiTick(object? sender, EventArgs e)
    {
        if (_state != State.Expanded || _tab != 0) return;
        UpdateProgress();
        if (!VolSlider.IsMouseOver || Mouse.LeftButton != MouseButtonState.Pressed) RenderVolume();
    }

    private void UpdateProgress()
    {
        var (position, duration) = _media.GetTimeline();
        if (duration <= TimeSpan.Zero)
        {
            ProgressFill.Width = 0;
            PosText.Text = "";
            DurText.Text = "";
            return;
        }

        var fraction = Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1);
        ProgressFill.Width = ProgressTrack.ActualWidth * fraction;
        PosText.Text = TimeLabels.Clock(position);
        DurText.Text = TimeLabels.Clock(duration);
    }

    private void RenderVolume()
    {
        _syncingVolume = true;
        var level = _volume.Level;
        VolSlider.Value = level;
        VolText.Text = level.ToString();
        VolGlyph.Kind = level == 0 ? "speaker.xmark" : "speaker.wave.2";
        VolDevice.Text = _volume.DeviceName;
        VolSlider.IsEnabled = _volume.Available;
        _syncingVolume = false;
    }

    private void VolSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingVolume) return;
        var level = (int)Math.Round(e.NewValue);
        _volume.Level = level;
        VolText.Text = level.ToString();
        VolGlyph.Kind = level == 0 ? "speaker.xmark" : "speaker.wave.2";
    }

    private void BuildShortcuts()
    {
        foreach (var shortcut in _settings.Shortcuts)
        {
            var target = shortcut.Target;
            var button = new Button
            {
                Style = (Style)FindResource("IconBtn"),
                Content = shortcut.Glyph,
                ToolTip = Loc.T(shortcut.Name),
                Width = 42,
                Height = 34,
                Margin = new Thickness(0, 0, 2, 0),
            };
            button.Click += (_, _) =>
            {
                SystemUtil.Launch(target);
                Collapse();
            };
            ShortcutsPanel.Children.Add(button);
        }
    }

    // ---------- quick actions ----------

    private async void Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;
        try
        {
            switch (tag)
            {
                case "plain":
                    PlainText();
                    break;
                case "folder":
                    SystemUtil.Launch(ScreenTools.ScreenshotsFolder());
                    Collapse();
                    break;
                case "videos":
                    Directory.CreateDirectory(RecordingRules.Folder());
                    SystemUtil.Launch(RecordingRules.Folder());
                    Collapse();
                    break;
                default:
                    await RunScreenToolAsync(tag);
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowAlert(Loc.T("Не получилось"), ex.Message.Length > 80 ? ex.Message[..80] : ex.Message, glyph: "exclamationmark.triangle");
        }
    }

    private void PlainText()
    {
        var text = ClipboardUtil.GetText();
        Collapse();
        if (string.IsNullOrEmpty(text))
        {
            ShowAlert(Loc.T("В буфере нет текста"), "", glyph: "exclamationmark.triangle");
            return;
        }

        SilentSetClipboard(text);
        ShowAlert(Loc.T("Форматирование убрано"), Loc.T("Текст в буфере теперь без оформления"));
    }

    private async Task RunScreenToolAsync(string tool)
    {
        (string Title, string Message, bool Error)? result = null;
        _toolBusy = true;
        try
        {
            Collapse();
            Hide();
            await Task.Delay(340);

            using var shot = ScreenTools.CaptureScreen(out var bounds);
            switch (tool)
            {
                case "full":
                {
                    ClipboardUtil.SetImage(ScreenTools.ToBitmapSource(shot));
                    var path = ScreenTools.SavePng(shot);
                    result = (Loc.T("Снимок экрана скопирован"), Path.GetFileName(path), false);
                    break;
                }
                case "area":
                {
                    if (PickRegion(shot, bounds) is { } area)
                    {
                        using var crop = ScreenTools.Crop(shot, area);
                        ClipboardUtil.SetImage(ScreenTools.ToBitmapSource(crop));
                        var path = ScreenTools.SavePng(crop);
                        result = (Loc.T("Скриншот скопирован"), Path.GetFileName(path), false);
                    }

                    break;
                }
                case "ocr":
                {
                    if (PickRegion(shot, bounds) is { } area)
                    {
                        using var crop = ScreenTools.Crop(shot, area);
                        var text = (await ScreenTools.OcrAsync(crop))?.Trim();
                        if (string.IsNullOrEmpty(text))
                        {
                            result = (Loc.T("Текст не найден"), Loc.T("Выделите область покрупнее"), true);
                        }
                        else
                        {
                            SilentSetClipboard(text);
                            _clips.Add(text, "Текст с экрана");
                            result = (Loc.T("Текст скопирован"), text.Replace('\n', ' ').Length > 60 ? text.Replace('\n', ' ')[..60] + "…" : text.Replace('\n', ' '), false);
                        }
                    }

                    break;
                }
                case "picker":
                {
                    _overlayOpen = true;
                    var overlay = new ScreenOverlayWindow(shot, bounds, OverlayMode.Picker);
                    overlay.ShowDialog();
                    _overlayOpen = false;
                    if (overlay.PickedHex is { } hex)
                    {
                        SilentSetClipboard(hex);
                        _clips.Add(hex, "Пипетка");
                        result = (Loc.T("Цвет скопирован"), hex, false);
                    }

                    break;
                }
            }
        }
        catch (Exception ex)
        {
            result = (Loc.T("Не получилось"), ex.Message.Length > 80 ? ex.Message[..80] : ex.Message, true);
        }
        finally
        {
            _overlayOpen = false;
            _toolBusy = false;
            // In game mode the island stays hidden; the next game-mode check shows it when the game closes.
            if (!_gameHidden)
            {
                Show();
                PositionWindow();
            }
        }

        if (result is { } r) ShowAlert(r.Title, r.Message, glyph: r.Error ? "exclamationmark.triangle" : "checkmark");
    }

    private Drawing.Rectangle? PickRegion(Drawing.Bitmap shot, Drawing.Rectangle bounds)
    {
        _overlayOpen = true;
        var overlay = new ScreenOverlayWindow(shot, bounds, OverlayMode.Region);
        overlay.ShowDialog();
        _overlayOpen = false;
        return overlay.Selection;
    }

    // ---------- clipboard history ----------

    private void SilentSetClipboard(string text)
    {
        _suppressUntil = DateTime.UtcNow.AddMilliseconds(700);
        ClipboardUtil.SetText(text);
    }

    private async void OnClipboardChanged()
    {
        try
        {
            await Task.Delay(90);
            if (DateTime.UtcNow < _suppressUntil) return;
            if (ClipboardUtil.IsExcludedFromHistory()) return;
            var text = ClipboardUtil.GetText();
            if (string.IsNullOrWhiteSpace(text)) return;

            var owner = Native.ProcessNameOf(Native.GetClipboardOwner()) ?? Native.ProcessNameOf(_lastExternal);
            _clips.Add(text, SystemUtil.FriendlyProcessName(owner));
            if (_state == State.Expanded && _tab == 1) RefreshClips();
        }
        catch
        {
            // A failed clipboard read is not worth surfacing.
        }
    }

    private void RefreshClips()
    {
        var now = DateTime.Now;
        var rows = _clips.Filter(ClipSearch.Text).Select(e => new ClipRow
        {
            Entry = e,
            Preview = Preview(e.Text),
            Meta = $"{Loc.T(e.Source)} · {TimeLabels.Ago(e.At, now)}",
            Pinned = e.Pinned,
            PinGlyph = e.Pinned ? "pin.fill" : "pin",
            PinBrush = e.Pinned ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("SubBrush"),
        }).ToList();

        ClipList.ItemsSource = rows;
        ClipEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClipEmpty.Text = string.IsNullOrWhiteSpace(ClipSearch.Text) ? Loc.T("Здесь появится всё, что вы копируете") : Loc.T("Ничего не найдено");
    }

    private static string Preview(string text)
    {
        var line = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return line.Length > 110 ? line[..110] + "…" : line;
    }

    private void ClipSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) RefreshClips();
    }

    private void ClipClear_Click(object sender, RoutedEventArgs e)
    {
        _clips.ClearUnpinned();
        RefreshClips();
    }

    private void ClipRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ClipRow row }) return;
        SilentSetClipboard(row.Entry.Text);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            Collapse();
            ShowAlert(Loc.T("Скопировано"), Preview(row.Entry.Text));
            return;
        }

        _ = PasteIntoPreviousWindowAsync();
    }

    private async Task PasteIntoPreviousWindowAsync()
    {
        var target = _lastExternal;
        Collapse();
        await Task.Delay(180);
        if (target != IntPtr.Zero) Native.SetForegroundWindow(target);
        await Task.Delay(120);
        Native.SendCtrlV();
    }

    private void ClipPin_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: ClipRow row }) return;
        _clips.TogglePin(row.Entry);
        RefreshClips();
    }

    private void ClipDelete_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: ClipRow row }) return;
        _clips.Remove(row.Entry);
        RefreshClips();
    }

    // ---------- notes ----------

    private void RefreshNotes(Note? select = null)
    {
        _refreshingNotes = true;
        var items = _notes.Items;
        var rows = items.Select(n => new NoteRow { Note = n }).ToList();
        NotesList.ItemsSource = rows;
        var target = select ?? _currentNote ?? items.FirstOrDefault();
        NotesList.SelectedItem = rows.FirstOrDefault(r => r.Note == target);
        _refreshingNotes = false;

        var has = rows.Count > 0;
        NoteEditor.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        NotesEmpty.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        if (!has) _currentNote = null;
        else if (NotesList.SelectedItem is NoteRow selected && selected.Note != _currentNote) LoadNote(selected.Note);
        UpdateNotePlaceholder();
    }

    private void LoadNote(Note note)
    {
        _loadingNote = true;
        _currentNote = note;
        var document = new FlowDocument();
        NoteEditor.Document = document;
        if (!string.IsNullOrEmpty(note.Rtf))
        {
            try
            {
                using var ms = new MemoryStream(Encoding.UTF8.GetBytes(note.Rtf));
                new TextRange(document.ContentStart, document.ContentEnd).Load(ms, DataFormats.Rtf);
            }
            catch
            {
                // Corrupt RTF: open the note empty rather than failing.
            }
        }

        _loadingNote = false;
    }

    private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingNotes || NotesList.SelectedItem is not NoteRow row) return;
        FlushNote();
        LoadNote(row.Note);
    }

    private void NoteNew_Click(object sender, RoutedEventArgs e)
    {
        FlushNote();
        var note = _notes.Create();
        _currentNote = null;
        RefreshNotes(note);
        FocusLater(NoteEditor);
    }

    private void NoteDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNote is null) return;
        _noteTimer.Stop();
        _notes.Delete(_currentNote);
        _currentNote = null;
        RefreshNotes();
    }

    private void NoteEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateNotePlaceholder();
        if (_loadingNote || _currentNote is null) return;
        _noteTimer.Stop();
        _noteTimer.Start();
    }

    /// <summary>Shows «Начните писать…» while the open note is a single empty paragraph; a list or any text hides it.</summary>
    private void UpdateNotePlaceholder()
    {
        // TextChanged can fire while InitializeComponent is still building the window.
        if (NotePlaceholder is null) return;
        var blocks = NoteEditor.Document.Blocks;
        var empty = blocks.Count == 0
            || blocks.Count == 1 && blocks.FirstBlock is Paragraph p
                && new TextRange(p.ContentStart, p.ContentEnd).Text.Length == 0;
        NotePlaceholder.Visibility = empty && _currentNote is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FlushNote()
    {
        if (!_noteTimer.IsEnabled) return;
        _noteTimer.Stop();
        SaveCurrentNote();
    }

    private void SaveCurrentNote()
    {
        if (_currentNote is not { } note) return;
        var range = new TextRange(NoteEditor.Document.ContentStart, NoteEditor.Document.ContentEnd);
        using var ms = new MemoryStream();
        range.Save(ms, DataFormats.Rtf);
        var rtf = Encoding.UTF8.GetString(ms.ToArray());
        var firstLine = range.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        if (firstLine.Length > 40) firstLine = firstLine[..40];
        _notes.Save(note, firstLine, rtf);

        _refreshingNotes = true;
        var rows = _notes.Items.Select(n => new NoteRow { Note = n }).ToList();
        NotesList.ItemsSource = rows;
        NotesList.SelectedItem = rows.FirstOrDefault(r => r.Note == note);
        _refreshingNotes = false;
    }

    // ---------- search ----------

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private async Task RunSearchAsync(string query)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        var cts = _searchCts = new CancellationTokenSource();
        if (string.IsNullOrWhiteSpace(query))
        {
            SearchList.ItemsSource = null;
            return;
        }

        try
        {
            var results = await _search.SearchAsync(query, _settings, cts.Token);
            if (cts.IsCancellationRequested) return;
            var rows = results.Select(r => new SearchRow { Result = r, Glyph = GlyphFor(r.Kind) }).ToList();
            SearchList.ItemsSource = rows;
            if (rows.Count > 0) SearchList.SelectedIndex = 0;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke.
        }
    }

    private static string GlyphFor(ResultKind kind) => kind switch
    {
        ResultKind.App => "square.grid.2x2",
        ResultKind.File => "doc",
        ResultKind.Ask => "bubble.left",
        _ => "globe",
    };

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var count = SearchList.Items.Count;
        switch (e.Key)
        {
            case Key.Down when count > 0:
                SearchList.SelectedIndex = Math.Min(count - 1, SearchList.SelectedIndex + 1);
                SearchList.ScrollIntoView(SearchList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when count > 0:
                SearchList.SelectedIndex = Math.Max(0, SearchList.SelectedIndex - 1);
                SearchList.ScrollIntoView(SearchList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter when SearchList.SelectedItem is SearchRow row:
                ExecuteResult(row.Result);
                e.Handled = true;
                break;
        }
    }

    private void SearchRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SearchRow row }) ExecuteResult(row.Result);
    }

    private void ExecuteResult(SearchResult result)
    {
        SystemUtil.Launch(result.Target);
        Collapse();
    }

    // ---------- external notifications ----------

    private void StartNotifyServer()
    {
        _notify.Notified += (title, message) =>
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_settings.DoNotDisturb) return;
                ShowAlert(title, message, 10000, "bubble.left");
            }));
        _notify.Start(_settings.NotifyPort);
    }
}
