using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerfMonitor.Core.Settings;
using PerfMonitor.Core.ViewModels;
using PerfMonitor.Hardware;
using PerfMonitor.Windowing.Windows;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace PerfMonitor.App;

public partial class App : Application
{
    private IHost? _host;
    private PerfMonitor.Windowing.Behaviors.FullscreenDetector? _fullscreenDetector;
    private PerfMonitor.Windowing.Behaviors.HotkeyService? _hotkeyService;
    private PerfMonitor.Windowing.Behaviors.TopmostGuard? _topmostGuard;
    private PerfMonitor.Windowing.Behaviors.WindowPositionPersistence? _positionPersistence;
    private IWidgetWindow? _widget;
    private DisplayMode _activeMode;

    public IServiceProvider Services => _host!.Services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PerfMonitor");
        Directory.CreateDirectory(appDataDir);

        // Global crash logger — writes to %APPDATA%\PerfMonitor\crash.log so
        // silent WPF dispatcher exceptions leave a paper trail.
        var crashLog = Path.Combine(appDataDir, "crash.log");
        DispatcherUnhandledException += (_, ex) =>
        {
            File.AppendAllText(crashLog, $"[{DateTime.Now:u}] DISPATCHER: {ex.Exception}\n\n");
            ex.Handled = false;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            File.AppendAllText(crashLog, $"[{DateTime.Now:u}] APPDOMAIN: {ex.ExceptionObject}\n\n");
        };

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(lb => lb.AddDebug())
            .ConfigureServices(services =>
            {
                services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore(appDataDir));
                services.AddSingleton<MainViewModel>(sp =>
                {
                    var s = sp.GetRequiredService<ISettingsStore>().Load();
                    return new MainViewModel
                    {
                        CpuWarnPercent = s.Thresholds.CpuWarnPercent,
                        CpuCritPercent = s.Thresholds.CpuCritPercent,
                        CpuTempWarnC   = s.Thresholds.CpuTempWarnC,
                        CpuTempCritC   = s.Thresholds.CpuTempCritC,
                        GpuTempWarnC   = s.Thresholds.GpuTempWarnC,
                        GpuTempCritC   = s.Thresholds.GpuTempCritC
                    };
                });
                // LhmHardwareSource handles both elevated and unelevated cases internally
                // (returns null temps + Health = TempsUnavailable when unelevated).
                services.AddSingleton<IHardwareSource>(_ => new LhmHardwareSource());
                services.AddSingleton<HardwareMonitor>(sp =>
                {
                    var src = sp.GetRequiredService<IHardwareSource>();
                    var settings = sp.GetRequiredService<ISettingsStore>().Load();
                    return new HardwareMonitor(src, settings.Display.RefreshIntervalMs);
                });
                services.AddSingleton<PerfMonitor.Core.Metrics.IProcessSampler>(_ =>
                    new ProcessSampler(interval: TimeSpan.FromSeconds(1)));
                services.AddSingleton<IWidgetWindow>(sp =>
                    new PerfMonitor.Windowing.Windows.MainWidgetWindow(
                        sp.GetRequiredService<MainViewModel>(),
                        sp.GetRequiredService<PerfMonitor.Core.Metrics.IProcessSampler>()));
                services.AddSingleton<PerfMonitor.Tray.TrayIconHost>();
                services.AddSingleton<PerfMonitor.Windowing.INotificationService, Services.TrayNotificationService>();
                services.AddHostedService<Services.MetricsSampler>();
                services.AddHostedService<Services.PerfBudgetCheck>();
                services.AddTransient<PerfMonitor.Windowing.ViewModels.SettingsViewModel>();
                services.AddTransient<PerfMonitor.Windowing.Windows.SettingsWindow>();
            })
            .Build();

        _host.Start();

        var currentSettings = Services.GetRequiredService<ISettingsStore>().Load();
        if (currentSettings.Behavior.StartWithWindows && !PerfMonitor.Startup.StartupRegistrar.IsRegistered())
        {
            try { PerfMonitor.Startup.StartupRegistrar.Register(Environment.ProcessPath!); }
            catch { /* user can retry from settings */ }
        }
        else if (!currentSettings.Behavior.StartWithWindows && PerfMonitor.Startup.StartupRegistrar.IsRegistered())
        {
            try { PerfMonitor.Startup.StartupRegistrar.Unregister(); }
            catch { /* ignore */ }
        }

        _widget = Services.GetRequiredService<IWidgetWindow>();
        _activeMode = currentSettings.Display.Mode;
        _widget.Show();
        if (_widget is Window ww)
        {
            ww.Opacity = currentSettings.Display.Opacity;
            // Restore last-known position (validated against current monitors).
            PerfMonitor.Windowing.Behaviors.WindowPositionPersistence.Restore(
                ww, currentSettings.Display.Position.X, currentSettings.Display.Position.Y);
            // Persist on move (coalesced so drag events don't thrash disk).
            _positionPersistence = new PerfMonitor.Windowing.Behaviors.WindowPositionPersistence(
                ww, SavePosition);
        }

        var tray = Services.GetRequiredService<PerfMonitor.Tray.TrayIconHost>();
        tray.ExitRequested += (_, _) => Shutdown();
        tray.ToggleVisibilityRequested += (_, _) =>
        {
            if (_widget is Window w)
                w.Visibility = w.IsVisible ? System.Windows.Visibility.Hidden : System.Windows.Visibility.Visible;
        };
        tray.SettingsRequested += (_, _) =>
        {
            var win = Services.GetRequiredService<PerfMonitor.Windowing.Windows.SettingsWindow>();
            win.Show();
        };
        tray.Show();

        ApplyModeBehavior(currentSettings);

        if (_widget is Window hw)
        {
            _hotkeyService = new PerfMonitor.Windowing.Behaviors.HotkeyService(hw, ModifierKeys.Control | ModifierKeys.Alt, Key.M);
            _hotkeyService.ClickThroughChanged += OnClickThroughChanged;
            // Apply the effective click-through state for the boot configuration.
            _hotkeyService.SetClickThrough(EffectiveClickThrough(currentSettings));
        }

        // Live-apply settings whenever the settings file changes (via Save()
        // in-process or FileSystemWatcher for external edits).
        Services.GetRequiredService<ISettingsStore>().SettingsChanged += OnSettingsChanged;
    }

    /// <summary>
    /// Click-through is enabled if the user set it as default OR the mode is
    /// OnTop (where click-through is almost always what you want over a game).
    /// The hotkey can temporarily override this during a session.
    /// </summary>
    private static bool EffectiveClickThrough(AppSettings s) =>
        s.Behavior.ClickThroughByDefault || s.Display.Mode == DisplayMode.OnTop;

    private void SavePosition(int x, int y)
    {
        try
        {
            var store = Services.GetRequiredService<ISettingsStore>();
            var current = store.Load();
            // Skip the write if nothing changed — avoids the FileSystemWatcher
            // event loop firing ApplyLive on every drag end.
            if (current.Display.Position.X == x && current.Display.Position.Y == y) return;
            store.Save(current with
            {
                Display = current.Display with { Position = new Position(x, y) }
            });
        }
        catch { /* best-effort — a transient save miss is non-fatal */ }
    }

    private void OnClickThroughChanged(object? sender, bool on)
    {
        try
        {
            var notify = Services.GetRequiredService<PerfMonitor.Windowing.INotificationService>();
            notify.Show("PerfMonitor", on
                ? "Click-through ON — widget ignores mouse (Ctrl+Alt+M to toggle)"
                : "Click-through OFF — widget accepts clicks");
        }
        catch { /* toast is best-effort */ }
    }

    private void OnSettingsChanged(object? sender, AppSettings s)
    {
        Dispatcher.Invoke(() => ApplyLive(s));
    }

    private void ApplyLive(AppSettings s)
    {
        // Thresholds — applied on next sampler tick via MainViewModel.Classify
        var vm = Services.GetRequiredService<MainViewModel>();
        vm.CpuWarnPercent = s.Thresholds.CpuWarnPercent;
        vm.CpuCritPercent = s.Thresholds.CpuCritPercent;
        vm.CpuTempWarnC   = s.Thresholds.CpuTempWarnC;
        vm.CpuTempCritC   = s.Thresholds.CpuTempCritC;
        vm.GpuTempWarnC   = s.Thresholds.GpuTempWarnC;
        vm.GpuTempCritC   = s.Thresholds.GpuTempCritC;

        // Refresh rate
        Services.GetRequiredService<HardwareMonitor>().SetInterval(s.Display.RefreshIntervalMs);

        // Opacity
        if (_widget is Window w) w.Opacity = s.Display.Opacity;

        ApplyModeBehavior(s);

        // Click-through: re-derive from current settings + mode. The hotkey can
        // still temporarily override this afterward.
        _hotkeyService?.SetClickThrough(EffectiveClickThrough(s));

        // Start-with-Windows
        try
        {
            if (s.Behavior.StartWithWindows && !PerfMonitor.Startup.StartupRegistrar.IsRegistered())
                PerfMonitor.Startup.StartupRegistrar.Register(Environment.ProcessPath!);
            else if (!s.Behavior.StartWithWindows && PerfMonitor.Startup.StartupRegistrar.IsRegistered())
                PerfMonitor.Startup.StartupRegistrar.Unregister();
        }
        catch { /* non-fatal */ }

        // Floating ↔ OnTop is a pure behavior change on the same window;
        // ApplyModeBehavior handles it without needing a restart.
        _activeMode = s.Display.Mode;
    }

    /// <summary>
    /// Wires fullscreen-fade and topmost-reassert services based on the
    /// configured display mode. Floating respects AutoHideOnFullscreen;
    /// OnTop forces always-visible + periodic topmost re-assert.
    /// </summary>
    private void ApplyModeBehavior(AppSettings s)
    {
        if (_widget is not Window window) return;

        var wantFullscreenDetector =
            s.Display.Mode == DisplayMode.Floating && s.Behavior.AutoHideOnFullscreen;
        var wantTopmostGuard = s.Display.Mode == DisplayMode.OnTop;

        if (wantFullscreenDetector && _fullscreenDetector is null)
            _fullscreenDetector = new PerfMonitor.Windowing.Behaviors.FullscreenDetector(window);
        else if (!wantFullscreenDetector && _fullscreenDetector is not null)
        {
            _fullscreenDetector.Dispose();
            _fullscreenDetector = null;
            window.Opacity = s.Display.Opacity; // restore, in case we were mid-fade
        }

        if (wantTopmostGuard && _topmostGuard is null)
            _topmostGuard = new PerfMonitor.Windowing.Behaviors.TopmostGuard(window);
        else if (!wantTopmostGuard && _topmostGuard is not null)
        {
            _topmostGuard.Dispose();
            _topmostGuard = null;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Hard-capped shutdown: try to dispose everything cleanly, but
        // never wait more than ~4 s total. A wedged hardware driver post
        // sleep/resume must not prevent the process from exiting.
        var cleanup = Task.Run(() =>
        {
            try { _fullscreenDetector?.Dispose(); }  catch { }
            try { _topmostGuard?.Dispose(); }        catch { }
            try { _positionPersistence?.Dispose(); } catch { }
            try { _hotkeyService?.Dispose(); }       catch { }
            try { _host?.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); } catch { }
            try { _host?.Dispose(); } catch { }
        });
        cleanup.Wait(TimeSpan.FromSeconds(4));
        base.OnExit(e);
        // Belt-and-suspenders: if any finalizer or native handle is still
        // holding the CLR up, force termination. Managed state is flushed
        // in the cleanup Task above; this only affects stragglers.
        Environment.Exit(0);
    }
}

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
