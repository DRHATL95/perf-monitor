using PerfMonitor.Core.Metrics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PerfMonitor.Windowing.Controls;

public partial class ProcessListPanel : UserControl
{
    private const int TopN = 8;

    private static readonly Brush CpuAccent   = (Brush)new BrushConverter().ConvertFromString("#FF8A5A")!;
    private static readonly Brush RamAccent   = (Brush)new BrushConverter().ConvertFromString("#5AD0FF")!;
    private static readonly Brush MutedAccent = (Brush)new BrushConverter().ConvertFromString("#9AA0B4")!;

    public ProcessListPanel() => InitializeComponent();

    /// <summary>Switch which metric we're drilling into and update the list.</summary>
    public void Show(ProcessMetric metric, IReadOnlyList<ProcessSnapshot>? snapshots)
    {
        switch (metric)
        {
            case ProcessMetric.Cpu:
                HeaderTitle.Text = "Top CPU";
                HeaderValue.Text = "%";
                DeferredNote.Visibility = Visibility.Collapsed;
                ProcessList.ItemsSource = Rank(snapshots, byCpu: true);
                break;
            case ProcessMetric.Ram:
                HeaderTitle.Text = "Top RAM";
                HeaderValue.Text = "MB";
                DeferredNote.Visibility = Visibility.Collapsed;
                ProcessList.ItemsSource = Rank(snapshots, byCpu: false);
                break;
            case ProcessMetric.Gpu:
                HeaderTitle.Text = "Top GPU";
                HeaderValue.Text = "";
                DeferredNote.Text = "Per-process GPU tracking isn't wired yet. Use Task Manager's GPU column for now.";
                DeferredNote.Visibility = Visibility.Visible;
                ProcessList.ItemsSource = Array.Empty<Row>();
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

    private static IEnumerable<Row> Rank(IReadOnlyList<ProcessSnapshot>? source, bool byCpu)
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
                WorkingSetMB = p.WorkingSetBytes / (1024f * 1024f)
            });
        }
        return (byCpu
                ? rows.OrderByDescending(r => r.CpuPercent)
                : rows.OrderByDescending(r => r.WorkingSetMB))
            .Take(TopN)
            .Select(r =>
            {
                r.Display = r.Count > 1 ? $"{r.Name} ({r.Count})" : r.Name;
                r.FormattedValue = byCpu
                    ? $"{r.CpuPercent:F0}%"
                    : $"{r.WorkingSetMB:F0}";
                r.AccentBrush = byCpu ? CpuAccent : RamAccent;
                // Nothing interesting to rank if the value rounds to zero —
                // dim it so the eye doesn't waste time on it.
                if ((byCpu && r.CpuPercent < 0.5f) || (!byCpu && r.WorkingSetMB < 1f))
                    r.AccentBrush = MutedAccent;
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
        public string Display { get; set; } = "";
        public string FormattedValue { get; set; } = "";
        public Brush AccentBrush { get; set; } = Brushes.White;
    }
}
