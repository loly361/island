using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Island.Interop;
using Island.Services;

namespace Island;

/// <summary>
/// Screen recording: the «Запись экрана» tile, the red indicator with the elapsed time on the pill and in the panel header,
/// stopping from the tray, and saving to Videos\Island. The island itself is excluded from the capture while recording.
/// </summary>
public partial class IslandWindow
{
    private static readonly TimeSpan ExitFinishTimeout = TimeSpan.FromSeconds(15);

    private readonly DispatcherTimer _recTick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private ScreenRecorder? _recorder;
    private bool _recordStarting;

    public bool IsRecording => _recorder is not null;

    private void InitRecording()
    {
        _recTick.Tick += (_, _) => RenderRecording();
    }

    private void Record_Click(object sender, RoutedEventArgs e)
    {
        if (_recorder is not null)
        {
            StopRecording();
            return;
        }

        if (_recordStarting) return;
        var choose = RecordChoice.Visibility != Visibility.Visible;
        RecordChoice.Visibility = choose ? Visibility.Visible : Visibility.Collapsed;
        ToolTiles.Visibility = choose ? Visibility.Hidden : Visibility.Visible;
    }

    private async void RecordChoice_Click(object sender, RoutedEventArgs e)
    {
        RecordChoice.Visibility = Visibility.Collapsed;
        ToolTiles.Visibility = Visibility.Visible;
        if (sender is not FrameworkElement { Tag: string tag } || tag == "cancel") return;
        await StartRecordingAsync(area: tag == "area");
    }

    private void RecStop_Click(object sender, RoutedEventArgs e) => StopRecording();

    /// <summary>Also used by the tray menu.</summary>
    public void StopRecording() => _recorder?.StopAsync(RecordingStopReason.User);

    private async Task StartRecordingAsync(bool area)
    {
        if (_recorder is not null || _recordStarting || _toolBusy) return;

        _recordStarting = true;
        PixelRect? region = null;
        string? problem = null;
        _toolBusy = true;
        try
        {
            Collapse();
            Hide();
            await Task.Delay(340);

            var screen = ToPixels(System.Windows.Forms.SystemInformation.VirtualScreen);
            if (area)
            {
                using var shot = ScreenTools.CaptureScreen(out var bounds);
                if (PickRegion(shot, bounds) is { } picked)
                    region = RecordingRules.FitRegion(new PixelRect(picked.X + bounds.X, picked.Y + bounds.Y, picked.Width, picked.Height), screen);
            }
            else
            {
                // The island lives on the primary monitor, so «Весь экран» is that monitor.
                var primary = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? System.Windows.Forms.SystemInformation.VirtualScreen;
                region = RecordingRules.FitRegion(ToPixels(primary), screen);
            }

            // Excluded before the window comes back, so not even the first frame shows the island.
            if (region is not null) SetCaptureExcluded(true);
        }
        catch (Exception ex)
        {
            problem = ex.Message;
        }
        finally
        {
            _toolBusy = false;
            Show();
            PositionWindow();
        }

        if (region is not { } r)
        {
            _recordStarting = false;
            if (problem is not null) ShowAlert(Loc.T("Не удалось начать запись"), Shorten(problem));
            return;
        }

        try
        {
            var recorder = await ScreenRecorder.StartAsync(r, RecordingRules.Folder(), DateTime.Now);
            _recorder = recorder;
            _ = recorder.Completion.ContinueWith(
                t => Dispatcher.BeginInvoke(new Action(() => OnRecordingFinished(recorder, t.Result))),
                TaskScheduler.Default);
            _recTick.Start();
            RenderRecording();
        }
        catch (Exception ex)
        {
            SetCaptureExcluded(false);
            if (ex is not RecordingStartException) App.LogError(ex);
            ShowAlert(Loc.T("Не удалось начать запись"), Shorten(ex.Message));
        }
        finally
        {
            _recordStarting = false;
        }
    }

    private void OnRecordingFinished(ScreenRecorder recorder, RecordingResult result)
    {
        if (_recorder != recorder) return;
        _recorder = null;
        _recTick.Stop();
        SetCaptureExcluded(false);
        RenderRecording();

        var note = result.Reason switch
        {
            RecordingStopReason.TimeLimit => Loc.F("Достигнут предел {0} мин", (int)RecordingRules.MaxDuration.TotalMinutes),
            RecordingStopReason.LowDisk => Loc.T("Не хватает места на диске"),
            RecordingStopReason.DisplayChanged => Loc.T("Изменился экран"),
            RecordingStopReason.CaptureFailed => Loc.T("Не удалось снять экран"),
            _ => null,
        };

        if (result.Saved && result.Path is { } path)
        {
            ShowAlert(Loc.T("Запись сохранена"), Loc.F("{0} — нажмите, чтобы показать в папке", System.IO.Path.GetFileName(path)),
                8000, "video", note, (Brush)FindResource("RecordBrush"), onClick: () => SystemUtil.RevealInExplorer(path));
            return;
        }

        if (result.Error is { } error) App.LogError(error);
        var message = note ?? (result.Error is null ? Loc.T("Запись слишком короткая") : Shorten(result.Error.Message));
        ShowAlert(Loc.T("Не удалось сохранить запись"), message);
    }

    /// <summary>The recorded rectangle no longer matches the screen; finish the file with what was recorded so far.</summary>
    private void OnDisplayChangedWhileRecording() => _recorder?.StopAsync(RecordingStopReason.DisplayChanged);

    /// <summary>On exit or log-off: finish the MP4 before the process ends, so it is never left half-written.</summary>
    public void FinishRecordingNow()
    {
        var recorder = _recorder;
        if (recorder is null) return;
        _recorder = null;
        _recTick.Stop();
        try
        {
            // The recorder never needs this thread, so waiting here cannot deadlock.
            recorder.StopAsync(RecordingStopReason.AppExit).Wait(ExitFinishTimeout);
        }
        catch (Exception ex)
        {
            App.LogError(ex);
        }

        SetCaptureExcluded(false);
    }

    private void RenderRecording()
    {
        var recorder = _recorder;
        var on = recorder is not null;
        var time = on ? RecordingRules.Elapsed(recorder!.Elapsed) : "";
        RecChipText.Text = time;
        RecStopText.Text = time;
        RecChip.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        RecStopButton.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        RecordTileGlyph.Kind = on ? "stop.fill" : "record.circle";
        if (on) RecordTileGlyph.Foreground = (Brush)FindResource("RecordBrush");
        else RecordTileGlyph.ClearValue(Island.Icon.ForegroundProperty);
        RecordTileLabel.Text = on ? Loc.T("Остановить") : Loc.T("Запись экрана");

        // Minimal mode: the resting circle's dot turns red while recording.
        if (on) MinimalDot.Fill = (Brush)FindResource("RecordBrush");
        else MinimalDot.SetResourceReference(Shape.FillProperty, "AccentBrush");

        if (on == _recChipShown) return;
        _recChipShown = on;
        RefreshCollapsedSize();
    }

    private void SetCaptureExcluded(bool excluded)
    {
        if (_hwnd == IntPtr.Zero) return;
        if (!Native.SetWindowDisplayAffinity(_hwnd, excluded ? Native.WDA_EXCLUDEFROMCAPTURE : Native.WDA_NONE) && excluded)
            App.LogError(new InvalidOperationException("SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) failed"));
    }

    private static PixelRect ToPixels(System.Drawing.Rectangle r) => new(r.X, r.Y, r.Width, r.Height);

    private static string Shorten(string text) => text.Length > 80 ? text[..80] : text;
}
