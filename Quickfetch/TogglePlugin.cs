using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Quickfetch;

internal sealed record ToggleAction(string Id, string Title, string? TitleZh = null);
internal sealed class TogglePlugin
{
    public int Version { get; init; }
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string? TitleZh { get; init; }
    public string Icon { get; init; } = "⚙";
    public string Script { get; init; } = "";
    public bool ExecutablePicker { get; init; }
    public ToggleAction[] Actions { get; init; } = [];
    private string _scriptPath = "";
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static bool Identifier(string? value) => value is { Length: > 0 and < 65 } && Regex.IsMatch(value, @"\A[a-z][a-z0-9.-]*\z");
    internal static List<TogglePlugin> Discover(params string[] roots)
    {
        var items = new List<TogglePlugin>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var manifest = Path.Combine(dir, "plugin.json");
                        if (!File.Exists(manifest) || new FileInfo(manifest).Length > 64 * 1024) continue;
                        var p = JsonSerializer.Deserialize<TogglePlugin>(File.ReadAllText(manifest), Json);
                        if (p == null || p.Version != 1 || !Identifier(p.Id) || string.IsNullOrWhiteSpace(p.Title)
                            || string.IsNullOrEmpty(p.Script) || Path.GetFileName(p.Script) != p.Script || !p.Script.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)
                            || p.Actions == null || p.Actions.Any(a => a == null || !Identifier(a.Id) || string.IsNullOrWhiteSpace(a.Title))
                            || p.Actions.Select(a => a.Id).Distinct().Count() != p.Actions.Length) continue;
                        p._scriptPath = Path.Combine(dir, p.Script);
                        if (File.Exists(p._scriptPath) && ids.Add(p.Id)) items.Add(p);
                    }
                    catch (Exception e) when (AppDiagnostics.IsRecoverable(e)) { AppDiagnostics.Current.Fault("toggle-manifest", e); }
                }
            }
            catch (Exception e) when (AppDiagnostics.IsRecoverable(e)) { AppDiagnostics.Current.Fault("toggle-directory", e); }
        }
        return items;
    }

    internal async Task<ToggleReading> Run(string command, string? exe, CancellationToken token, string? shortcutFolder = null)
    {
        if (command is not ("status" or "on" or "off") && !Actions.Any(a => command == "action." + a.Id)) throw new ArgumentException(nameof(command));
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(_scriptPath)!
        };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-STA", "-ExecutionPolicy", "RemoteSigned", "-File", _scriptPath, command }) start.ArgumentList.Add(arg);
        start.Environment["QUICKFETCH_TOGGLE_EXE"] = exe ?? "";
        start.Environment["QUICKFETCH_SHORTCUT_FOLDER"] = shortcutFolder ?? "";
        token.ThrowIfCancellationRequested();
        using var process = Process.Start(start) ?? throw new IOException("Plugin could not start.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromSeconds(command == "status" ? 8 : 45));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(limit.Token);
            var error = process.StandardError.ReadToEndAsync(limit.Token);
            await Task.WhenAll(process.WaitForExitAsync(limit.Token), output, error);
            if (process.ExitCode != 0) throw new InvalidOperationException(error.Result.Trim() is { Length: > 0 } msg ? msg : "Plugin command failed.");
            using var reply = JsonDocument.Parse(output.Result);
            if (!reply.RootElement.TryGetProperty("on", out _)) throw new InvalidDataException("Plugin response requires an on field.");
            return reply.RootElement.Deserialize<ToggleReading>(Json) ?? throw new InvalidDataException("Empty plugin response.");
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) try { process.Kill(); } catch (InvalidOperationException) { }
            if (!token.IsCancellationRequested) throw new TimeoutException("Plugin timed out.");
            throw;
        }
    }
}
