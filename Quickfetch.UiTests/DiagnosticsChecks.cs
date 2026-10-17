using System;
using System.IO;
using System.Threading;
using Quickfetch;
using Quickfetch.Core;

internal static class DiagnosticsChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var folder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "QuickfetchDiagnosticsTest-" + Guid.NewGuid()));
        try
        {
            var diagnostics = new AppDiagnostics(folder, 1024);
            diagnostics.Start();
            var marker = Path.Combine(folder, "running.json"); var log = Path.Combine(folder, "lifecycle.log");
            check(File.Exists(marker), "Starting the primary instance must create a session marker.");
            var before = File.ReadAllText(log); diagnostics.Start();
            check(before == File.ReadAllText(log), "Repeated initialization must not produce duplicate lifecycle writes.");
            diagnostics.Stop("test-explicit-exit", 0);
            check(!File.Exists(marker) && File.ReadAllText(log).Contains("test-explicit-exit"), "Normal exit must record its reason and clear the marker.");
            File.WriteAllText(marker, "{}");
            var resumed = new AppDiagnostics(folder, 1024); resumed.Start();
            check(File.ReadAllText(log).Contains("previous-run-incomplete"), "A missing normal exit must be detectable at the next startup.");
            resumed.Stop("test", 0);
            for (var i = 0; i < 30; i++) resumed.Write("rotation-test", new string('x', 100));
            check(File.Exists(log + ".old") && new FileInfo(log).Length <= 1024 && new FileInfo(log + ".old").Length <= 1024, "Diagnostic logs must rotate within bounded storage.");
            var blocked = Path.Combine(folder, "blocked"); File.WriteAllText(blocked, "file, not folder");
            var unwritable = new AppDiagnostics(blocked); unwritable.Start(); unwritable.Fault("test", new IOException("test")); unwritable.Stop("test", 0);
            check(File.ReadAllText(blocked) == "file, not folder", "Inaccessible diagnostic storage must not crash or replace existing files.");
            using var ready = new ManualResetEventSlim();
            using (var listener = new GlobalTouchGestures(new GestureSettings(), (_, _, _) => { }, _ => { ready.Set(); throw new InvalidOperationException("injected-status-callback-failure"); }, resumed))
                check(ready.Wait(TimeSpan.FromSeconds(5)), "The touch listener must reach its status callback during fault injection.");
            check(File.ReadAllText(log).Contains("touch-status-callback"), "A throwing native listener subscriber must be contained and diagnosed instead of escaping its thread.");
            check(!AppDiagnostics.IsRecoverable(new OutOfMemoryException()) && !AppDiagnostics.IsRecoverable(new AccessViolationException()), "Fatal runtime failures must not be silently swallowed.");
        }
        finally
        {
            var expected = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!folder.StartsWith(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid test cleanup path.");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
