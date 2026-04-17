using H.NotifyIcon;
using PerfMonitor.Core.ViewModels;
using PerfMonitor.Tray.Rendering;
using System.ComponentModel;
using System.Drawing;

namespace PerfMonitor.Tray;

public sealed class TrayIconHost : IDisposable
{
    private readonly MainViewModel _vm;
    private readonly TrayIconRenderer _renderer = new();
    private readonly TaskbarIcon _cpuIcon = new();
    private readonly TaskbarIcon _ramIcon = new();
    private readonly TaskbarIcon _gpuIcon = new();
    private readonly TaskbarIcon _cpuTempIcon = new();

    private int _lastCpu = -1, _lastRam = -1, _lastGpu = -1, _lastCpuTemp = -1;

    public TrayIconHost(MainViewModel vm)
    {
        _vm = vm;
        _cpuIcon.ToolTipText = "CPU load";
        _ramIcon.ToolTipText = "RAM used %";
        _gpuIcon.ToolTipText = "GPU load";
        _cpuTempIcon.ToolTipText = "CPU temperature";
        _vm.PropertyChanged += OnVmChanged;
        UpdateAll();
    }

    public void Show()
    {
        _cpuIcon.ForceCreate();
        _ramIcon.ForceCreate();
        _gpuIcon.ForceCreate();
        if (_vm.CpuTempAvailable) _cpuTempIcon.ForceCreate();
    }

    private void OnVmChanged(object? s, PropertyChangedEventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.CpuLoadPercent):
                    UpdateIfChanged(ref _lastCpu, (int)_vm.CpuLoadPercent,
                        v => SetIcon(_cpuIcon, v, Color.FromArgb(255, 255, 138, 90)),
                        v => _cpuIcon.ToolTipText = $"CPU {v}%");
                    break;
                case nameof(MainViewModel.RamUsedGb):
                case nameof(MainViewModel.RamTotalGb):
                    var ram = _vm.RamTotalGb > 0 ? (int)(_vm.RamUsedGb / _vm.RamTotalGb * 100) : 0;
                    UpdateIfChanged(ref _lastRam, ram,
                        v => SetIcon(_ramIcon, v, Color.FromArgb(255, 90, 208, 255)),
                        v => _ramIcon.ToolTipText = $"RAM {v}% ({_vm.RamUsedGb:F1} / {_vm.RamTotalGb:F0} GB)");
                    break;
                case nameof(MainViewModel.GpuLoadPercent):
                    UpdateIfChanged(ref _lastGpu, (int)_vm.GpuLoadPercent,
                        v => SetIcon(_gpuIcon, v, Color.FromArgb(255, 165, 138, 255)),
                        v => _gpuIcon.ToolTipText = $"GPU {v}%");
                    break;
                case nameof(MainViewModel.CpuTempC):
                    if (!_vm.CpuTempAvailable) return;
                    UpdateIfChanged(ref _lastCpuTemp, (int)_vm.CpuTempC,
                        v => SetIcon(_cpuTempIcon, v, Color.FromArgb(255, 255, 77, 109)),
                        v => _cpuTempIcon.ToolTipText = $"CPU {v}°C");
                    break;
            }
        });
    }

    private static void UpdateIfChanged(ref int last, int current,
        Action<int> apply, Action<int> tooltip)
    {
        if (last == current) return;
        last = current;
        apply(current);
        tooltip(current);
    }

    private void SetIcon(TaskbarIcon icon, int value, Color accent)
    {
        using var bmp = _renderer.RenderLoadBitmap(value, accent);
        icon.Icon = _renderer.ToIcon(bmp);
    }

    private void UpdateAll()
    {
        OnVmChanged(null, new PropertyChangedEventArgs(nameof(MainViewModel.CpuLoadPercent)));
        OnVmChanged(null, new PropertyChangedEventArgs(nameof(MainViewModel.RamUsedGb)));
        OnVmChanged(null, new PropertyChangedEventArgs(nameof(MainViewModel.GpuLoadPercent)));
        OnVmChanged(null, new PropertyChangedEventArgs(nameof(MainViewModel.CpuTempC)));
    }

    public void Dispose()
    {
        _cpuIcon.Dispose();
        _ramIcon.Dispose();
        _gpuIcon.Dispose();
        _cpuTempIcon.Dispose();
        _renderer.Dispose();
    }
}
