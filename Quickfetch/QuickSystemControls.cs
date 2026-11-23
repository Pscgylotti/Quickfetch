using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;

namespace Quickfetch;

internal record ToggleReading(bool? On, bool Available = true, string? Note = null, string? Executable = null);

internal static class QuickSystemControls
{
    private const string Rotation = "SystemSettings_Display_IsRotationLocked_ToggleSwitch";
    private const string Airplane = "SystemSettings_Radio_IsAirplaneModeEnabled_SettingsApp_ToggleSwitch";
    private const string Hotspot = "SystemSettings_Connections_InternetSharingEnabled_ToggleSwitch";
    private const string EnergySaver = "SystemSettings_PowerAndBattery_EnergySaverAlwaysOn_ToggleSwitch";
    private const string EnergySaverSummary = "SystemSettings_PowerAndBattery_EnergySaver_ValueTextBlock";

    internal static async Task<ToggleReading> Read(string id)
    {
        switch (id)
        {
            case "wifi": case "bluetooth":
                var radios = await Radio.GetRadiosAsync();
                var devices = radios.Where(r => r.Kind == Kind(id)).ToArray();
                return new(devices.Any(r => r.State == RadioState.On), devices.Any(r => r.State is RadioState.On or RadioState.Off));
            case "hotspot":
                var profile = NetworkInformation.GetInternetConnectionProfile();
                if (profile == null) return new(null, false);
                try
                {
                    var manager = NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
                    return new(manager.TetheringOperationalState == TetheringOperationalState.On,
                        manager.TetheringOperationalState is TetheringOperationalState.On or TetheringOperationalState.Off);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or COMException)
                {
                    return new(null, true, Loc.IsEnglish ? "Uses the Windows hotspot switch." : "使用 Windows 热点开关。");
                }
            case "flight":
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\RadioManagement\SystemRadioState"))
                    return new(key?.GetValue("") is int flag ? flag != 0 : null);
            case "power":
                return new(await EnergySaverStatus.Read().ConfigureAwait(false), true, Loc.IsEnglish ? "Energy saver" : "节能模式");
            case "rotation":
                return GetAutoRotationState(out var state) ? new((state & 1) != 0, (state & 0xFC) == 0) : new(null, false);
            default: throw new ArgumentOutOfRangeException(nameof(id));
        }
    }

