using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerfMonitor.Core.Settings;
using PerfMonitor.Core.ViewModels;
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
            })
            .Build();

        _host.Start();
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
