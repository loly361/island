using System.IO;
using Island.Services;

namespace Island.Tests;

public class RecordingRulesTests
{
    private static readonly PixelRect Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void FileName_UsesDateAndTimeWithDashes() =>
        Assert.Equal("Island-2026-11-10-09-05-07.mp4", RecordingRules.FileName(new DateTime(2026, 11, 10, 9, 5, 7)));

    [Fact]
    public void FileName_IgnoresCurrentCulture()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            Assert.Equal("Island-2026-01-02-03-04-05.mp4", RecordingRules.FileName(new DateTime(2026, 1, 2, 3, 4, 5)));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void UniqueFileName_AddsCounterWhenTaken()
    {
        var taken = new HashSet<string> { "Island-x.mp4", "Island-x-2.mp4" };
        Assert.Equal("Island-y.mp4", RecordingRules.UniqueFileName("Island-y.mp4", taken.Contains));
        Assert.Equal("Island-x-3.mp4", RecordingRules.UniqueFileName("Island-x.mp4", taken.Contains));
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(9.9, "00:09")]
    [InlineData(61, "01:01")]
    [InlineData(1800, "30:00")]
    [InlineData(3599, "59:59")]
    [InlineData(3661, "1:01:01")]
    [InlineData(-5, "00:00")]
    public void Elapsed_FormatsAsMinutesAndSeconds(double seconds, string expected) =>
        Assert.Equal(expected, RecordingRules.Elapsed(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void FitRegion_TrimsOddSizesToEven()
    {
        var r = RecordingRules.FitRegion(new PixelRect(10, 20, 641, 401), Screen)!.Value;
        Assert.Equal(new PixelRect(10, 20, 640, 400), r);
    }

    [Fact]
    public void FitRegion_ClipsToScreen()
    {
        var r = RecordingRules.FitRegion(new PixelRect(1800, 1000, 500, 500), Screen)!.Value;
        Assert.True(Screen.Contains(r));
        Assert.Equal(0, r.Width % 2);
        Assert.Equal(0, r.Height % 2);
        Assert.True(r.Width >= RecordingRules.MinSide && r.Height >= RecordingRules.MinSide);
    }

    [Fact]
    public void FitRegion_GrowsTinySelectionAroundItsCentre()
    {
        var r = RecordingRules.FitRegion(new PixelRect(500, 500, 10, 10), Screen)!.Value;
        Assert.Equal(RecordingRules.MinSide, r.Width);
        Assert.Equal(RecordingRules.MinSide, r.Height);
        Assert.Equal(505, r.X + r.Width / 2);
        Assert.Equal(505, r.Y + r.Height / 2);
    }

    [Fact]
    public void FitRegion_GrowsInsideTheScreenAtTheEdge()
    {
        var r = RecordingRules.FitRegion(new PixelRect(0, 1075, 6, 5), Screen)!.Value;
        Assert.True(Screen.Contains(r));
        Assert.Equal(0, r.X);
        Assert.Equal(1080, r.Bottom);
    }

    [Fact]
    public void FitRegion_WorksOnMonitorsLeftOfPrimary()
    {
        var virtualScreen = new PixelRect(-1280, 0, 3200, 1080);
        var r = RecordingRules.FitRegion(new PixelRect(-1279, 3, 301, 203), virtualScreen)!.Value;
        Assert.Equal(new PixelRect(-1279, 3, 300, 202), r);
    }

    [Fact]
    public void FitRegion_ReturnsNullOffScreen() =>
        Assert.Null(RecordingRules.FitRegion(new PixelRect(5000, 5000, 100, 100), Screen));

    [Fact]
    public void FitRegion_FullScreenStaysAsIs() =>
        Assert.Equal(Screen, RecordingRules.FitRegion(Screen, Screen));

    [Theory]
    [InlineData(1920, 1080, 1920, 1080)]
    [InlineData(2560, 1440, 2560, 1440)]
    [InlineData(3840, 2160, 3840, 2160)]
    [InlineData(5120, 2880, 4096, 2304)]
    [InlineData(7680, 2160, 4096, 1152)]
    [InlineData(5000, 3001, 3838, 2304)]
    public void OutputSize_FitsH264LimitsAndStaysEven(int w, int h, int ew, int eh)
    {
        var (ow, oh) = RecordingRules.OutputSize(w, h);
        Assert.Equal((ew, eh), (ow, oh));
        Assert.Equal(0, ow % 2);
        Assert.Equal(0, oh % 2);
        Assert.True(ow <= RecordingRules.MaxWidth && oh <= RecordingRules.MaxHeight);
    }

    [Fact]
    public void Bitrate_ScalesWithSizeWithinBounds()
    {
        Assert.Equal(2_000_000u, RecordingRules.Bitrate(128, 128));
        Assert.Equal(6_220_800u, RecordingRules.Bitrate(1920, 1080));
        Assert.Equal(25_000_000u, RecordingRules.Bitrate(4096, 2304));
    }

    [Fact]
    public void Limits_ThirtyMinutesAndDiskSpace()
    {
        Assert.Equal(30, RecordingRules.FrameRate);
        Assert.False(RecordingRules.ReachedLimit(TimeSpan.FromMinutes(29.99)));
        Assert.True(RecordingRules.ReachedLimit(TimeSpan.FromMinutes(30)));
        Assert.False(RecordingRules.HasRoomToStart(100L * 1024 * 1024));
        Assert.True(RecordingRules.HasRoomToStart(10L * 1024 * 1024 * 1024));
        Assert.True(RecordingRules.HasRoomToContinue(300L * 1024 * 1024));
        Assert.False(RecordingRules.HasRoomToContinue(100L * 1024 * 1024));
    }

    [Fact]
    public void Folder_IsIslandInsideVideos() =>
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Island"),
            RecordingRules.Folder());

    [Fact]
    public void NewIcons_Exist()
    {
        foreach (var name in new[] { "record.circle", "stop.fill", "video" }) Assert.NotNull(IconSet.Resolve(name));
    }
}

public class FullscreenDetectorTests
{
    private static readonly PixelRect Primary = new(0, 0, 2560, 1440);

