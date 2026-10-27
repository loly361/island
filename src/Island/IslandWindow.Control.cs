using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Island.Services;

namespace Island;

/// <summary>The «Управление» tab: Wi-Fi, Bluetooth, theme, VPN and power buttons.</summary>
public partial class IslandWindow
{
    private readonly ControlService _control = new();
    private readonly DispatcherTimer _disarmTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private string? _armed;

    private async Task RefreshControlAsync()
    {
        await _control.RefreshAsync();
        RenderControl();
    }

    private void RenderControl()
    {
        SetTile(WifiIcon, WifiState, _control.WifiOn, "Включён", "Выключен");
        SetTile(BluetoothIcon, BluetoothState, _control.BluetoothOn, "Включён", "Выключен");
        SetTile(ThemeIcon, ThemeState, ControlService.IsDarkTheme(), "Включена", "Выключена");
        SetTile(VpnIcon, VpnState, _control.VpnName is null ? null : _control.VpnConnected, "Подключён", "Выключен", "Не настроен");
    }

    private void SetTile(Border icon, TextBlock state, bool? on, string onText, string offText, string unavailable = "Недоступно")
    {
        if (on is null)
        {
            icon.Background = (Brush)FindResource("Surface2");
            state.Text = unavailable;
            return;
        }

        icon.Background = on.Value ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("Surface2");
        state.Text = on.Value ? onText : offText;
    }

    private async void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;
        try
        {
            switch (tag)
            {
                case "wifi":
                    await _control.SetWifiAsync(_control.WifiOn != true);
                    break;
                case "bluetooth":
                    await _control.SetBluetoothAsync(_control.BluetoothOn != true);
                    break;
                case "theme":
                    ControlService.SetDarkTheme(!ControlService.IsDarkTheme());
                    break;
                case "vpn" when _control.VpnName is null:
                    SystemUtil.Launch("ms-settings:network-vpn");
                    Collapse();
                    return;
                case "vpn":
                    VpnState.Text = "…";
                    await _control.ToggleVpnAsync();
                    break;
            }
        }
        catch
        {
            // A refused toggle is reflected by the refresh below.
        }

        await RefreshControlAsync();
    }

    private async void Power_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;

        switch (tag)
        {
            case "lock":
                Collapse();
                ControlService.Lock();
                return;
            case "sleep":
                Collapse();
                await Task.Delay(400);
                ControlService.Sleep();
                return;
        }

        // Restart and shutdown need a second click so a stray tap cannot end the session.
        if (_armed == tag)
        {
            Disarm();
            if (tag == "restart") ControlService.Restart();
            else ControlService.ShutDown();
            return;
        }

        Disarm();
        _armed = tag;
        var label = tag == "restart" ? RestartLabel : ShutdownLabel;
        label.Text = "Точно?";
        label.Foreground = (Brush)FindResource("AccentBrush");
        _disarmTimer.Tick -= OnDisarm;
        _disarmTimer.Tick += OnDisarm;
        _disarmTimer.Start();
    }

    private void OnDisarm(object? sender, EventArgs e) => Disarm();

    private void Disarm()
    {
        _disarmTimer.Stop();
        _armed = null;
        RestartLabel.Text = "Перезагрузка";
        ShutdownLabel.Text = "Выключение";
        RestartLabel.ClearValue(TextBlock.ForegroundProperty);
        ShutdownLabel.ClearValue(TextBlock.ForegroundProperty);
    }
}
