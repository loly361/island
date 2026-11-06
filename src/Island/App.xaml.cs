using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Media.Animation;
using Island.Interop;
using Island.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Island;

public partial class App : Application
{
    private Mutex? _mutex;
    private Forms.NotifyIcon? _tray;
    private IslandWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "Island.SingleInstance.9d1f", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // WPF animates at 60 fps by default; on a fast monitor that looks stepped, so match the display's refresh rate.
        Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline),
            new FrameworkPropertyMetadata { DefaultValue = Native.DisplayRefreshRate() });

        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            args.Handled = true;
        };

        var settings = JsonStore.Load(AppPaths.File("settings.json"), () => new Settings());
        if (settings.NotifyToken.Length == 0)
        {
            settings.NotifyToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            JsonStore.Save(AppPaths.File("settings.json"), settings);
        }

        var clips = new ClipboardHistory(AppPaths.File("clipboard.dat"));
        var notes = new NotesStore(AppPaths.File("notes.json"));

        // The autostart entry stores the exe path; rewrite it so moving the app folder never breaks startup.
        if (Autostart.IsEnabled()) Autostart.Set(true);

        _window = new IslandWindow(settings, clips, notes);
        _window.Show();
        BuildTray(settings);
    }

    private void BuildTray(Settings settings)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => _window?.OpenHome());
        menu.Items.Add("Поиск", null, (_, _) => _window?.OpenSearch());

        var autostart = new Forms.ToolStripMenuItem("Запускать вместе с Windows") { Checked = Autostart.IsEnabled(), CheckOnClick = true };
        autostart.CheckedChanged += (_, _) => Autostart.Set(autostart.Checked);
        menu.Items.Add(autostart);

        var inbox = new Forms.ToolStripMenuItem("Показывать уведомления Windows") { Checked = settings.NotificationsEnabled, CheckOnClick = true };
        inbox.CheckedChanged += async (_, _) =>
        {
            if (_window is not null) await _window.SetInboxEnabledAsync(inbox.Checked);
        };
        menu.Items.Add(inbox);

        menu.Items.Add("Папка с данными", null, (_, _) =>
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            SystemUtil.Launch(AppPaths.DataDir);
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Quit());

        _tray = new Forms.NotifyIcon
        {
            Icon = MakeIcon(),
            Text = "Island",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => _window?.OpenHome();
    }

    private void Quit()
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }

        _window?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private static Drawing.Icon MakeIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);
            using var pill = new Drawing.SolidBrush(Drawing.Color.FromArgb(240, 240, 244));
            using var dot = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 84, 112));
            g.FillRectangle(pill, 3, 3, 26, 6);
            g.FillPie(pill, 3, 0, 26, 18, 0, 180);
            g.FillEllipse(dot, 12, 6, 8, 8);
        }

        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private static void LogError(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.AppendAllText(AppPaths.File("error.log"), $"{DateTime.Now:O}\n{ex}\n\n");
        }
        catch
        {
            Debug.WriteLine(ex);
        }
    }
}