    private static ForegroundWindowInfo Window(
        PixelRect? bounds = null, string cls = "UnityWndClass", bool own = false, bool maximized = false, bool caption = false,
        bool minimized = false, bool visible = true, bool primary = true, PixelRect? monitor = null) =>
        new(42, cls, own, visible, minimized, maximized, caption, bounds ?? Primary, monitor ?? Primary, primary);

    [Fact]
    public void BorderlessWindowCoveringPrimaryMonitor_IsFullscreen() =>
        Assert.True(FullscreenDetector.IsFullscreen(Window(), ShellNotificationState.AcceptsNotifications, false));

    [Fact]
    public void MaximisedWindowWithTitleBar_IsNotFullscreen()
    {
        // With an auto-hidden taskbar a maximised window overhangs the whole monitor by its border.
        var overhang = new PixelRect(-8, -8, 2576, 1456);
        Assert.False(FullscreenDetector.IsFullscreen(Window(overhang, "Chrome_WidgetWin_1", maximized: true, caption: true), ShellNotificationState.AcceptsNotifications, false));
        Assert.False(FullscreenDetector.IsFullscreen(Window(overhang, "Chrome_WidgetWin_1", maximized: true, caption: true), ShellNotificationState.Busy, false));
    }

    [Fact]
    public void MaximisedWindowWithoutTitleBar_IsFullscreen() =>
        Assert.True(FullscreenDetector.IsFullscreen(Window(maximized: true, caption: false), ShellNotificationState.AcceptsNotifications, false));

    [Fact]
    public void OrdinaryWindow_IsNotFullscreen() =>
        Assert.False(FullscreenDetector.IsFullscreen(Window(new PixelRect(100, 100, 1200, 800), "Notepad", caption: true), ShellNotificationState.AcceptsNotifications, false));

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("XamlExplorerHostIslandWindow")]
    [InlineData("MultitaskingViewFrame")]
    public void DesktopAndShell_AreNotFullscreen(string cls)
    {
        Assert.True(FullscreenDetector.IsShellWindow(cls));
        Assert.False(FullscreenDetector.IsFullscreen(Window(cls: cls), ShellNotificationState.AcceptsNotifications, true));
        Assert.False(FullscreenDetector.IsFullscreen(Window(cls: cls), ShellNotificationState.Busy, true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OwnWindowInFront_KeepsThePreviousAnswer(bool previous)
    {
        Assert.Equal(previous, FullscreenDetector.IsFullscreen(Window(own: true), ShellNotificationState.AcceptsNotifications, previous));
        Assert.Equal(previous, FullscreenDetector.IsFullscreen(Window(new PixelRect(0, 0, 10, 10), own: true), ShellNotificationState.RunningD3DFullScreen, previous));
    }

    [Theory]
    [InlineData(ShellNotificationState.RunningD3DFullScreen)]
    [InlineData(ShellNotificationState.PresentationMode)]
    public void ExclusiveFullscreenAndPresentation_AreReportedByWindows(ShellNotificationState state)
    {
        Assert.True(FullscreenDetector.IsFullscreen(ForegroundWindowInfo.None, state, false));
        Assert.True(FullscreenDetector.IsFullscreen(Window(new PixelRect(0, 0, 800, 600), caption: true), state, false));
    }

    [Fact]
    public void BusyState_TrustsTheShellForBorderlessWindows() =>
        Assert.True(FullscreenDetector.IsFullscreen(Window(new PixelRect(0, 0, 2560, 1439)), ShellNotificationState.Busy, false));

    [Fact]
    public void FullscreenOnSecondaryMonitor_DoesNotHideTheIsland()
    {
        var second = new PixelRect(2560, 0, 1920, 1080);
        Assert.False(FullscreenDetector.IsFullscreen(Window(second, primary: false, monitor: second), ShellNotificationState.Busy, false));
    }

    [Fact]
    public void MinimisedOrHiddenOrMissingWindow_IsNotFullscreen()
    {
        Assert.False(FullscreenDetector.IsFullscreen(Window(minimized: true), ShellNotificationState.AcceptsNotifications, true));
        Assert.False(FullscreenDetector.IsFullscreen(Window(visible: false), ShellNotificationState.AcceptsNotifications, true));
        Assert.False(FullscreenDetector.IsFullscreen(ForegroundWindowInfo.None, ShellNotificationState.AcceptsNotifications, true));
    }

    [Fact]
    public void PixelRect_ContainsAndIntersect()
    {
        Assert.True(new PixelRect(-8, -8, 2576, 1456).Contains(Primary));
        Assert.False(new PixelRect(0, 0, 2560, 1400).Contains(Primary));
        Assert.Equal(new PixelRect(100, 100, 50, 50), new PixelRect(0, 0, 150, 150).Intersect(new PixelRect(100, 100, 200, 200)));
        Assert.True(new PixelRect(0, 0, 10, 10).Intersect(new PixelRect(20, 20, 5, 5)).IsEmpty);
    }

    [Fact]
    public void Settings_GameModeIsOnByDefaultAndCopied()
    {
        Assert.True(new Settings().GameMode);
        var target = new Settings();
        target.CopyFrom(new Settings { GameMode = false });
        Assert.False(target.GameMode);
    }
}
