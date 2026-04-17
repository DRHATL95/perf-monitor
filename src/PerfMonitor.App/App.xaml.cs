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

namespace PerfMonitor.App;

public partial class App : Application
{
    private IHost? _host;

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
            })
            .Build();

        _host.Start();

        var widget = Services.GetRequiredService<IWidgetWindow>();
        widget.Show();

        var tray = Services.GetRequiredService<PerfMonitor.Tray.TrayIconHost>();
        tray.ExitRequested += (_, _) => Shutdown();
        tray.ToggleVisibilityRequested += (_, _) =>
        {
            if (widget is Window w)
                w.Visibility = w.IsVisible ? System.Windows.Visibility.Hidden : System.Windows.Visibility.Visible;
        };
        tray.Show();

        if (Services.GetRequiredService<ISettingsStore>().Load().Behavior.AutoHideOnFullscreen
            && widget is Window w)
        {
            _ = new PerfMonitor.Windowing.Behaviors.FullscreenDetector(w);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
