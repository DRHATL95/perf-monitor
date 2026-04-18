using PerfMonitor.Core.Metrics;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PerfMonitor.Windowing.Controls;

public partial class ProcessListPanel : UserControl
{
    private const int TopN = 8;

    private static readonly Brush CpuAccent   = (Brush)new BrushConverter().ConvertFromString("#FF8A5A")!;
    private static readonly Brush RamAccent   = (Brush)new BrushConverter().ConvertFromString("#5AD0FF")!;
    private static readonly Brush GpuAccent   = (Brush)new BrushConverter().ConvertFromString("#A58AFF")!;
    private static readonly Brush MutedAccent = (Brush)new BrushConverter().ConvertFromString("#9AA0B4")!;

    /// <summary>
    /// Raised when the user confirms "End task" on a row. Consumers
    /// (MainWidgetWindow) should emit any notification toast. The panel
    /// itself only brokers the action — it has no tray / notification
    /// dependency.
    /// </summary>
    public event EventHandler<ProcessKillResult>? ProcessKilled;

    public ProcessListPanel() => InitializeComponent();

    private void OnRowRightClick(object sender, MouseButtonEventArgs e)
    {
        // Default WPF behavior already opens the ContextMenu on right-click
        // when Background != null. This handler is a placeholder for any
        // future "select row on right-click" behavior.
    }

    private void OnEndTaskClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        if (mi.DataContext is not Row row) return;

        var confirm = MessageBox.Show(
            row.Count > 1
                ? $"End all {row.Count} instances of {row.Name}?\n\nUnsaved work will be lost."
                : $"End task {row.Name}?\n\nUnsaved work will be lost.",
            "PerfMonitor",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;

        var result = KillByName(row.Name);
        ProcessKilled?.Invoke(this, result);
    }

    private static ProcessKillResult KillByName(string name)
    {
        var killed = 0;
        var denied = 0;
        var failed = 0;

        Process[] processes;
        try { processes = Process.GetProcessesByName(name); }
        catch { return new ProcessKillResult(name, 0, 0, 1); }

        foreach (var p in processes)
        {
            try
            {
                p.Kill(entireProcessTree: false);
                p.WaitForExit(500);
                killed++;
            }
            catch (System.ComponentModel.Win32Exception) { denied++; }
            catch (InvalidOperationException) { /* already exited */ }
            catch { failed++; }
            finally { try { p.Dispose(); } catch { } }
        }
        return new ProcessKillResult(name, killed, denied, failed);
    }

    /// <summary>Switch which metric we're drilling into and update the list.</summary>
    public void Show(ProcessMetric metric, IReadOnlyList<ProcessSnapshot>? snapshots)
    {
        switch (metric)
        {
            case ProcessMetric.Cpu:
                HeaderTitle.Text = "Top CPU";
                HeaderValue.Text = "%";
                DeferredNote.Visibility = Visibility.Collapsed;
                ProcessList.ItemsSource = Rank(snapshots, SortBy.Cpu);
                break;
            case ProcessMetric.Ram:
                HeaderTitle.Text = "Top RAM";
                HeaderValue.Text = "MB";
                DeferredNote.Visibility = Visibility.Collapsed;
                ProcessList.ItemsSource = Rank(snapshots, SortBy.Ram);
                break;
            case ProcessMetric.Gpu:
                HeaderTitle.Text = "Top GPU";
                HeaderValue.Text = "%";
                DeferredNote.Visibility = Visibility.Collapsed;
                ProcessList.ItemsSource = Rank(snapshots, SortBy.Gpu);
                break;
            case ProcessMetric.Net:
                HeaderTitle.Text = "Top NET";
                HeaderValue.Text = "";
                DeferredNote.Text = "Per-process network tracking isn't wired yet (needs ETW or socket-to-PID correlation).";
                DeferredNote.Visibility = Visibility.Visible;
                ProcessList.ItemsSource = Array.Empty<Row>();
                break;
        }
    }

    private enum SortBy { Cpu, Ram, Gpu }

    private static IEnumerable<Row> Rank(IReadOnlyList<ProcessSnapshot>? source, SortBy by)
    {
        if (source is null || source.Count == 0) return Array.Empty<Row>();
        var rows = new List<Row>(source.Count);
        foreach (var p in source)
        {
            rows.Add(new Row
            {
                Name = p.Name,
                Count = p.Count,
                CpuPercent = p.CpuPercent,
                WorkingSetMB = p.WorkingSetBytes / (1024f * 1024f),
                GpuPercent = p.GpuPercent
            });
        }
        IEnumerable<Row> ordered = by switch
        {
            SortBy.Cpu => rows.OrderByDescending(r => r.CpuPercent),
            SortBy.Ram => rows.OrderByDescending(r => r.WorkingSetMB),
            SortBy.Gpu => rows.OrderByDescending(r => r.GpuPercent),
            _ => rows
        };
        return ordered.Take(TopN)
            .Select(r =>
            {
                r.Display = r.Count > 1 ? $"{r.Name} ({r.Count})" : r.Name;
                (r.FormattedValue, r.AccentBrush, var noise) = by switch
                {
                    SortBy.Cpu => ($"{r.CpuPercent:F0}%",     CpuAccent, r.CpuPercent   < 0.5f),
                    SortBy.Ram => ($"{r.WorkingSetMB:F0}",    RamAccent, r.WorkingSetMB < 1f),
                    SortBy.Gpu => ($"{r.GpuPercent:F0}%",     GpuAccent, r.GpuPercent   < 0.5f),
                    _          => ("", (Brush)Brushes.White, true)
                };
                // Dim rows whose value effectively rounds to zero so the eye
                // lands on the interesting ones.
                if (noise) r.AccentBrush = MutedAccent;
                return r;
            })
            .ToList();
    }

    /// <summary>Row view-model for the DataTemplate binding.</summary>
    public sealed class Row
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public float CpuPercent { get; set; }
        public float WorkingSetMB { get; set; }
        public float GpuPercent { get; set; }
        public string Display { get; set; } = "";
        public string FormattedValue { get; set; } = "";
        public Brush AccentBrush { get; set; } = Brushes.White;
    }
}

/// <summary>
/// Summary of what an "End task" action actually managed to do.
/// Emitted by <see cref="ProcessListPanel.ProcessKilled"/>.
/// </summary>
public sealed record ProcessKillResult(string Name, int Killed, int Denied, int Failed)
{
    public string ToToastMessage()
    {
        if (Killed == 0 && Denied == 0 && Failed == 0)
            return $"{Name}: no running instances.";
        var parts = new List<string>();
        if (Killed > 0) parts.Add($"{Killed} ended");
        if (Denied > 0) parts.Add($"{Denied} denied (try running as admin)");
        if (Failed > 0) parts.Add($"{Failed} failed");
        return $"{Name}: {string.Join(", ", parts)}.";
    }
}
