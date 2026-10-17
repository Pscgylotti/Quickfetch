using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Quickfetch.Core;

namespace Quickfetch;

internal static class GestureActionRunner
{
    internal static ushort[] ParseShortcut(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new ArgumentException("请设置快捷键，例如 Ctrl + Alt + T。");
        var result = new List<ushort>();
        foreach (var part in parts)
        {
            ushort key = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => (ushort)0x11, "ALT" => 0x12, "SHIFT" => 0x10,
                "WIN" or "WINDOWS" => 0x5B,
                "ESC" or "ESCAPE" => 0x1B,
                _ => ConvertKey(part)
            };
            if (key == 0 || result.Contains(key)) throw new ArgumentException("快捷键无效或包含重复按键。");
            result.Add(key);
        }
        bool Modifier(ushort k) => k is 0x11 or 0x12 or 0x10 or 0x5B;
        if (result.Count(k => !Modifier(k)) != 1 || Modifier(result[^1]) || result.Take(result.Count - 1).Any(k => !Modifier(k)))
            throw new ArgumentException("快捷键应由修饰键加一个主键组成，例如 Win + S。");
        if (result.Count == 1 && result[0] != 0x1B && result[0] is < 0x70 or > 0x87)
            throw new ArgumentException("单键快捷键允许 Esc 或 F1–F24；其他按键请搭配 Ctrl / Alt / Shift / Win。");
        return result.ToArray();
    }
    private static ushort ConvertKey(string text)
    {
        if (text.Length == 1 && char.IsAsciiLetterOrDigit(text[0])) return char.ToUpperInvariant(text[0]);
        try
        {
            var key = (Key?)new KeyConverter().ConvertFromInvariantString(text);
            if (key is null or Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed) return 0;
            return (ushort)KeyInterop.VirtualKeyFromKey(key.Value);
        }
        catch (Exception ex) when (ex is NotSupportedException or FormatException or ArgumentException) { return 0; }
    }
    internal static void Validate(GestureAction action)
    {
        if (action.Kind == GestureActionKind.Shortcut) ParseShortcut(action.Shortcut);
        else if (action.Kind == GestureActionKind.Program)
        {
            var path = Environment.ExpandEnvironmentVariables(action.Program.Trim());
            if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new ArgumentException("请选择存在的程序或快捷方式文件。");
            if (!new[] { ".exe", ".lnk", ".bat", ".cmd" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("程序类型支持 .exe、.lnk、.bat 和 .cmd。");
            if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(action.Arguments))
                throw new ArgumentException(".lnk 请在快捷方式自身的属性中设置参数。");
        }
        else if (action.Kind != GestureActionKind.Disabled) throw new ArgumentException("动作类型无效。");
    }
    internal static void Run(GestureAction action, Action showLauncher)
    {
        Validate(action);
        if (action.Kind == GestureActionKind.Disabled) return;
        if (action.Kind == GestureActionKind.Program)
        {
            var path = Environment.ExpandEnvironmentVariables(action.Program.Trim());
            Process.Start(new ProcessStartInfo(path, action.Arguments) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path)! });
            return;
        }
        var keys = ParseShortcut(action.Shortcut);
        // Preserve the user's physical modifier state instead of releasing held keys.
        if (new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0))
            throw new InvalidOperationException("请先松开键盘上的修饰键再执行手势。");
        if (keys.SequenceEqual(new ushort[] { 0x5B, 0x53 })) { showLauncher(); return; }
        var input = keys.Select(k => Keyboard(k, 0)).Concat(keys.Reverse().Select(k => Keyboard(k, 2))).ToArray();
        Send(input);
    }
    internal static void RightClick(int x, int y)
    {
        var left = GetSystemMetrics(76); var top = GetSystemMetrics(77);
        var width = GetSystemMetrics(78); var height = GetSystemMetrics(79);
        var move = new Input { Data = new InputData { Mouse = new MouseInput { X = (int)Math.Round((x - left) * 65535d / Math.Max(1, width - 1)), Y = (int)Math.Round((y - top) * 65535d / Math.Max(1, height - 1)), Flags = 0xC001 } } };
        Send(new[] { move, new Input { Data = new InputData { Mouse = new MouseInput { Flags = 8 } } }, new Input { Data = new InputData { Mouse = new MouseInput { Flags = 16 } } } });
    }
    private static Input Keyboard(ushort key, uint flags) => new() { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { Key = key, Flags = flags | (key is >= 0x21 and <= 0x2E or 0x5B or 0x5C or 0x5D or 0x6F or 0xA3 or 0xA5 ? 1u : 0u) } } };
    private static void Send(Input[] input)
    {
        if (SendInput((uint)input.Length, input, Marshal.SizeOf<Input>()) != input.Length)
        {
            // A partial insertion must not leave synthetic modifiers pressed.
            var releases = input.Where(i => i.Type == 1 && (i.Data.Keyboard.Flags & 2) != 0).ToArray();
            if (releases.Length > 0) SendInput((uint)releases.Length, releases, Marshal.SizeOf<Input>());
            if (input.Any(i => i.Type == 0 && i.Data.Mouse.Flags == 8)) SendInput(1, new[] { new Input { Data = new InputData { Mouse = new MouseInput { Flags = 16 } } } }, Marshal.SizeOf<Input>());
            throw new Win32Exception(Marshal.GetLastWin32Error(), "输入未能发送；普通权限程序无法向管理员窗口发送按键或点击。");
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData { [FieldOffset(0)] public KeyboardInput Keyboard; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint MouseData, Flags, Time; public UIntPtr ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] input, int size);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}
