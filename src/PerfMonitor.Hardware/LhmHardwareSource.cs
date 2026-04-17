using LibreHardwareMonitor.Hardware;
using PerfMonitor.Core.Metrics;

namespace PerfMonitor.Hardware;

public sealed class LhmHardwareSource : IHardwareSource
{
    private readonly Computer _computer;
    private readonly bool _elevated;

    public bool TempsAvailable => _elevated;

    public LhmHardwareSource()
    {
        _elevated = ElevationCheck.IsElevated();
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsNetworkEnabled = true,
            IsMotherboardEnabled = _elevated,
            IsControllerEnabled = _elevated
        };
        _computer.Open();
    }

    public MetricsSnapshot Poll()
    {
        foreach (var hw in _computer.Hardware)
        {
            hw.Update();
            foreach (var sub in hw.SubHardware) sub.Update();
        }

        float cpuLoad = 0, gpuLoad = 0, gpuVramUsed = 0, gpuVramTotal = 0, netDown = 0, netUp = 0;
        float? cpuTemp = null, gpuTemp = null;
        float ramUsed = 0, ramTotal = 0;
        var fans = new List<FanReading>();

        foreach (var hw in _computer.Hardware)
        {
            foreach (var sensor in hw.Sensors)
            {
                if (hw.HardwareType == HardwareType.Cpu)
                {
                    if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                        cpuLoad = sensor.Value ?? 0;
                    if (_elevated && sensor.SensorType == SensorType.Temperature && sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                        cpuTemp = sensor.Value;
                }
                else if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                {
                    if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                        gpuLoad = sensor.Value ?? 0;
                    if (_elevated && sensor.SensorType == SensorType.Temperature && sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                        gpuTemp = sensor.Value;
                    if (sensor.SensorType == SensorType.SmallData && sensor.Name.Contains("Used", StringComparison.OrdinalIgnoreCase))
                        gpuVramUsed = (sensor.Value ?? 0) / 1024f;
                    if (sensor.SensorType == SensorType.SmallData && sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                        gpuVramTotal = (sensor.Value ?? 0) / 1024f;
                }
                else if (hw.HardwareType == HardwareType.Memory)
                {
                    if (sensor.SensorType == SensorType.Data && sensor.Name.Contains("Used"))
                        ramUsed = sensor.Value ?? 0;
                    if (sensor.SensorType == SensorType.Data && sensor.Name.Contains("Available"))
                        ramTotal = ramUsed + (sensor.Value ?? 0);
                }
                else if (hw.HardwareType == HardwareType.Network)
                {
                    if (sensor.SensorType == SensorType.Throughput && sensor.Name.Contains("Download"))
                        netDown += (sensor.Value ?? 0) / (1024f * 1024f);
                    if (sensor.SensorType == SensorType.Throughput && sensor.Name.Contains("Upload"))
                        netUp += (sensor.Value ?? 0) / (1024f * 1024f);
                }
                if (sensor.SensorType == SensorType.Fan)
                    fans.Add(new FanReading(sensor.Name, (int)(sensor.Value ?? 0)));
            }
        }

        return new MetricsSnapshot(
            DateTime.UtcNow, cpuLoad, cpuTemp, ramUsed, ramTotal,
            gpuLoad, gpuTemp, gpuVramUsed, gpuVramTotal,
            netDown, netUp, fans,
            _elevated ? MetricsHealth.Ok : MetricsHealth.TempsUnavailable);
    }

    public void Dispose() => _computer.Close();
}
