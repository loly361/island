using System;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Island.Services;

namespace Island;

public sealed class ReminderRow
{
    public required Reminder Reminder { get; init; }
    public string Text => Reminder.Text;
    public string DueLabel { get; init; } = "";
}

/// <summary>Timer ring, quick presets and the natural-language reminders list.</summary>
public partial class IslandWindow
{
    private const double RingCenter = 39, RingRadius = 36;
    private const double MiniRingCenter = 7, MiniRingRadius = 6;

    private readonly ReminderStore _reminders = new(AppPaths.File("reminders.json"));
    private readonly CountdownTimer _timer = new();
    private readonly DispatcherTimer _timerTick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _reminderTick = new() { Interval = TimeSpan.FromSeconds(5) };
    private int _presetMinutes = 5;

    private void InitTimerAndReminders()
    {
        _timer.Set(TimeSpan.FromMinutes(_presetMinutes));
        _timerTick.Tick += OnTimerTick;
        _reminderTick.Tick += OnReminderTick;
        Loaded += (_, _) =>
        {
            HighlightPreset();
            RenderTimer();
            _timerTick.Start();
            _reminderTick.Start();
            OnReminderTick(null, EventArgs.Empty);
        };
    }

    // ---------- timer ----------

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_timer.Tick(DateTime.UtcNow)) OnTimerFinished();
        UpdateTimerChip();
        if (_state == State.Expanded && _tab == IxNotes) RenderTimer();
    }

    private void OnTimerFinished()
    {
        SystemSounds.Asterisk.Play();
        var minutes = (int)Math.Round(_timer.Total.TotalMinutes);
        ShowAlert(Loc.T("Таймер завершён"), Loc.F("{0} мин прошло", minutes), 10000, "clock", null, (Brush)FindResource("TimerBrush"));
        if (_tab == IxNotes) RenderTimer();
    }

    private static string ClockText(TimeSpan t)
    {
        var seconds = (int)Math.Ceiling(Math.Max(0, t.TotalSeconds));
        return TimeLabels.Clock(TimeSpan.FromSeconds(seconds));
    }

    private void UpdateTimerChip()
    {
        var show = _timer.Running;
        if (show)
        {
            TimerChipText.Text = ClockText(_timer.Remaining);
            DrawArc(MiniRingArc, _timer.Fraction, MiniRingCenter, MiniRingRadius);
        }
        if (show == _timerChipShown) return;

        _timerChipShown = show;
        TimerChip.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        RefreshCollapsedSize();
    }

    private void RenderTimer()
    {
        TimerText.Text = ClockText(_timer.Remaining);
        DrawRing(_timer.Fraction);
        TimerStartGlyph.Kind = _timer.Running ? "pause.fill" : "play.fill";
        TimerStartLabel.Text = Loc.T(_timer.Running ? "Пауза" : _timer.Paused ? "Продолжить" : "Старт");
        TimerSecondaryLabel.Text = Loc.T(_timer.Running || _timer.Paused ? "Сбросить" : "Своё время");
        UpdateTimerChip();
    }

    private void DrawRing(double fraction) => DrawArc(TimerArc, fraction, RingCenter, RingRadius);

    /// <summary>Draws a clockwise arc from 12 o'clock covering <paramref name="fraction"/> of a full turn.</summary>
    private static void DrawArc(System.Windows.Shapes.Path target, double fraction, double center, double radius)
    {
        if (fraction <= 0.001)
        {
            target.Data = Geometry.Empty;
            return;
        }

        var angle = Math.Min(fraction, 0.9999) * 2 * Math.PI;
        var end = new Point(center + radius * Math.Sin(angle), center - radius * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = new Point(center, center - radius), IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, angle > Math.PI, SweepDirection.Clockwise, true));
        target.Data = new PathGeometry(new[] { figure });
    }

    private void TimerStart_Click(object sender, RoutedEventArgs e)
    {
        if (_timer.Running) _timer.Pause(DateTime.UtcNow);
        else _timer.Start(DateTime.UtcNow);
        RenderTimer();
    }

    private void TimerSecondary_Click(object sender, RoutedEventArgs e)
    {
        if (_timer.Running || _timer.Paused)
        {
            _timer.Reset();
            RenderTimer();
            return;
        }

        var show = TimerCustom.Visibility != Visibility.Visible;
        TimerCustom.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) FocusLater(TimerCustom);
    }

    private void TimerPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } || !int.TryParse(tag, out var minutes)) return;
        _presetMinutes = minutes;
        _timer.Set(TimeSpan.FromMinutes(minutes));
        HighlightPreset();
        RenderTimer();
    }

    private void TimerCustom_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            TimerCustom.Visibility = Visibility.Collapsed;
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (!int.TryParse(TimerCustom.Text.Trim(), out var minutes) || minutes is < 1 or > 600) return;

        _presetMinutes = minutes;
        _timer.Set(TimeSpan.FromMinutes(minutes));
        _timer.Start(DateTime.UtcNow);
        TimerCustom.Clear();
        TimerCustom.Visibility = Visibility.Collapsed;
        HighlightPreset();
        RenderTimer();
    }

    private void HighlightPreset()
    {
        var accent = (Brush)FindResource("TimerBrush");
        var normal = (Brush)FindResource("Surface2");
        foreach (var button in PresetRow.Children.OfType<Button>())
        {
            var selected = button.Tag is string tag && int.TryParse(tag, out var m) && m == _presetMinutes;
            button.Background = selected ? accent : normal;
            button.Foreground = selected ? new SolidColorBrush(Color.FromRgb(0x1A, 0x11, 0x00)) : (Brush)FindResource("TextBrush");
        }
    }

    // ---------- reminders ----------

    private void RenderReminders()
    {
        var now = DateTime.Now;
        var rows = _reminders.Items
            .Select(r => new ReminderRow { Reminder = r, DueLabel = ReminderFormat.Due(r.Due, r.Daily, now) })
            .ToList();
        ReminderList.ItemsSource = rows;
        ReminderEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ReminderBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;

        var parsed = ReminderParser.Parse(ReminderBox.Text, DateTime.Now);
        if (parsed is null)
        {
            ReminderHint.Text = Loc.T("Не удалось понять, когда напомнить. Например: «завтра в 9», «через 20 минут», «каждый день в 23:00».");
            ReminderHint.Visibility = Visibility.Visible;
            return;
        }

        _reminders.Add(parsed);
        ReminderBox.Clear();
        ReminderHint.Visibility = Visibility.Collapsed;
        RenderReminders();
    }

    private void ReminderDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ReminderRow row }) return;
        _reminders.Remove(row.Reminder);
        RenderReminders();
    }

    private void OnReminderTick(object? sender, EventArgs e)
    {
        var due = _reminders.TakeDue(DateTime.Now);
        if (due.Count == 0) return;

        SystemSounds.Exclamation.Play();
        var last = due[^1];
        ShowAlert(Loc.T("Напоминание"), due.Count == 1 ? last.Text : Loc.F("{0} и ещё {1}", last.Text, due.Count - 1), 12000, "bell", null, (Brush)FindResource("TimerBrush"));
        if (_state == State.Expanded && _tab == IxNotes) RenderReminders();
    }
}
