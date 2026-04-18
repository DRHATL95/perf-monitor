using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PerfMonitor.Hardware;

/// <summary>
/// Reads the Windows "GPU Engine" performance counter category (Windows 10
/// version 1903+) and returns a PID → aggregate-utilization-percent map.
///
/// Task Manager's "GPU" column for a process is the sum of "Utilization
/// Percentage" across that PID's engines (3D / Compute / Copy / Video*).
/// Counters return an instantaneous sample, so the first read after a
/// process spawns may report zero until Windows has taken a baseline.
/// </summary>
public sealed class GpuEnginePerProcess
{
    private const string CategoryName = "GPU Engine";
    private const string CounterName  = "Utilization Percentage";

    // "pid_1234_luid_..._eng_0_engtype_3D" -> capture 1234
    private static readonly Regex PidPattern = new(@"^pid_(\d+)_", RegexOptions.Compiled);

    /// <summary>
    /// Returns aggregate GPU utilization percent per PID. Empty on systems
    /// that don't expose the counter (pre-1903) or where access is denied.
    /// </summary>
    public Dictionary<int, float> Read()
    {
        var result = new Dictionary<int, float>();
        try
        {
            if (!PerformanceCounterCategory.Exists(CategoryName)) return result;

            var category = new PerformanceCounterCategory(CategoryName);
            var instances = category.GetInstanceNames();

            foreach (var instance in instances)
            {
                var match = PidPattern.Match(instance);
                if (!match.Success) continue;
                if (!int.TryParse(match.Groups[1].ValueSpan, out var pid)) continue;

                try
                {
                    using var counter = new PerformanceCounter(CategoryName, CounterName, instance, readOnly: true);
                    var value = counter.NextValue();
                    if (value <= 0) continue;
                    result[pid] = result.TryGetValue(pid, out var existing)
                        ? existing + value
                        : value;
                }
                catch
                {
                    // Instance can disappear between enumeration and read —
                    // skip and continue.
                }
            }
        }
        catch
        {
            // Category missing, insufficient permissions, or PDH service
            // unavailable — return whatever we collected.
        }
        return result;
    }
}
