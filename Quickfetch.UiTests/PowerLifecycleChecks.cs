using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using Quickfetch;

internal static class PowerLifecycleChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var folder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "QuickfetchPowerTest-" + Guid.NewGuid()));
        try
        {
            var diagnostics = new AppDiagnostics(folder);
            var pending = new Queue<Action>();
            long now = 10000; int suspends = 0, resumes = 0;
            using var lifecycle = new PowerLifecycle(pending.Enqueue, () => suspends++, () => resumes++, diagnostics, () => now, subscribe: false);
            void Drain() { while (pending.TryDequeue(out var operation)) operation(); }
            lifecycle.OnPower(PowerModes.StatusChange);
            check(pending.Count == 0 && !Directory.Exists(folder), "Battery/source status changes must not schedule recovery or write diagnostic logs.");
            lifecycle.OnPower(PowerModes.Suspend);
            check(suspends == 0 && pending.Count == 1, "System-event callbacks must marshal work rather than mutate UI on the notification thread.");
            Drain(); check(suspends == 1 && resumes == 0, "Suspend must release input only, without starting recovery.");
            lifecycle.OnPower(PowerModes.Resume);
            lifecycle.OnSession(SessionSwitchReason.SessionUnlock); Drain();
            check(resumes == 1, "A resume and unlock arriving together must rebind input once.");
            now += 3000; lifecycle.OnSession(SessionSwitchReason.SessionUnlock); Drain();
            check(resumes == 2, "A later unlock must be able to repair a stale input registration.");
            lifecycle.OnSession(SessionSwitchReason.SessionLock); Drain();
            check(resumes == 2, "Locking a session must not activate input on a protected desktop.");
            lifecycle.OnPower(PowerModes.Suspend); lifecycle.OnPower(PowerModes.Resume); Drain();
            check(suspends == 2 && resumes == 3, "A genuine second suspend/resume must recover even inside the duplicate-notification window.");
            now += 3000; lifecycle.OnSession(SessionSwitchReason.ConsoleConnect);
            lifecycle.Dispose(); Drain();
            check(resumes == 3, "Shutdown must discard an already queued recovery callback.");
            var before = File.ReadAllText(Path.Combine(folder, "lifecycle.log"));
            lifecycle.OnPower(PowerModes.Resume);
            check(File.ReadAllText(Path.Combine(folder, "lifecycle.log")) == before, "Disposed notification subscribers must neither resume input nor keep writing logs.");
            using var failing = new PowerLifecycle(a => a(), () => { }, () => throw new InvalidOperationException("injected-resume-failure"), diagnostics, subscribe: false);
            failing.OnPower(PowerModes.Resume);
            check(File.ReadAllText(Path.Combine(folder, "lifecycle.log")).Contains("power-recovery"), "A failed resume action must be contained and diagnosed.");
            using var liveSubscription = new PowerLifecycle(a => a(), () => { }, () => { }, diagnostics);
            check(true, "Native system-event subscriptions must be constructible and disposable without suspending the computer.");
        }
        finally
        {
            var expected = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!folder.StartsWith(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid power test cleanup path.");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
