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
                        sp.GetRequiredService<PerfMonitor.Core.Metrics.IProcessSampler>(),
                        sp.GetRequiredService<PerfMonitor.Windowing.INotificationService>(),
                        sp.GetRequiredService<ISettingsStore>()));
                services.AddSingleton<PerfMonitor.Tray.TrayIconHost>();
                services.AddSingleton<PerfMonitor.Windowing.INotificationService, Services.TrayNotificationService>();
                services.AddHostedService<Services.MetricsSampler>();
                services.AddHostedService<Services.PerfBudgetCheck>();
                // Singleton + hosted-service-from-singleton so the App layer
                // can subscribe to UpdateAvailable on the same instance the
                // host is running.
                services.AddSingleton<Services.UpdateChecker>();
                services.AddHostedService(sp => sp.GetRequiredService<Services.UpdateChecker>());
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
        tray.ExitRequested += OnExitRequestedFromTray;
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
        // Open the GitHub release page when the user clicks the tray
        // "Install update" item. Browser launch via UseShellExecute so the
        // OS picks the user's default browser.
        tray.UpdateClicked += (_, url) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { /* malformed URL or blocked launcher — silent best-effort */ }
        };
        tray.Show();

        // UpdateChecker fires on a background thread; marshal to UI before
        // touching the tray menu.
        var updateChecker = Services.GetRequiredService<Services.UpdateChecker>();
        updateChecker.UpdateAvailable += (_, info) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                tray.SetUpdateAvailable(info.Tag, info.HtmlUrl);
                try
                {
                    var notify = Services.GetRequiredService<PerfMonitor.Windowing.INotificationService>();
                    notify.Show("PerfMonitor",
                        $"Update {info.Tag} available — right-click the tray icon to install.");
                }
                catch { /* notify is best-effort */ }
            });
        };

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
    /// Click-through is driven solely by the persistent setting. Display mode
    /// (Floating vs OnTop) controls visibility + topmost; it does NOT imply
    /// click-through. Users who want "over my game AND ignoring mouse" tick
    /// both "OnTop" mode and "Click-through by default" in Settings.
    /// The hotkey can temporarily override this during a session.
    /// </summary>
    private static bool EffectiveClickThrough(AppSettings s) =>
        s.Behavior.ClickThroughByDefault;

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

    /// <summary>
    /// Exit request from the tray context menu. We give WPF one render
    /// pass to close the menu cleanly and hide the widget + tray icons
    /// so the user sees immediate visual feedback, THEN call Shutdown
    /// on a lower-priority dispatcher queue so the render work completes
    /// before the dispatcher freezes.
    /// </summary>
    private void OnExitRequestedFromTray(object? sender, EventArgs e)
    {
        // Immediate visual feedback — widget and tray icons disappear
        // before any (potentially slow) cleanup starts. Perceived latency
        // is dominated by visible state, not by handle reclamation.
        if (_widget is Window w) w.Hide();
        try { Services.GetRequiredService<PerfMonitor.Tray.TrayIconHost>().Dispose(); } catch { }

        // Background priority runs after the Render pass that closes the
        // context menu, so the menu animates out cleanly before Shutdown
        // begins freezing the dispatcher.
        Dispatcher.BeginInvoke(new Action(Shutdown),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Synchronous cleanup: hotkey release is cheap and prevents a
        // stale Ctrl+Alt+M registration from briefly lingering if the user
        // relaunches immediately.
        try { _hotkeyService?.Dispose(); } catch { }

        // Fire-and-forget the slow disposals — sensor driver close and
        // HardwareMonitor loop join can each take ~1 s on a wedged driver
        // post-suspend. Environment.Exit below reaps stragglers, so we
        // don't block the UI thread waiting for them.
        _ = Task.Run(() =>
        {
            try { _fullscreenDetector?.Dispose(); }  catch { }
            try { _topmostGuard?.Dispose(); }        catch { }
            try { _positionPersistence?.Dispose(); } catch { }
            try { _host?.Dispose(); }                catch { }
        });

        base.OnExit(e);
        // Small grace window for WPF to finalize window tear-down
        // rendering before the process dies. 80 ms is imperceptible
        // to users but enough for the final render pass.
        Thread.Sleep(80);
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
