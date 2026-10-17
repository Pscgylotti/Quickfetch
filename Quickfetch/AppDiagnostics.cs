using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Quickfetch;

// Event-only bounded diagnostics. No polling, queries, touch traces or normal input events are logged.
internal sealed class AppDiagnostics
{
    private readonly string _folder;
    private readonly object _gate = new();
    private readonly int _limit;
    private bool _started;
    internal static AppDiagnostics Current { get; } = new(Path.Combine(Settings.Storage, "diagnostics"));
    internal AppDiagnostics(string folder, int limit = 256 * 1024) { _folder = folder; _limit = limit; }
    internal void Start()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
            try
            {
                Directory.CreateDirectory(_folder);
                var marker = Path.Combine(_folder, "running.json");
                if (File.Exists(marker))
                    Write("previous-run-incomplete", "Previous session did not record a normal exit; forced termination, power loss and a crash are not distinguishable from this marker alone.");
                File.WriteAllText(marker, JsonSerializer.Serialize(new { Pid = Environment.ProcessId, StartedUtc = DateTimeOffset.UtcNow }));
                Write("start", "Version=" + (typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? typeof(App).Assembly.GetName().Version?.ToString()));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
    internal void Stop(string reason, int code)
    {
        lock (_gate)
        {
            if (!_started) return;
            Write("exit", $"Reason={reason}; Code={code}");
            try { File.Delete(Path.Combine(_folder, "running.json")); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            _started = false;
        }
    }
    internal void Fault(string component, Exception exception) => Write("exception:" + component, exception.ToString());
    internal void Write(string kind, string message)
    {
        lock (_gate)
        try
        {
            Directory.CreateDirectory(_folder);
            var file = Path.Combine(_folder, "lifecycle.log");
            var line = $"{DateTimeOffset.UtcNow:O} pid={Environment.ProcessId} {kind} {message.Replace("\r", "").Replace("\n", " | ")}\n";
            if (line.Length > 16000) line = line[..16000] + " [truncated]\n";
            if (File.Exists(file) && new FileInfo(file).Length + Encoding.UTF8.GetByteCount(line) > _limit)
                File.Move(file, file + ".old", true);
            File.AppendAllText(file, line, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Logging must never terminate the application. */ }
    }
    internal static bool IsRecoverable(Exception exception) => exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException);
}
