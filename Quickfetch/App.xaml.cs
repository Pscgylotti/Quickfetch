using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Quickfetch;

public partial class App : Application
{
    public App() => AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.DisableImplicitTouchKeyboardInvocation", true);
    internal bool SuppressLauncherStartup { get; init; }
    private Mutex? _instance;
    private EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showRegistration;
    private PowerLifecycle? _power;
    private string _exitReason = "dispatcher-exit-without-explicit-request";
    internal void ExitFromTray() { _exitReason = "tray-exit"; Shutdown(); }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (SuppressLauncherStartup) return;
        var id = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        _instance = new Mutex(true, "Local\\Quickfetch.Instance." + id, out var first);
        if (!first)
        {
            try { using var signal = EventWaitHandle.OpenExisting("Local\\Quickfetch.Show." + id); signal.Set(); } catch (WaitHandleCannotBeOpenedException) { }
            Shutdown(); return;
        }
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\Quickfetch.Show." + id);
        AppDiagnostics.Current.Start();
        DispatcherUnhandledException += (_, args) => AppDiagnostics.Current.Fault("ui-unhandled", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception) AppDiagnostics.Current.Fault("process-unhandled", exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) => { AppDiagnostics.Current.Fault("unobserved-task", args.Exception); args.SetObserved(); };
        SessionEnding += (_, args) => { _exitReason = "windows-session-ending:" + args.ReasonSessionEnding; AppDiagnostics.Current.Write("session-ending", args.ReasonSessionEnding.ToString()); };
        SetTheme();
        var window = new MainWindow();
        MainWindow = window;
        _showRegistration = ThreadPool.RegisterWaitForSingleObject(_showSignal,
            (_, _) => { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(window.ShowLauncher); }, null, Timeout.Infinite, false);
        window.InitializeLauncher(Array.Exists(e.Args, arg => arg == "--background"));
        try { _power = new PowerLifecycle(action =>
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(action);
        }, window.SuspendBackgroundInput, window.ResumeBackgroundInput); }
        catch (Exception ex) when (AppDiagnostics.IsRecoverable(ex)) { AppDiagnostics.Current.Fault("power-notifications", ex); }
    }

    private void SetTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        if (key?.GetValue("AppsUseLightTheme") is int light && light == 0)
        {
            foreach (var (name, color) in new[] { ("Surface", "#10171C27"), ("Panel", "#28FFFFFF"), ("KeyboardSurface", "#30202737"), ("KeySurface", "#26FFFFFF"), ("Ink", "#EDF2FF"), ("Muted", "#ADB9CD"), ("Accent", "#9CC7FF"), ("Line", "#38FFFFFF"), ("Selection", "#66567FAF") })
                Resources[name] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showRegistration?.Unregister(null);
        _power?.Dispose();
        (MainWindow as MainWindow)?.DisposeLauncher();
        _showSignal?.Dispose();
        if (_instance is not null) { try { _instance.ReleaseMutex(); } catch (ApplicationException) { } _instance.Dispose(); }
        if (!SuppressLauncherStartup) AppDiagnostics.Current.Stop(_exitReason, e.ApplicationExitCode);
        base.OnExit(e);
    }
}
