using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerfMonitor.Core.Settings;
using PerfMonitor.Core.ViewModels;
using PerfMonitor.Hardware;
using PerfMonitor.Windowing.Docking;
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
    private IWidgetWindow? _widget;
    private DisplayMode _activeMode;

    public IServiceProvider Services => _host!.Services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PerfMonitor");

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
                services.AddSingleton<IAppBarService, AppBarService>();
                services.AddSingleton<IWidgetWindow>(sp =>
                {
                    var vm = sp.GetRequiredService<MainViewModel>();
                    var settings = sp.GetRequiredService<ISettingsStore>().Load();
                    return settings.Display.Mode switch
                    {
                        DisplayMode.Floating     => new PerfMonitor.Windowing.Windows.MainWidgetWindow(vm),
                        DisplayMode.DockedTop    => new PerfMonitor.Windowing.Windows.DockedBarWindow(vm, sp.GetRequiredService<IAppBarService>(), AppBarEdge.Top),
                        DisplayMode.DockedBottom => new PerfMonitor.Windowing.Windows.DockedBarWindow(vm, sp.GetRequiredService<IAppBarService>(), AppBarEdge.Bottom),
                        DisplayMode.DockedLeft   => new PerfMonitor.Windowing.Windows.DockedBarWindow(vm, sp.GetRequiredService<IAppBarService>(), AppBarEdge.Left),
                        DisplayMode.DockedRight  => new PerfMonitor.Windowing.Windows.DockedBarWindow(vm, sp.GetRequiredService<IAppBarService>(), AppBarEdge.Right),
                        _ => new PerfMonitor.Windowing.Windows.MainWidgetWindow(vm)
                    };
                });
                services.AddSingleton<PerfMonitor.Tray.TrayIconHost>();
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
        if (_widget is Window ww) ww.Opacity = currentSettings.Display.Opacity;

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

        if (currentSettings.Behavior.AutoHideOnFullscreen && _widget is Window fsw)
            _fullscreenDetector = new PerfMonitor.Windowing.Behaviors.FullscreenDetector(fsw);

        if (_widget is Window hw)
            _hotkeyService = new PerfMonitor.Windowing.Behaviors.HotkeyService(hw, ModifierKeys.Control | ModifierKeys.Alt, Key.M);

        // Live-apply settings whenever the settings file changes (via Save()
        // in-process or FileSystemWatcher for external edits).
        Services.GetRequiredService<ISettingsStore>().SettingsChanged += OnSettingsChanged;
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

        // Auto-hide-on-fullscreen toggle
        if (s.Behavior.AutoHideOnFullscreen && _fullscreenDetector is null && _widget is Window fsw)
            _fullscreenDetector = new PerfMonitor.Windowing.Behaviors.FullscreenDetector(fsw);
        else if (!s.Behavior.AutoHideOnFullscreen && _fullscreenDetector is not null)
        {
            _fullscreenDetector.Dispose();
            _fullscreenDetector = null;
        }

        // Start-with-Windows
        try
        {
            if (s.Behavior.StartWithWindows && !PerfMonitor.Startup.StartupRegistrar.IsRegistered())
                PerfMonitor.Startup.StartupRegistrar.Register(Environment.ProcessPath!);
            else if (!s.Behavior.StartWithWindows && PerfMonitor.Startup.StartupRegistrar.IsRegistered())
                PerfMonitor.Startup.StartupRegistrar.Unregister();
        }
        catch { /* non-fatal */ }

        // Display mode change requires recreating the window — prompt for restart.
        if (s.Display.Mode != _activeMode)
        {
            var result = MessageBox.Show(
                $"Switching to {s.Display.Mode} requires restarting PerfMonitor. Restart now?",
                "PerfMonitor",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                var exe = Environment.ProcessPath;
                if (exe is not null)
                {
                    System.Diagnostics.Process.Start(exe);
                    Shutdown();
                }
            }
            else
            {
                _activeMode = s.Display.Mode; // don't re-prompt on each subsequent fire
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _fullscreenDetector?.Dispose();
        _hotkeyService?.Dispose();
        _host?.StopAsync().GetAwaiter().GetResult();
        _host?.Dispose();
        base.OnExit(e);
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
