using System;
using System.Collections.Generic;
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
internal record AudioChoice(string Id, string Name, bool Current);

internal static class QuickSystemControls
{
    private const string Night = "SystemSettings_Display_BlueLight_AutomaticOnScheduleWithTime_ToggleSwitch";
    private const string Rotation = "SystemSettings_Display_IsRotationLocked_ToggleSwitch";
    private const string Airplane = "SystemSettings_Radio_IsAirplaneModeEnabled_SettingsApp_ToggleSwitch";
    private const string Hotspot = "SystemSettings_Connections_InternetSharingEnabled_ToggleSwitch";
    private const string NightRecord = "windows.data.bluelightreduction.bluelightreductionstate";

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
            case "night":
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$" + NightRecord + "\\" + NightRecord))
                    return new(key?.GetValue("Data") is byte[] data ? NightState(data) : null);
            case "rotation":
                return GetAutoRotationState(out var state) ? new((state & 1) != 0, (state & 0xFC) == 0) : new(null, false);
            default: throw new ArgumentOutOfRangeException(nameof(id));
        }
    }

    internal static bool? NightState(byte[] data)
    {
        ReadOnlySpan<byte> header = [0x43, 0x42, 1, 0];
        if (!data.AsSpan().StartsWith(header)) return null;
        var inner = data.AsSpan(header.Length);
        var offset = inner.IndexOf(header);
        if (offset < 0 || offset + header.Length >= inner.Length) return null;
        var payload = inner[(offset + header.Length)..];
        return payload[0] switch { 0x10 when payload.Length > 1 && payload[1] == 0 => true, 0 or 0xD0 or 0xC6 => false, _ => null };
    }

    internal static async Task Set(string id, bool? desired, CancellationToken token)
    {
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
        var (page, control) = id switch { "night" => ("display", Night), "rotation" => ("display", Rotation), "flight" => ("network-airplanemode", Airplane), _ => throw new ArgumentException(id) };
        await Task.Run(() => SetWindowsSwitch(page, control, desired, token), token);
    }

    // Use the system's actual switch for settings without a public desktop setter.
    // Never write to the private CloudStore binary or approximate airplane mode with separate radios.
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
        if (id == "bluetooth")
        {
            var selector = Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var devices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(selector);
            return string.Join(Environment.NewLine, devices.Select(d => d.Name).Where(s => s.Length > 0).Distinct().Take(12));
        }
        if (id == "wifi")
            return NetworkInformation.GetConnectionProfiles().FirstOrDefault(p => p.IsWlanConnectionProfile && p.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None)?.ProfileName;
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

    internal static List<AudioChoice> AudioDevices(DataFlow flow)
    {
        using var audio = new MMDeviceEnumerator();
        string? current = null;
        try { using var endpoint = audio.GetDefaultAudioEndpoint(flow, Role.Multimedia); current = endpoint.ID; }
        catch (COMException) { }
        var result = new List<AudioChoice>();
        foreach (var device in audio.EnumerateAudioEndPoints(flow, DeviceState.Active))
        using (device) result.Add(new(device.ID, device.FriendlyName, device.ID == current));
        return result;
    }

    internal static void SetDefaultAudio(string id)
    {
        var policy = (IPolicyConfig)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"))!)!;
        try { policy.SetDefaultEndpoint(id, Role.Console); policy.SetDefaultEndpoint(id, Role.Multimedia); }
        finally { Marshal.ReleaseComObject(policy); }
    }

    [DllImport("user32")] private static extern bool GetAutoRotationState(out uint state);
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        void GetMixFormat(); void GetDeviceFormat(); void ResetDeviceFormat(); void SetDeviceFormat();
        void GetProcessingPeriod(); void SetProcessingPeriod(); void GetShareMode(); void SetShareMode();
        void GetPropertyValue(); void SetPropertyValue();
        void SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, Role role);
    }
}
