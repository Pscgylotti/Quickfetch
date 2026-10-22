using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Quickfetch;

public sealed class Settings
{
    public string UiLanguage { get; set; } = "zh-CN";
    public string Folder { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
    public bool IncludeSubfolders { get; set; } = true;
    public bool IncludeRegisteredApps { get; set; } = true;
    public bool EnableFilePreview { get; set; }
    public Quickfetch.Core.DownloadOptions Downloads { get; set; } = new() { Folder = DownloadBridge.DefaultFolder() };
    public bool UseBuiltInKeyboard { get; set; } = true;
    public double[] FileColumnWeights { get; set; } = [2.2, 3.0, 0.9, 1.6];
    public uint FileSort { get; set; } = 1;
    public Quickfetch.Core.GestureSettings Gestures { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public string[] ShortcutRoots => Folder.TrimEnd(Path.DirectorySeparatorChar).Equals(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
        ? new[] { Folder, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) } : new[] { Folder };
    public static string Storage => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quickfetch");
    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(Storage, "settings.json"))) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Storage);
        var file = Path.Combine(Storage, "settings.json");
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, file, true);
    }
    public static bool AutoStart
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return key?.GetValue("Quickfetch") is string; }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (value) key.SetValue("Quickfetch", $"\"{Environment.ProcessPath}\" --background");
            else key.DeleteValue("Quickfetch", false);
        }
    }
}
