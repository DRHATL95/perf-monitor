using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace PerfMonitor.App.Services;

public sealed class PerfBudgetCheck : BackgroundService
{
    private readonly ILogger<PerfBudgetCheck> _log;
    private const long MaxWorkingSetMb = 80;
    private const int MaxGdiHandles = 50;
    private const int MaxUserHandles = 100;

    public PerfBudgetCheck(ILogger<PerfBudgetCheck> log) => _log = log;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            using var p = Process.GetCurrentProcess();
            var ws = p.WorkingSet64 / (1024 * 1024);
            var gdi = GetGuiResources(p.Handle, 0);
            var user = GetGuiResources(p.Handle, 1);
            if (ws > MaxWorkingSetMb) _log.LogWarning("Working set {Ws} MB exceeds budget {Max} MB", ws, MaxWorkingSetMb);
            if (gdi > MaxGdiHandles) _log.LogWarning("GDI handles {Gdi} exceed budget {Max}", gdi, MaxGdiHandles);
            if (user > MaxUserHandles) _log.LogWarning("USER handles {User} exceed budget {Max}", user, MaxUserHandles);
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);
}
