using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Island.Services;

namespace Island;

/// <summary>The «Настройки» tab: city, search links, accent colour and a few system shortcuts.</summary>
public partial class IslandWindow
{
    private static readonly (string Name, string? Hex)[] AccentChoices =
    {
        ("Из обоев", null),
        ("Синий", "#0A84FF"),
        ("Розовый", "#FF375F"),
        ("Оранжевый", "#FF9F0A"),
        ("Зелёный", "#30D158"),
    };

    private static readonly Brush WallpaperSwatch = Gradient3();

    private FileSystemWatcher? _settingsWatcher;
    private readonly DispatcherTimer _reloadTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    /// <summary>
    /// Edits to settings.json made by hand take effect without a restart. Our own saves come back here too and
    /// reload the same values, which is harmless; the open settings fields are not re-rendered, so typing is never reset.
    /// </summary>
    private void WatchSettingsFile()
    {
        _reloadTimer.Tick += (_, _) =>
        {
            _reloadTimer.Stop();
            ReloadSettingsFromDisk();
        };

        Directory.CreateDirectory(AppPaths.DataDir);
        _settingsWatcher = new FileSystemWatcher(AppPaths.DataDir, "settings.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
        };
        _settingsWatcher.Changed += OnSettingsFileChanged;
        _settingsWatcher.Created += OnSettingsFileChanged;
        _settingsWatcher.Renamed += OnSettingsFileChanged;
        _settingsWatcher.EnableRaisingEvents = true;
    }

    private void OnSettingsFileChanged(object sender, FileSystemEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _reloadTimer.Stop();
            _reloadTimer.Start();
        }));

    private void ReloadSettingsFromDisk()
    {
        // TryLoad returns null for a half-written or broken file, so the current values stay in place.
        var fresh = JsonStore.TryLoad<Settings>(AppPaths.File("settings.json"));
        if (fresh is null) return;

        _settings.CopyFrom(fresh);
        _ = RefreshAccentAsync(animate: true);
        RenderModeButtons();
        RenderLanguageButtons();
        ApplyMode();
    }

    private void RenderSettings()
    {
        SetCity.Text = _settings.City ?? "";
        SetLatitude.Text = _settings.Latitude?.ToString(CultureInfo.InvariantCulture) ?? "";
        SetLongitude.Text = _settings.Longitude?.ToString(CultureInfo.InvariantCulture) ?? "";
        SetSearch.Text = _settings.SearchUrl;
        SetAsk.Text = _settings.AskUrl;
        SetPort.Text = Loc.F("Порт уведомлений {0} нужен для хука Claude Code. Меняется в settings.json, после этого нужен перезапуск.", _settings.NotifyPort);
        SetHint.Visibility = Visibility.Collapsed;
        BuildAccentSwatches();
        RenderModeButtons();
        RenderLanguageButtons();
    }

    /// <summary>Highlights the saved language; the running window keeps the language it started with until a restart.</summary>
    private void RenderLanguageButtons()
    {
        var french = Loc.Normalize(_settings.Language) == Loc.French;
        StyleModeButton(LanguageRuButton, !french);
        StyleModeButton(LanguageFrButton, french);
    }

    private void Language_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string language }) return;

        _settings.Language = Loc.Normalize(language);
        JsonStore.Save(AppPaths.File("settings.json"), _settings);
        RenderLanguageButtons();
    }

    private void RenderModeButtons()
    {
        var minimal = Minimal;
        StyleModeButton(ModeStandardButton, !minimal);
        StyleModeButton(ModeMinimalButton, minimal);
    }

    /// <summary>The active mode is filled with the accent colour; resource references keep it in step with accent changes.</summary>
    private static void StyleModeButton(Button button, bool selected)
    {
        if (selected)
        {
            button.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
            button.Foreground = Brushes.White;
        }
        else
        {
            button.SetResourceReference(Control.BackgroundProperty, "Surface2");
            button.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        }
    }

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string mode }) return;

        _settings.Mode = mode;
        JsonStore.Save(AppPaths.File("settings.json"), _settings);
        RenderModeButtons();
        ApplyMode();
    }

    private void BuildAccentSwatches()
    {
        AccentPanel.Children.Clear();
        foreach (var (name, hex) in AccentChoices)
        {
            var selected = string.Equals(_settings.AccentHex, hex, StringComparison.OrdinalIgnoreCase);
            var swatch = new Button
            {
                Style = (Style)FindResource("SwatchBtn"),
                ToolTip = Loc.T(name),
                Tag = hex,
                Background = hex is null ? WallpaperSwatch : new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                BorderThickness = new Thickness(selected ? 2 : 0),
            };
            swatch.Click += Accent_Click;
            AccentPanel.Children.Add(swatch);
        }
    }

    private void Accent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: var tag }) return;

        _settings.AccentHex = tag as string;
        JsonStore.Save(AppPaths.File("settings.json"), _settings);
        BuildAccentSwatches();
        _ = RefreshAccentAsync(animate: true);
    }

    private void SettingsField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Keyboard.ClearFocus();
    }

    /// <summary>Applies a field when focus leaves it; a bad value is rejected and the old one comes back.</summary>
    private void SettingsField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box) return;

        var text = box.Text.Trim();
        var weatherChanged = false;
        var valid = true;

        if (box == SetCity)
        {
            _settings.City = text.Length == 0 ? null : text;
            weatherChanged = true;
        }
        else if (box == SetLatitude && TryCoordinate(text, 90, out var latitude))
        {
            _settings.Latitude = latitude;
            weatherChanged = true;
        }
        else if (box == SetLongitude && TryCoordinate(text, 180, out var longitude))
        {
            _settings.Longitude = longitude;
            weatherChanged = true;
        }
        else if (box == SetSearch && IsLinkTemplate(text)) _settings.SearchUrl = text;
        else if (box == SetAsk && IsLinkTemplate(text)) _settings.AskUrl = text;
        else valid = false;

        if (!valid)
        {
            RenderSettings();
            SetHint.Text = box == SetSearch || box == SetAsk
                ? Loc.T("Ссылка должна начинаться с http или https и содержать {0} там, где вставляется запрос.")
                : Loc.T("Координата — число: широта от −90 до 90, долгота от −180 до 180.");
            SetHint.Visibility = Visibility.Visible;
            return;
        }

        JsonStore.Save(AppPaths.File("settings.json"), _settings);
        if (weatherChanged) _ = UpdateWeatherAsync();
    }

    private void OpenSettingsFile_Click(object sender, RoutedEventArgs e)
    {
        JsonStore.Save(AppPaths.File("settings.json"), _settings);
        SystemUtil.Launch(AppPaths.File("settings.json"));
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        SystemUtil.Launch(AppPaths.DataDir);
    }

    private static bool TryCoordinate(string text, double limit, out double value) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && Math.Abs(value) <= limit;

    private static bool IsLinkTemplate(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && text.Contains("{0}", StringComparison.Ordinal);

    private static Brush Gradient3()
    {
        var brush = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromRgb(0xFF, 0x9F, 0x0A), 0),
                new(Color.FromRgb(0xFF, 0x37, 0x5F), 0.5),
                new(Color.FromRgb(0x0A, 0x84, 0xFF), 1),
            },
            new Point(0, 0),
            new Point(1, 1));
        brush.Freeze();
        return brush;
    }
}
