using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Quickfetch;

internal sealed record QuickPowerMode(Guid Id, string Chinese, string English)
{
    internal string Name => Loc.IsEnglish ? English : Chinese;
    internal static readonly QuickPowerMode[] Choices =
    [
        new(new("961cc777-2547-4f9d-8174-7d86181b8a7a"), "最佳能效", "Best power efficiency"),
        new(Guid.Empty, "平衡", "Balanced"),
        new(new("ded574b5-45a0-4f42-8737-46345c09c238"), "最佳性能", "Best performance")
    ];

    internal static (bool PluggedIn, QuickPowerMode Mode) Current()
    {
        var ac = SystemInformation.PowerStatus.PowerLineStatus switch
        {
            PowerLineStatus.Online => true,
            PowerLineStatus.Offline => false,
            _ => throw new InvalidOperationException(Loc.IsEnglish ? "Power source unavailable." : "无法读取供电状态。")
        };
        var result = ac ? PowerGetUserConfiguredACPowerMode(out var id) : PowerGetUserConfiguredDCPowerMode(out id);
        if (result != 0) throw new Win32Exception(result);
        var mode = Choices.FirstOrDefault(choice => choice.Id == id) ??
            throw new InvalidOperationException(Loc.IsEnglish ? "Unrecognized power mode." : "未知电源模式。");
        return (ac, mode);
    }

    internal void Apply(bool pluggedIn)
    {
        var id = Id;
        var result = pluggedIn ? PowerSetUserConfiguredACPowerMode(in id) : PowerSetUserConfiguredDCPowerMode(in id);
        if (result != 0) throw new Win32Exception(result);
    }

    [DllImport("powrprof.dll")] private static extern int PowerGetUserConfiguredACPowerMode(out Guid id);
    [DllImport("powrprof.dll")] private static extern int PowerGetUserConfiguredDCPowerMode(out Guid id);
    [DllImport("powrprof.dll")] private static extern int PowerSetUserConfiguredACPowerMode(in Guid id);
    [DllImport("powrprof.dll")] private static extern int PowerSetUserConfiguredDCPowerMode(in Guid id);
}
