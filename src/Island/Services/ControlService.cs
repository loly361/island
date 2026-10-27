using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.Devices.Radios;

namespace Island.Services;

/// <summary>Wi-Fi, Bluetooth, theme, VPN and power actions behind the «Управление» tab.</summary>
public sealed class ControlService
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static readonly TimeSpan VpnCacheTime = TimeSpan.FromMinutes(1);
    private Radio? _wifi, _bluetooth;
    private DateTime _vpnCheckedAt = DateTime.MinValue;

    public bool? WifiOn => _wifi is null ? null : _wifi.State == RadioState.On;

    public bool? BluetoothOn => _bluetooth is null ? null : _bluetooth.State == RadioState.On;

    /// <summary>Name of the first Windows VPN profile, or null when none is configured.</summary>
    public string? VpnName { get; private set; }

    public bool VpnConnected { get; private set; }

    /// <summary>The VPN query starts PowerShell, so it runs at most once a minute unless <paramref name="force"/> is set.</summary>
    public async Task RefreshAsync(bool force = false)
    {
        try
        {
            if (await Radio.RequestAccessAsync().AsTask() == RadioAccessStatus.Allowed)
            {
                var radios = await Radio.GetRadiosAsync().AsTask();
                _wifi = radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
                _bluetooth = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            }
            else
            {
                _wifi = _bluetooth = null;
            }
        }
        catch
        {
            _wifi = _bluetooth = null;
        }

        await RefreshVpnAsync(force);
    }

    public Task<bool> SetWifiAsync(bool on) => SetRadioAsync(_wifi, on);

    public Task<bool> SetBluetoothAsync(bool on) => SetRadioAsync(_bluetooth, on);

    private static async Task<bool> SetRadioAsync(Radio? radio, bool on)
    {
        if (radio is null) return false;
        try
        {
            return await radio.SetStateAsync(on ? RadioState.On : RadioState.Off).AsTask() == RadioAccessStatus.Allowed;
        }
        catch
        {
            return false;
        }
    }

    // ---------- theme ----------

    public static bool IsDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    public static void SetDarkTheme(bool dark)
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: true);
        if (key is null) return;
        var value = dark ? 0 : 1;
        key.SetValue("AppsUseLightTheme", value, RegistryValueKind.DWord);
        key.SetValue("SystemUsesLightTheme", value, RegistryValueKind.DWord);
        SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, UIntPtr.Zero, "ImmersiveColorSet", 0x0002, 200, out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);

    // ---------- VPN ----------

    private async Task RefreshVpnAsync(bool force = false)
    {
        if (!force && DateTime.UtcNow - _vpnCheckedAt < VpnCacheTime) return;
        _vpnCheckedAt = DateTime.UtcNow;

        try
        {
            var json = await RunAsync("powershell.exe",
                "-NoProfile", "-NonInteractive", "-Command",
                "[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-VpnConnection | Select-Object Name,ConnectionStatus | ConvertTo-Json -Compress");
            VpnName = null;
            VpnConnected = false;
            if (string.IsNullOrWhiteSpace(json)) return;

            using var doc = JsonDocument.Parse(json);
            var first = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().FirstOrDefault()
                : doc.RootElement;
            if (first.ValueKind != JsonValueKind.Object) return;

            VpnName = first.GetProperty("Name").GetString();
            // ConnectionStatus is an enum serialised as a number (2 = Connected) or as its name.
            var status = first.GetProperty("ConnectionStatus");
            VpnConnected = status.ValueKind == JsonValueKind.Number
                ? status.GetInt32() == 2
                : string.Equals(status.GetString(), "Connected", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            VpnName = null;
            VpnConnected = false;
        }
    }

    public async Task<bool> ToggleVpnAsync()
    {
        if (VpnName is null) return false;
        // Passed as separate arguments, so a profile name containing quotes cannot change the command line.
        var args = VpnConnected ? new[] { VpnName, "/disconnect" } : new[] { VpnName };
        await RunAsync("rasdial.exe", args);
        await RefreshVpnAsync(force: true);
        return true;
    }

    private static async Task<string> RunAsync(string file, params string[] args)
    {
        var info = new ProcessStartInfo(file)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info);
        if (process is null) return "";
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output.Trim();
    }

    // ---------- power ----------

    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    public static void Lock() => LockWorkStation();

    public static void Sleep() =>
        System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, false, false);

    public static void Restart() => Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false });

    public static void ShutDown() => Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false });
}