    internal static async Task Set(string id, bool? desired, CancellationToken token)
    {
        if (id == "power")
        {
            var current = await EnergySaverStatus.Read(token);
            var target = desired ?? !current;
            if (current == target) return;
            await Task.Run(() => SetWindowsSwitch("batterysaver", EnergySaver, target, token), token);
            for (int i = 0; i < 20; i++)
            {
                if (await EnergySaverStatus.Read(token) == target) return;
                await Task.Delay(100, token);
            }
            throw new InvalidOperationException(Loc.IsEnglish ? "Windows is still controlling Energy saver automatically." : "Windows 仍在自动管理节能模式。");
        }
        if (id is "wifi" or "bluetooth")
        {
            // RequestAccessAsync must begin on the UI thread.
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) throw new InvalidOperationException("Windows denied radio access.");
            var radios = (await Radio.GetRadiosAsync()).Where(r => r.Kind == Kind(id) && r.State is RadioState.On or RadioState.Off).ToArray();
            if (radios.Length == 0) throw new InvalidOperationException("Radio unavailable.");
            var next = (desired ?? !radios.Any(r => r.State == RadioState.On)) ? RadioState.On : RadioState.Off;
            foreach (var radio in radios.Where(r => r.State != next))
            {
                token.ThrowIfCancellationRequested();
                if (await radio.SetStateAsync(next) != RadioAccessStatus.Allowed) throw new InvalidOperationException("Windows could not switch " + radio.Name);
            }
            return;
        }
        if (id == "hotspot")
        {
            var profile = NetworkInformation.GetInternetConnectionProfile() ?? throw new InvalidOperationException("No shared connection.");
            try
            {
                var manager = NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
                bool enable = desired ?? manager.TetheringOperationalState != TetheringOperationalState.On;
                var result = enable ? await manager.StartTetheringAsync() : await manager.StopTetheringAsync();
                if (result.Status != TetheringOperationStatus.Success) throw new InvalidOperationException(result.Status + " " + result.AdditionalErrorMessage);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or COMException)
            {
                await Task.Run(() => SetWindowsSwitch("network-mobilehotspot", Hotspot, desired, token), token);
            }
            return;
        }
        var (page, control) = id switch { "rotation" => ("display", Rotation), "flight" => ("network-airplanemode", Airplane), _ => throw new ArgumentException(id) };
        await Task.Run(() => SetWindowsSwitch(page, control, desired, token), token);
    }

    // Use the system's actual switch for settings without a public desktop setter.
    private static void SetWindowsSwitch(string page, string id, bool? desired, CancellationToken token)
    {
        OpenSettings("ms-settings:" + page);
        for (int i = 0; i < 40; i++)
        {
            token.ThrowIfCancellationRequested();
            foreach (var process in Process.GetProcessesByName("SystemSettings"))
            using (process)
            {
                var filter = new AndCondition(new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id),
                    new PropertyCondition(AutomationElement.AutomationIdProperty, id));
                var shellWindows = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition);
                foreach (AutomationElement window in shellWindows)
                {
                    try
                    {
                        if (window.Current.ClassName != "ApplicationFrameWindow" && window.Current.ProcessId != process.Id) continue;
                        var element = window.FindFirst(TreeScope.Descendants, filter);
                        if (element == null && id == EnergySaver)
                        {
                            var summary = window.FindFirst(TreeScope.Descendants, new AndCondition(
                                new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id),
                                new PropertyCondition(AutomationElement.AutomationIdProperty, EnergySaverSummary)));
                            var header = summary == null ? null : TreeWalker.RawViewWalker.GetParent(summary);
                            if (header != null && header.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var pattern) &&
                                pattern is ExpandCollapsePattern section && section.Current.ExpandCollapseState == ExpandCollapseState.Collapsed)
                                section.Expand();
                        }
                        if (element == null || !element.TryGetCurrentPattern(TogglePattern.Pattern, out var raw)) continue;
                        if (!element.Current.IsEnabled) throw new InvalidOperationException("Windows has disabled this switch.");
                        var toggle = (TogglePattern)raw;
                        var target = desired ?? toggle.Current.ToggleState != ToggleState.On;
                        token.ThrowIfCancellationRequested();
                        if ((toggle.Current.ToggleState == ToggleState.On) == target) return;
                        toggle.Toggle();
                        for (int attempt = 0; attempt < 25; attempt++)
                        {
                            if ((toggle.Current.ToggleState == ToggleState.On) == target) return;
                            Thread.Sleep(100);
                        }
                        throw new InvalidOperationException("Windows did not confirm the requested switch state.");
                    }
                    catch (ElementNotAvailableException) { }
                }
            }
            Thread.Sleep(125);
        }
        throw new InvalidOperationException("Windows Settings did not expose this switch.");
    }

    internal static async Task<string?> Details(string id)
    {
        if (id == "hotspot" && NetworkInformation.GetInternetConnectionProfile() is { } network)
            return NetworkOperatorTetheringManager.CreateFromConnectionProfile(network).GetCurrentAccessPointConfiguration().Ssid;
        var state = await Read(id);
        if (!state.Available) return Loc.IsEnglish ? "Unavailable in the current device posture or configuration." : "当前设备姿态或配置下不可用。";
        return state.On switch { true => Loc.IsEnglish ? "On" : "已开启", false => Loc.IsEnglish ? "Off" : "已关闭", _ => state.Note };
    }

    private static RadioKind Kind(string id) => id == "wifi" ? RadioKind.WiFi : RadioKind.Bluetooth;
    internal static void OpenSettings(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });

    internal static int? Brightness(int? set = null)
    {
        const string scope = @"root\wmi";
        using var query = new ManagementObjectSearcher(scope, set.HasValue ? "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE" :
            "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE");
        using var rows = query.Get();
        foreach (ManagementObject row in rows)
        using (row)
        {
            if (set == null) return Convert.ToInt32(row["CurrentBrightness"]);
            row.InvokeMethod("WmiSetBrightness", new object[] { 0u, (byte)Math.Clamp(set.Value, 0, 100) });
            return set;
        }
        return null;
    }

    internal static int Volume(int? set = null)
    {
        using var audio = new MMDeviceEnumerator();
        using var speaker = audio.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        if (set.HasValue) speaker.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(set.Value, 0, 100) / 100f;
        return (int)Math.Round(speaker.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
    }

    [DllImport("user32")] private static extern bool GetAutoRotationState(out uint state);
}
